//
// POLYGONAL AREA LIGHTS
// The MIT License (MIT)
// Copyright (c) 2016 ALEXANDER PETRYAEV
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is furnished 
// to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, 
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A 
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT 
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION 
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum IrradianceMapResolution
{
	_16x16,
	_32x32,
	_64x64,
	_96x96,
	_128x128,
	_192x192,
	_256x256
};

static public class AngularMeter
{	
	public static float GetAngle(this Vector3 thisVector, Vector3 otherVector)
	{
		return GetAngle( thisVector, otherVector, Vector3.Cross( thisVector, otherVector ).normalized );
	}

	public static float GetAngle(this Vector3 thisVector, Vector3 otherVector, Vector3 axisVector)
	{
		float angle = Vector3.Dot( thisVector, otherVector );
		angle = ( angle > 1 ) ? ( 0 ) : ( ( angle < -1 ) ? ( Mathf.Rad2Deg * Mathf.PI ) : ( angle = Mathf.Rad2Deg * Mathf.Acos( angle ) ) );
		Vector3 crossTestAxis = Vector3.Cross( thisVector, otherVector );
		if( !Mathf.Approximately( crossTestAxis.sqrMagnitude, 0.0f ) )
		{
			angle = angle * Mathf.Sign( Vector3.Dot( crossTestAxis, axisVector ) );
		}
		return angle;
	}
}

[RequireComponent(typeof(MeshAreaLight))]
public class IrradianceTransfer : MonoBehaviour
{
	public RenderTexture AlbedoBuffer;
	public RenderTexture DepthBuffer;
	public RenderTexture NormalBuffer;
	public RenderTexture MergeBuffer;
	public RenderTexture GeometryBuffer;
	public RenderTexture IlluminationBuffer;

	#region Constants
	const float IlluminationBufferIntensityScale = 0.125f;
	const int   IrradiancePolygonSmoothing = 4;
	const float OutlineOffset = 0.25f;
	#endregion

	#region EmbeddedTypes
	public struct PixelCoords
	{
		public int x;
		public int y;

		public PixelCoords(int x, int y)
		{
			this.x = x;
			this.y = y;
		}

		public static PixelCoords zero = new PixelCoords( 0,0 );
	};

	public class IrradiancePolygon : PolygonalAreaLight
	{
		public int   polygonIndex = 0;
		public int   totalPixels = 0;
		public float totalIllumination = 0.0f;
		public int   totalRed = 0;
		public int   totalGreen = 0;
		public int   totalBlue = 0;
	};

	/*public class IrradiancePolygons : Dictionary<int,IrradiancePolygon>
	{
	};*/
	#endregion

	#region PublicFields
	public IrradianceMapResolution Resolution = IrradianceMapResolution._32x32;

	[Range(0.0f, 2.0f)]
	public float BounceIntensityTreshold = 0.5f;

	[Range(0.0f, 2.0f)]
	public float IrradianceBias = 0.0f;

	[Range(0.0f, 5.0f)]
	public float IrradianceIntensityMultiplier = 1.0f;
	#endregion

	#region PrivateFields
	Vector2               _irradianceMapBufferResolution = Vector2.zero;
	Vector2               _irradianceMapInvBufferResolution = Vector2.zero;
	Vector4               _irradianceMapPixelSize = Vector4.zero;
	MeshAreaLight         _meshAreaLight;
	Shader                _albedoBufferShader;
	Shader                _depthBufferShader;
	Shader                _normalBufferShader;
	Shader                _geometryBufferShader;
	Shader                _mergeBufferShader;
	Shader                _illuminationBufferShader;
	Material              _mergeBufferMaterial;
	Camera                _offscreenCamera;
	RenderTexture         _albedoBuffer;
	RenderTexture         _depthBuffer;
	RenderTexture         _normalBuffer;
	RenderTexture         _mergeBuffer;
	RenderTexture         _geometryBuffer;
	RenderTexture         _illuminationBuffer;
	Texture2D             _transferBuffer;
	Color32[]             _albedoBufferPixels = new Color32[0];
	Color32[]             _depthBufferPixels = new Color32[0];
	Color32[]             _mergeBufferPixels = new Color32[0];
	Color32[]             _illuminationBufferPixels = new Color32[0];
	int                   _numPolygons = 0;
	int[]                 _polygonMap = new int[0];
	int[]                 _polygonMergeMap = new int[0];
	bool[]                _thresholdMap = new bool[0];
	ushort[]              _contourMap = new ushort[0];
	IrradiancePolygon[]   _irradiancePolygons = new IrradiancePolygon[0];
	#endregion

	#region MarchingSquares
	#endregion

	#region MonoBehaviour
	void Awake()
	{
		string[] s = Resolution.ToString().Trim( '_' ).Split( 'x' );

		int width = 0;
		int height = 0;

		if( int.TryParse( s[0], out width ) && int.TryParse( s[1], out height ) )
		{
			_irradianceMapBufferResolution.Set( (float)(width), (float)(height) );
			_irradianceMapInvBufferResolution.Set( 1.0f/width, 1.0f/height );
			_irradianceMapPixelSize.Set( 1.0f/width, 1.0f/height, 0.0f, 0.0f );
		}
		else
		{
			Debug.LogError( "[IrradianceTransfer] Awake() unable to parse buffer resolution from enum value " + Resolution.ToString() + ", forced to 16x16" );
			_irradianceMapBufferResolution.Set( 16.0f, 16.0f );
			_irradianceMapInvBufferResolution.Set( 1.0f/16.0f, 1.0f/16.0f );
			_irradianceMapPixelSize.Set( 1.0f/16.0f, 1.0f/16.0f, 0.0f, 0.0f );
		}
	}

	void OnDestroy()
	{
		for( int i=0; i<_irradiancePolygons.Length; i++ )
		{
			if( _irradiancePolygons[i] != null )
			{
				PALBatchBuilder.UnregisterPolygonalAreaLight( _irradiancePolygons[i] );
			}
		}
	}

	void Start()
	{
		_meshAreaLight = GetComponent<MeshAreaLight>();
		Bounds polygonBounds = _meshAreaLight.PolygonBounds;
		Vector3 x = _meshAreaLight.transform.localToWorldMatrix.MultiplyVector( Vector3.right ) * polygonBounds.extents.x;
		Vector3 y = _meshAreaLight.transform.localToWorldMatrix.MultiplyVector( Vector3.up ) * polygonBounds.extents.y;
		Vector3 z = _meshAreaLight.transform.localToWorldMatrix.MultiplyVector( Vector3.forward ) * polygonBounds.extents.z;

		GameObject offscreenCameraObject = new GameObject("OffscreenCamera");
		offscreenCameraObject.hideFlags = HideFlags.HideAndDontSave;// HideFlags.DontSave; // 
		offscreenCameraObject.transform.parent = this.transform;
		offscreenCameraObject.transform.localPosition = Vector3.zero;
		offscreenCameraObject.transform.localRotation = Quaternion.identity;
		float angle = offscreenCameraObject.transform.forward.GetAngle( _meshAreaLight.PolygonNormal );
		offscreenCameraObject.transform.Rotate( Vector3.Cross( offscreenCameraObject.transform.forward, _meshAreaLight.PolygonNormal ), angle, Space.World );

		_offscreenCamera = offscreenCameraObject.AddComponent<Camera>();

		_albedoBufferShader = Shader.Find( "PAL/Opaque" );
		_depthBufferShader = Shader.Find( "PAL/DepthBuffer" );
		_normalBufferShader = Shader.Find( "PAL/NormalBuffer" );
		_mergeBufferShader = Shader.Find( "PAL/MergeBuffer" );
		_illuminationBufferShader = Shader.Find( "PAL/IlluminationBuffer" );
		_geometryBufferShader = Shader.Find( "PAL/GeometryBuffer" );
		_mergeBufferMaterial = new Material( _mergeBufferShader );

		int bufferWidth = (int)(_irradianceMapBufferResolution.x);
		int bufferHeight = (int)(_irradianceMapBufferResolution.x);

		_albedoBuffer = new RenderTexture( bufferWidth, bufferHeight, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear );
		_depthBuffer = new RenderTexture( bufferWidth, bufferHeight, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear );
		_normalBuffer = new RenderTexture( bufferWidth, bufferHeight, 32, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear );
		_mergeBuffer = new RenderTexture( bufferWidth, bufferHeight, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear );
		_geometryBuffer = new RenderTexture( bufferWidth, bufferHeight, 32, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear );
		_illuminationBuffer = new RenderTexture( bufferWidth, bufferHeight, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear );
		_transferBuffer = new Texture2D( bufferWidth, bufferHeight, TextureFormat.ARGB32, false );

		_albedoBuffer.filterMode = FilterMode.Point;
		_depthBuffer.filterMode = FilterMode.Point;
		_normalBuffer.filterMode = FilterMode.Point;
		_mergeBuffer.filterMode = FilterMode.Point;
		_geometryBuffer.filterMode = FilterMode.Point;
		_illuminationBuffer.filterMode = FilterMode.Point;

		_offscreenCamera.nearClipPlane = 0.1f;
		_offscreenCamera.farClipPlane = 50.0f;
		_offscreenCamera.fieldOfView = 150.0f;
		_offscreenCamera.enabled = false;

		_polygonMap = new int[bufferWidth*bufferHeight];
		_polygonMergeMap = new int[bufferWidth*bufferHeight];
		_thresholdMap = new bool[bufferWidth*bufferHeight];
		_contourMap = new ushort[(bufferWidth-1)*(bufferHeight-1)];

		AlbedoBuffer = _albedoBuffer;
		DepthBuffer = _depthBuffer;
		NormalBuffer = _normalBuffer;
		MergeBuffer = _mergeBuffer;
		GeometryBuffer = _geometryBuffer;
		IlluminationBuffer = _illuminationBuffer;
	}

	void PrintPolygonMap(string name)
	{
		System.IO.StreamWriter streamWriter = new System.IO.StreamWriter( "PolygonMap_" + name + ".txt", false, System.Text.Encoding.ASCII );

		int bufferHeight = _depthBuffer.height;
		int bufferWidth = _depthBuffer.width;

		int fieldSize = _numPolygons.ToString().Length;
		string fieldFormat = "{0," + fieldSize.ToString() + "} ";

		for( int y=bufferHeight-1; y>=0; y-- )
		{
			for( int x=0; x<bufferWidth; x++ )
			{
				if( _polygonMap[y*bufferWidth + x] >= 0 )
				{
					streamWriter.Write( string.Format( fieldFormat, _polygonMap[y*bufferWidth + x] ) );
				}
				else
				{
					streamWriter.Write( string.Format( fieldFormat, " "  ) );
				}
			}
			streamWriter.Write( "\n" );
		}

		streamWriter.Close();
	}

	void PrintThresholdMap(string name)
	{
		System.IO.StreamWriter streamWriter = new System.IO.StreamWriter( "ThresholdMap_" + name + ".txt", false, System.Text.Encoding.ASCII );

		int bufferHeight = _depthBuffer.height;
		int bufferWidth = _depthBuffer.width;

		for( int y=bufferHeight-1; y>=0; y-- )
		{
			for( int x=0; x<bufferWidth; x++ )
			{
				if( _thresholdMap[y*bufferWidth + x] )
				{
					streamWriter.Write( "1" );
				}
				else
				{
					streamWriter.Write( "0" );
				}
			}
			streamWriter.Write( "\n" );
		}

		streamWriter.Close();
	}

	void PrintContourMap(string name)
	{
		System.IO.StreamWriter streamWriter = new System.IO.StreamWriter( "ContourMap_" + name + ".txt", false, System.Text.Encoding.ASCII );

		int bufferHeight = _depthBuffer.height-1;
		int bufferWidth = _depthBuffer.width-1;

		string[] ContourVariants = new string[]{ "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "A", "B", "C", "D", "E", "F" };

		for( int y=bufferHeight-1; y>=0; y-- )
		{
			for( int x=0; x<bufferWidth; x++ )
			{
				var contourValue = _contourMap[y*bufferWidth + x];
				if( contourValue >= 0 && contourValue < 16 )
				{
					streamWriter.Write( ContourVariants[contourValue] );
				}
				else
				{
					streamWriter.Write( " " );
				}

			}
			streamWriter.Write( "\n" );
		}

		streamWriter.Close();
	}

	void Update()
	{	
		_meshAreaLight.PrepareIrradianceTransfer();

		Shader.EnableKeyword( "_PAL_ALBEDO" );
		_offscreenCamera.backgroundColor = new Color( 0, 0, 0, 0 );
		_offscreenCamera.targetTexture = _albedoBuffer;
		_offscreenCamera.RenderWithShader( _albedoBufferShader, "RenderType" );
		Shader.DisableKeyword( "_PAL_ALBEDO" );

		_offscreenCamera.backgroundColor = new Color( 1, 1, 1, 1 );
		_offscreenCamera.targetTexture = _depthBuffer;
		_offscreenCamera.RenderWithShader( _depthBufferShader, "" );

		_offscreenCamera.backgroundColor = new Color( 0, 0, 0, 0 );
		_offscreenCamera.targetTexture = _normalBuffer;
		_offscreenCamera.RenderWithShader( _normalBufferShader, "" );

		_offscreenCamera.backgroundColor = new Color( 0, 0, 0, 0 );
		_offscreenCamera.targetTexture = _geometryBuffer;
		_offscreenCamera.RenderWithShader( _geometryBufferShader, "" ); 

		_mergeBufferMaterial.SetTexture( "_NormalBuffer", _normalBuffer );
		_mergeBufferMaterial.SetTexture( "_GeometryBuffer", _geometryBuffer );
		_mergeBufferMaterial.SetVector( "_PixelSize", _irradianceMapPixelSize );
		Graphics.Blit( _normalBuffer, _mergeBuffer, _mergeBufferMaterial, 0 );

		Shader.SetGlobalFloat( "_IlluminationScale", IlluminationBufferIntensityScale );
		_offscreenCamera.backgroundColor = new Color( 0, 0, 0, 0 );
		_offscreenCamera.targetTexture = _illuminationBuffer;
		_offscreenCamera.RenderWithShader( _illuminationBufferShader, "" ); 

		_offscreenCamera.targetTexture = _mergeBuffer;

		int bufferHeight = _depthBuffer.height;
		int bufferWidth = _depthBuffer.width;

		var prevRenderTexture = RenderTexture.active;
		RenderTexture.active = _albedoBuffer;
		_transferBuffer.ReadPixels( new Rect(0, 0, bufferWidth, bufferHeight), 0, 0, false );
		_transferBuffer.Apply();
		RenderTexture.active = prevRenderTexture;
		_albedoBufferPixels = _transferBuffer.GetPixels32();

		prevRenderTexture = RenderTexture.active;
		RenderTexture.active = _depthBuffer;
		_transferBuffer.ReadPixels( new Rect(0, 0, bufferWidth, bufferHeight), 0, 0, false );
		_transferBuffer.Apply();
		RenderTexture.active = prevRenderTexture;
		_depthBufferPixels = _transferBuffer.GetPixels32();

		prevRenderTexture = RenderTexture.active;
		RenderTexture.active = _mergeBuffer;
		_transferBuffer.ReadPixels( new Rect(0, 0, bufferWidth, bufferHeight), 0, 0, false );
		_transferBuffer.Apply();
		RenderTexture.active = prevRenderTexture;
		_mergeBufferPixels = _transferBuffer.GetPixels32();

		prevRenderTexture = RenderTexture.active;
		RenderTexture.active = _illuminationBuffer;
		_transferBuffer.ReadPixels( new Rect(0, 0, bufferWidth, bufferHeight), 0, 0, false );
		_transferBuffer.Apply();
		RenderTexture.active = prevRenderTexture;
		_illuminationBufferPixels = _transferBuffer.GetPixels32();

		// decoding constants (from UnityCG.cginc)

		const float kDecodeGreenFactor = 1/255.0f; 
		const float kDecodeBlueFactor = 1/65025.0f;
		const float kDecodeAlphaFactor = 1/16581375.0f;

		// step 1 : map pixels to polygons

		Color32 depthPixel32;
		Color32 mergePixel32;
		Color32 irradiancePixel32;
		Color32 albedoPixel32;

		_numPolygons = 0;

		for( int y=0; y<bufferHeight; y++ )
		{
			for( int x=0; x<bufferWidth; x++ )
			{
				int pixelIndex = y*bufferWidth + x;
				int leftPixelIndex = ( x > 0 ) ? pixelIndex-1 : -1;
				int lowerPixelIndex = ( y > 0 ) ? pixelIndex-bufferWidth : -1;

				int leftPolygonIndex = ( leftPixelIndex >= 0 ) ? _polygonMap[leftPixelIndex] : -1;
				int lowerPolygonIndex = ( lowerPixelIndex >= 0 ) ? _polygonMap[lowerPixelIndex] : -1;

				depthPixel32 = _depthBufferPixels[pixelIndex];
				mergePixel32 = _mergeBufferPixels[pixelIndex];

				bool leftPixelIsMergeable = ( leftPixelIndex >= 0 ) ? ( mergePixel32.r > 0 ) : ( false );
				bool lowerPixelIsMergeable = ( lowerPixelIndex >= 0 ) ? ( mergePixel32.g > 0 ) : ( false );

				if( depthPixel32.r == 0xFF && depthPixel32.g == 0xFF && depthPixel32.b == 0xFF && depthPixel32.a == 0xFF )
				{
					_polygonMap[pixelIndex] = -1;
				}
				else
				{
					if( leftPolygonIndex >= 0 )
					{
						if( leftPixelIsMergeable )
						{
							_polygonMap[pixelIndex] = leftPolygonIndex;

							if( lowerPixelIsMergeable && leftPolygonIndex > lowerPolygonIndex )
							{
								if( _polygonMergeMap[leftPolygonIndex] == leftPolygonIndex )
								{
									_polygonMergeMap[leftPolygonIndex] = lowerPolygonIndex;
								}
							}
						}
						else if( lowerPixelIsMergeable )
						{
							_polygonMap[pixelIndex] = lowerPolygonIndex;
						}
						else
						{
							_polygonMap[pixelIndex] = _numPolygons;
							_polygonMergeMap[_numPolygons] = _numPolygons;
							_numPolygons++;
						}
					}
					else if( lowerPolygonIndex >= 0 )
					{
						if( lowerPixelIsMergeable )
						{
							_polygonMap[pixelIndex] = lowerPolygonIndex;
						}
						else
						{
							_polygonMap[pixelIndex] = _numPolygons;
							_polygonMergeMap[_numPolygons] = _numPolygons;
							_numPolygons++;
						}
					}
					else
					{
						_polygonMap[pixelIndex] = _numPolygons;
						_polygonMergeMap[_numPolygons] = _numPolygons;
						_numPolygons++;
					}
				}
			}
		}

		// step 2 : collapse hierarchies of merge map and merge polygons

		for( int i=0; i<_numPolygons; i++ )
		{
			int indexToCollapse = _polygonMergeMap[i];
			while( _polygonMergeMap[indexToCollapse] != indexToCollapse )
			{
				indexToCollapse = _polygonMergeMap[indexToCollapse];
			}

			if( i != indexToCollapse )
			{
				_polygonMergeMap[i] = indexToCollapse;
			}
		}

		for( int i=0; i<_polygonMap.Length; i++ )
		{
			int polygonIndex = _polygonMap[i];
			if( polygonIndex >= 0 )
			{
				_polygonMap[i] = _polygonMergeMap[polygonIndex];
			}
		}

		// step 3 : filter out pixels illuminated above the given threshold of bounce intensity

		Color irradiancePixel = Color.black;

		for( int i=0; i<_irradiancePolygons.Length; i++ )
		{
			if( _irradiancePolygons[i] != null )
			{
				PALBatchBuilder.UnregisterPolygonalAreaLight( _irradiancePolygons[i] );
			}
		}

		if( _irradiancePolygons.Length < _numPolygons )
		{
			_irradiancePolygons = new IrradiancePolygon[_numPolygons];
		}

		for( int i=0; i<_irradiancePolygons.Length; i++ )
		{
			_irradiancePolygons[i] = null;
		}

		for( int i=0; i<_polygonMap.Length; i++ )
		{
			int polygonIndex = _polygonMap[i];
			if( polygonIndex >= 0 )
			{
				albedoPixel32 = _albedoBufferPixels[i];
				irradiancePixel32 = _illuminationBufferPixels[i];
				irradiancePixel.r = irradiancePixel32.r / 255.0f; 
				irradiancePixel.g = irradiancePixel32.g / 255.0f; 
				irradiancePixel.b = irradiancePixel32.b / 255.0f; 
				irradiancePixel.a = irradiancePixel32.a / 255.0f; 
				float illumination = irradiancePixel.r + irradiancePixel.g * kDecodeGreenFactor + irradiancePixel.b * kDecodeBlueFactor + irradiancePixel.a * kDecodeAlphaFactor;
				illumination *= 1.0f / IlluminationBufferIntensityScale;
				if( illumination < BounceIntensityTreshold )
				{
					_polygonMap[i] = -1;
				}
				else
				{
					IrradiancePolygon irradiancePolygon = _irradiancePolygons[polygonIndex];
					if( irradiancePolygon == null )
					{
						irradiancePolygon = new IrradiancePolygon();
						irradiancePolygon.polygonIndex = polygonIndex;
						_irradiancePolygons[polygonIndex] = irradiancePolygon;
					}
					irradiancePolygon.totalPixels += 1;
					irradiancePolygon.totalIllumination += illumination;
					irradiancePolygon.totalRed += albedoPixel32.r;
					irradiancePolygon.totalGreen += albedoPixel32.g;
					irradiancePolygon.totalBlue += albedoPixel32.b;
				}
			}
		}

		// PrintPolygonMap( "Merged" );

		// step 4 : build isolines using marching squares

		const ushort Zero = 0;
		const ushort BitMask0 = 8;
		const ushort BitMask1 = 4;
		const ushort BitMask2 = 2;
		const ushort BitMask3 = 1;

		Plane polygonPlane = new Plane();

		float farClipPlane = _offscreenCamera.farClipPlane;

		int numPlanePixelCoords = 0;
		PixelCoords[] planePixels = new PixelCoords[] { PixelCoords.zero, PixelCoords.zero, PixelCoords.zero };
		Vector3[] worldSpacePixelPos = new Vector3[] { Vector3.zero, Vector3.zero, Vector3.zero };
		Vector3 viewPortSpacePixelPos = Vector3.zero;
		Vector3 worldSpacePlaneNormal = Vector3.zero;
		Color depthPixel = Color.black;
		Color averageAlbedo = Color.black;

		for( int polygonIndex=0; polygonIndex<_irradiancePolygons.Length; polygonIndex++ )
		{
			var irradiancePolygon = _irradiancePolygons[polygonIndex];
			if( irradiancePolygon == null ) continue;

			// fill threshold map & calculate world space normal of polygon

			numPlanePixelCoords = 0;
			for( int i=0; i<_polygonMap.Length; i++ )
			{
				int x = i % bufferWidth;
				int y = i / bufferWidth;

				if( _polygonMap[i] == polygonIndex && x > 0 && y > 0 && x < bufferWidth-1 && y < bufferHeight-1 )
				{
					_thresholdMap[i] = true;

					if( numPlanePixelCoords < 3 )
					{
						planePixels[numPlanePixelCoords].x = x;
						planePixels[numPlanePixelCoords].y = y;

						int pixelIndex = i;
						depthPixel32 = _depthBufferPixels[pixelIndex];
						depthPixel.r = depthPixel32.r / 255.0f;
						depthPixel.g = depthPixel32.g / 255.0f;
						depthPixel.b = depthPixel32.b / 255.0f;
						depthPixel.a = depthPixel32.a / 255.0f;
						viewPortSpacePixelPos.x = ( planePixels[numPlanePixelCoords].x + 0.5f ) * _irradianceMapInvBufferResolution.x;
						viewPortSpacePixelPos.y = ( planePixels[numPlanePixelCoords].y + 0.5f ) * _irradianceMapInvBufferResolution.y;
						viewPortSpacePixelPos.z = depthPixel.r + depthPixel.g * kDecodeGreenFactor + depthPixel.b * kDecodeBlueFactor + depthPixel.a * kDecodeAlphaFactor;
						viewPortSpacePixelPos.z = viewPortSpacePixelPos.z * farClipPlane;
						worldSpacePixelPos[numPlanePixelCoords] = _offscreenCamera.ViewportToWorldPoint( viewPortSpacePixelPos );

						numPlanePixelCoords++;
						if( numPlanePixelCoords == 3 )
						{
							if( ( planePixels[0].x == planePixels[1].x && planePixels[1].x == planePixels[2].x ) ||
								( planePixels[0].y == planePixels[1].y && planePixels[1].y == planePixels[2].y ) )
							{
								numPlanePixelCoords--;
							}
							else
							{
								Vector3 worldSpacePlaneTangent0 = ( worldSpacePixelPos[1] - worldSpacePixelPos[0] ).normalized;
								Vector3 worldSpacePlaneTangent1 = ( worldSpacePixelPos[2] - worldSpacePixelPos[0] ).normalized;
								if( Mathf.Abs( Vector3.Dot( worldSpacePlaneTangent0, worldSpacePlaneTangent1 ) ) > 0.9f )
								{
									numPlanePixelCoords--;
								}
							}
						}
					}
				}
				else
				{
					_thresholdMap[i] = false;
				}
			}

			if( numPlanePixelCoords == 3 )
			{
				worldSpacePlaneNormal = Vector3.Cross( (worldSpacePixelPos[1]-worldSpacePixelPos[0]).normalized, (worldSpacePixelPos[2]-worldSpacePixelPos[0]).normalized ).normalized;
				worldSpacePlaneNormal *= -Mathf.Sign( Vector3.Dot( _offscreenCamera.transform.forward, worldSpacePlaneNormal ) );
				polygonPlane.SetNormalAndPosition( worldSpacePlaneNormal, worldSpacePixelPos[0] );
			}
			else
			{
				// edge case : all the pixels lay on the same line
				continue;
			}

			// PrintThresholdMap( polygonIndex.ToString() );

			// fill contour map

			int leftMostXCoord = -1;
			int leftMostContourIndex = -1;
			int numContourCells = 0;
			for( int y=0; y<bufferHeight-1; y++ )
			{
				for( int x=0; x<bufferWidth-1; x++ )
				{
					int contourIndex = y*(bufferWidth-1)+x;

					int pixelIndex0 = (y+1)*bufferWidth + x;
					int pixelIndex1 = (y+1)*bufferWidth + (x+1);
					int pixelIndex2 = y*bufferWidth + (x+1);
					int pixelIndex3 = y*bufferWidth + x;

					bool thresholdValue0 = _thresholdMap[pixelIndex0];
					bool thresholdValue1 = _thresholdMap[pixelIndex1];
					bool thresholdValue2 = _thresholdMap[pixelIndex2];
					bool thresholdValue3 = _thresholdMap[pixelIndex3];

					ushort lookupIndex = 0;
					lookupIndex |= ( thresholdValue0 ? BitMask0 : Zero );
					lookupIndex |= ( thresholdValue1 ? BitMask1 : Zero );
					lookupIndex |= ( thresholdValue2 ? BitMask2 : Zero );
					lookupIndex |= ( thresholdValue3 ? BitMask3 : Zero );

					_contourMap[contourIndex] = lookupIndex;

					if( lookupIndex > 0 && lookupIndex < 15 )
					{
						numContourCells += ( lookupIndex == 5 || lookupIndex == 10 ) ? 2 : 1;
						if( leftMostContourIndex == -1 || ( leftMostContourIndex != -1 && leftMostXCoord > x ) )
						{
							leftMostContourIndex = contourIndex;
							leftMostXCoord = x;
						}
					}
				}
			}

			if( leftMostContourIndex < 0 )
			{
				Debug.LogError( "[IrradianceTransfer] leftMostContourIndex == " + leftMostContourIndex + "!" );
				continue;
			}

			// allocate array for vertices
			irradiancePolygon.Vertices = new Vector3[numContourCells];

			// PrintContourMap( polygonIndex.ToString() );

			// transform contour to world space

			const int Start = 0;
			const int Left = 1;
			const int Up = 2;
			const int Right = 3;
			const int Down = 4;
			const int Stop = -1;

			int numVertices = 0;
			int currentContourIndex = leftMostContourIndex;
			int move = Start;

			Vector3 viewPortSpaceVertexPos = Vector3.zero;

			while( move != Stop )
			{
				viewPortSpaceVertexPos.x = currentContourIndex % (bufferWidth-1) + 0.5f;
				viewPortSpaceVertexPos.y = currentContourIndex / (bufferWidth-1) + 0.5f;

				ushort currentContourValue = _contourMap[currentContourIndex];

				switch( currentContourValue )
				{
				case 1:
					viewPortSpaceVertexPos.x += OutlineOffset;
					move = ( move == Right ) ? Down : Stop;
					break;
				case 2:
					viewPortSpaceVertexPos.x += 1;
					viewPortSpaceVertexPos.y += OutlineOffset;
					move = ( move == Up || move == Start ) ? Right : Stop;
					break;
				case 3:
					viewPortSpaceVertexPos.x += 1;
					viewPortSpaceVertexPos.y += OutlineOffset;
					move = ( move == Right ) ? Right : Stop;
					break;
				case 4:
					viewPortSpaceVertexPos.x += 1 - OutlineOffset;
					viewPortSpaceVertexPos.y += 1;
					move = ( move == Left || move == Start ) ? Up : Stop;
					break;
				case 5:
					viewPortSpaceVertexPos.x += ( move == Right ) ? OutlineOffset : ( 1 - OutlineOffset );
					viewPortSpaceVertexPos.y += ( move == Right ) ? 1 : 0;
					move = ( move == Right ) ? Up : ( ( move == Left ) ? Down : Stop );
					break;
				case 6:
					viewPortSpaceVertexPos.x += 1 - OutlineOffset;
					viewPortSpaceVertexPos.y += 1;
					move = ( move == Up || move == Start ) ? Up : Stop;
					break;
				case 7:
					viewPortSpaceVertexPos.x += OutlineOffset;
					viewPortSpaceVertexPos.y += 1;
					move = ( move == Right ) ? Up : Stop;
					break;
				case 8:
					viewPortSpaceVertexPos.y += 1 - OutlineOffset;
					move = ( move == Down ) ? Left : Stop;
					break;
				case 9:
					viewPortSpaceVertexPos.x += OutlineOffset;
					move = ( move == Down ) ? Down : Stop;
					break;
				case 10:
					viewPortSpaceVertexPos.x += ( move == Down ) ? 1 : 0;
					viewPortSpaceVertexPos.y += ( move == Down ) ? ( 1 - OutlineOffset ) : OutlineOffset;
					move = ( move == Down ) ? Right : ( ( move == Up ) ? Left : Stop );
					break;
				case 11:
					viewPortSpaceVertexPos.x += 1;
					viewPortSpaceVertexPos.y += 1 - OutlineOffset;
					move = ( move == Down ) ? Right : Stop;
					break;
				case 12:
					viewPortSpaceVertexPos.y += 1 - OutlineOffset;
					move = ( move == Left ) ? Left : Stop;
					break;
				case 13:
					viewPortSpaceVertexPos.x += 1 - OutlineOffset;
					move = ( move == Left ) ? Down : Stop;
					break;
				case 14:
					viewPortSpaceVertexPos.y += OutlineOffset;
					move = ( move == Up ) ? Left : Stop;
					break;
				default:
					move = Stop;
					break;
				};

				switch( move )
				{
				case Left:
					currentContourIndex = ( currentContourIndex % ( bufferWidth-1 ) > 0 ) ? currentContourIndex-1 : -1;
					break;
				case Up:
					currentContourIndex = ( currentContourIndex / ( bufferWidth-1 ) < ( bufferHeight-2 ) ) ? currentContourIndex+bufferWidth-1 : -1;
					break;
				case Right:
					currentContourIndex = ( currentContourIndex % ( bufferWidth-1 ) < ( bufferWidth-2 ) ) ? currentContourIndex+1 : -1;
					break;
				case Down:
					currentContourIndex = ( currentContourIndex / ( bufferWidth-1 ) > 0 ) ? currentContourIndex-bufferWidth+1 : -1;
					break;
				default:
					move = Stop;
					break;
				}

				if( currentContourIndex == leftMostContourIndex || currentContourIndex < 0 )
				{
					move = Stop;
				}

				viewPortSpaceVertexPos.x *= _irradianceMapInvBufferResolution.x;
				viewPortSpaceVertexPos.y *= _irradianceMapInvBufferResolution.y;

				Ray ray = _offscreenCamera.ViewportPointToRay( viewPortSpaceVertexPos );
				float distance = float.MaxValue;
				if( polygonPlane.Raycast( ray, out distance ) )
				{
					irradiancePolygon.Vertices[numVertices] = ray.origin + ray.direction * distance;
					numVertices++;
				}
			}

			if( irradiancePolygon.Vertices.Length > numVertices )
			{
				System.Array.Resize<Vector3>( ref irradiancePolygon.Vertices, numVertices );
			}

			// smooth
			Vector3[] smoothVertices = new Vector3[irradiancePolygon.Vertices.Length];
			float planeFactor = 2 - Mathf.Abs( Vector3.Dot( _offscreenCamera.transform.forward, polygonPlane.normal ) );
			int numSmoothSteps = (int)(IrradiancePolygonSmoothing * planeFactor);
			for( int i=0; i<numSmoothSteps; i++ )
			{
				for( int index=0; index<irradiancePolygon.Vertices.Length; index++ )
				{
					int prevIndex = ( index == 0 ) ? irradiancePolygon.Vertices.Length-1 : index-1;
					int nextIndex = ( index == irradiancePolygon.Vertices.Length-1 ) ? 0 : index+1;
					smoothVertices[index] = ( irradiancePolygon.Vertices[prevIndex] + irradiancePolygon.Vertices[index] + irradiancePolygon.Vertices[nextIndex] ) / 3;
				}
				for( int index=0; index<irradiancePolygon.Vertices.Length; index++ )
				{
					irradiancePolygon.Vertices[index] = smoothVertices[index];
				}
			}

			// reduce semi-parallel edges

			const float MinEdgeAngle = 15.0f;
			const float ReductionAngle = 1.0f;

			bool[] vertexFlags = new bool[irradiancePolygon.Vertices.Length];
			for( int index=0; index<vertexFlags.Length; index++ ) 
			{
				vertexFlags[index] = true;
			}

			float reductionAngle = 0.0f;
			float reductionCosAngle = Mathf.Cos( reductionAngle * Mathf.Deg2Rad );
			numVertices = irradiancePolygon.Vertices.Length;
			while( reductionAngle < MinEdgeAngle && numVertices > 4 )
			{
				int prevIndex = -1;
				for( int index=0; index<irradiancePolygon.Vertices.Length; index++ )
				{
					if( vertexFlags[index] )
					{
						prevIndex = index;
						break;
					}
				}
				if( prevIndex < 0 )
				{
					Debug.LogError( "prevIndex < 0" );
					break;
				}

				int currIndex = -1;
				for( int index=prevIndex+1; index<irradiancePolygon.Vertices.Length; index++ )
				{
					if( vertexFlags[index] )
					{
						currIndex = index;
						break;
					}
				}
				if( currIndex < 0 )
				{
					Debug.LogError( "currIndex < 0" );
					break;
				}

				int nextIndex = -1;
				for( int index=currIndex+1; index<irradiancePolygon.Vertices.Length; index++ )
				{
					if( vertexFlags[index] )
					{
						nextIndex = index;
						break;
					}
				}
				if( nextIndex < 0 )
				{
					Debug.LogError( "nextIndex < 0" );
					break;
				}

				do
				{
					float cosAngle = Vector3.Dot( ( irradiancePolygon.Vertices[currIndex] - irradiancePolygon.Vertices[prevIndex] ).normalized, ( irradiancePolygon.Vertices[nextIndex] - irradiancePolygon.Vertices[currIndex] ).normalized );
					if( cosAngle >= reductionCosAngle )
					{
						vertexFlags[currIndex] = false;
						numVertices--;
					}

					if( nextIndex < prevIndex )
					{
						break;
					}

					prevIndex = currIndex;
					currIndex = nextIndex;
					nextIndex = -1;

					for( int index=currIndex+1; index<irradiancePolygon.Vertices.Length; index++ )
					{
						if( vertexFlags[index] )
						{
							nextIndex = index;
							break;
						}
					}

					if( nextIndex < 0 )
					{
						for( int index=0; index<prevIndex; index++ )
						{
							if( vertexFlags[index] )
							{
								nextIndex = index;
								break;
							}
						}

						if( nextIndex < 0 )
						{
							Debug.LogError( "nextIndex < 0 (in loop)" );
							break;
						}
					}
				}
				while( numVertices > 4 );

				reductionAngle += ReductionAngle;
				reductionCosAngle = Mathf.Cos( reductionAngle * Mathf.Deg2Rad );
			}

#if false
			irradiancePolygon.vertices = new Vector3[numVertices];
			int reducedIndex = 0;
			for( int index=0; index<smoothVertices.Length; index++ )
			{
				if( vertexFlags[index] )
				{
					irradiancePolygon.vertices[reducedIndex] = smoothVertices[index];
					reducedIndex++;
				}
			}
#else
			// combine rest of vertices

			irradiancePolygon.Vertices = new Vector3[numVertices];

			int originalIndex = 0;
			int reducedIndex = 0;
			while( originalIndex < vertexFlags.Length )
			{
				if( vertexFlags[originalIndex] ) 
				{
					int averagingStep = 0;
					Vector3 averageVertex = Vector3.zero;
					while( originalIndex < vertexFlags.Length && vertexFlags[originalIndex] )
					{
						Vector3 nextAverageVertex = ( averageVertex * averagingStep + smoothVertices[originalIndex] ) / ( averagingStep + 1 );

						if( averagingStep > 2 )
						{
							bool combinationIsValid = true;
							for( int index=originalIndex-averagingStep; index<=originalIndex; index++ )
							{
								float distanceToAverageVertex = Vector3.Distance( smoothVertices[index], nextAverageVertex );
								float distanceBetweenVertices = ( index < originalIndex ) ? Vector3.Distance( smoothVertices[index], smoothVertices[index+1] ) : Vector3.Distance( smoothVertices[index], smoothVertices[index-1] );
								if( distanceToAverageVertex > distanceBetweenVertices )
								{
									combinationIsValid = false;
									break;
								}
							}

							if( !combinationIsValid ) break;
						}

						averagingStep++;
						averageVertex = nextAverageVertex;
						originalIndex++;
						numVertices--;
						if( ( numVertices < 3 && reducedIndex < 1 ) || ( numVertices < 2 && reducedIndex < 2 ) ) break;
					}

					irradiancePolygon.Vertices[reducedIndex] = averageVertex;
					reducedIndex++;
				}
				else
				{
					originalIndex++;
				}
			}

			if( reducedIndex < irradiancePolygon.Vertices.Length )
			{
				System.Array.Resize<Vector3>( ref irradiancePolygon.Vertices, reducedIndex );
			}
#endif

			for( int index=0; index<irradiancePolygon.Vertices.Length; index++ )
			{
				irradiancePolygon.Vertices[index] += polygonPlane.normal * 0.001f;
			}

			averageAlbedo.r = ( irradiancePolygon.totalRed / irradiancePolygon.totalPixels ) / 255.0f;
			averageAlbedo.g = ( irradiancePolygon.totalGreen / irradiancePolygon.totalPixels ) / 255.0f;
			averageAlbedo.b = ( irradiancePolygon.totalGreen / irradiancePolygon.totalPixels ) / 255.0f;

			irradiancePolygon.Color = _meshAreaLight.Color * averageAlbedo;
			irradiancePolygon.Bias = IrradianceBias;
			irradiancePolygon.Intensity = irradiancePolygon.totalIllumination / irradiancePolygon.totalPixels * IrradianceIntensityMultiplier;
			irradiancePolygon.Normal = polygonPlane.normal;
			irradiancePolygon.ProjectionMode =_meshAreaLight.ProjectionMode;

			irradiancePolygon.Centroid = Vector3.zero;
			for( int index=0; index<irradiancePolygon.Vertices.Length; index++ )
			{
				irradiancePolygon.Centroid += irradiancePolygon.Vertices[index];
			}
			irradiancePolygon.Centroid *= 1.0f / irradiancePolygon.Vertices.Length;

			float circumradius = 0;
			for( int index=0; index<irradiancePolygon.Vertices.Length; index++ )
			{
				float distance = Vector3.Distance( irradiancePolygon.Centroid, irradiancePolygon.Vertices[index] );
				circumradius = Mathf.Max( circumradius, distance );
			}
			irradiancePolygon.Circumcircle.Set( irradiancePolygon.Centroid.x, irradiancePolygon.Centroid.y, irradiancePolygon.Centroid.z, circumradius );

			PALBatchBuilder.RegisterPolygonalAreaLight( irradiancePolygon );
			PALBatchBuilder.Update( irradiancePolygon );
		}
	}

	static Color[] gizmoColors = new Color[]
	{
		Color.red,
		Color.green,
		Color.blue,
		Color.yellow,
		Color.cyan,
		Color.magenta
	};

	void OnDrawGizmos()
	{
		for( int polygonIndex=0; polygonIndex<_irradiancePolygons.Length; polygonIndex++ )
		{
			var irradiancePolygon = _irradiancePolygons[polygonIndex];
			if( irradiancePolygon != null )
			{				
				if( irradiancePolygon.Vertices.Length > 1 )
				{
					Gizmos.color = gizmoColors[polygonIndex%gizmoColors.Length];
					for( int i=1; i<irradiancePolygon.Vertices.Length; i++ )
					{					
						Gizmos.DrawLine( irradiancePolygon.Vertices[i-1], irradiancePolygon.Vertices[i] );
						Gizmos.DrawCube( irradiancePolygon.Vertices[i-1], Vector3.one * 0.01f );
					}
					Gizmos.color = Color.white;
					Gizmos.DrawLine( irradiancePolygon.Vertices[0], irradiancePolygon.Vertices[irradiancePolygon.Vertices.Length-1] );
					Gizmos.DrawCube( irradiancePolygon.Vertices[0], Vector3.one * 0.01f );
				}
			}
		}
	}
	#endregion
}
