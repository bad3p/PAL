//
// POLYGONAL AREA LIGHTS
// The MIT License (MIT)
// Copyright (c) 2016-2017 ALEXANDER PETRYAEV
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

public class PolygonalAreaLight
{
	public Color Color = Color.black;
	public float Intensity = 0.0f;
	public float Bias = 0.0f;
	public bool Specular = false;
	public Vector3 Normal = Vector3.zero;
	public Vector3 Centroid = Vector3.zero;
	public Vector4 Circumcircle = Vector4.zero;
	public ProjectionMode ProjectionMode = ProjectionMode.Centered;
	public Vector3[] Vertices = new Vector3[0];
	public int BatchIndex = 0;
	public Vector4 SpecularBufferUVData = Vector4.zero;
};

static public class PALBatchBuilder
{
	#region Interface
	private static List<PolygonalAreaLight> _polygonalAreaLights = null;
	private static Material      _specularBufferMaterial = null;
	private static RenderTexture _specularBuffer = null;

	public static void RegisterPolygonalAreaLight(PolygonalAreaLight polygonalAreaLight)
	{
		if( _polygonalAreaLights == null )
		{
			_polygonalAreaLights = new List<PolygonalAreaLight>();
			_specularBufferMaterial = new Material( Shader.Find( "Hidden/PALSpecularBuffer" ) );
			_specularBuffer = new RenderTexture( 512, 512, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear );
			_specularBuffer.generateMips = false;
		}

		if( _polygonalAreaLights.Contains( polygonalAreaLight ) )
		{
			return;
		}
		_polygonalAreaLights.Add( polygonalAreaLight );

		// sort by number of vertices
		_polygonalAreaLights.Sort( (x,y) => { return (int)Mathf.Sign( x.Vertices.Length-y.Vertices.Length ); } );
	}

	public static void UnregisterPolygonalAreaLight(PolygonalAreaLight polygonalAreaLight)
	{
		if( _polygonalAreaLights == null ) return;

		if( !_polygonalAreaLights.Contains( polygonalAreaLight ) )
		{
			return;
		}

		_polygonalAreaLights.Remove( polygonalAreaLight );

		if( _polygonalAreaLights.Count == 0 )
		{
			_polygonalAreaLights = null;
			_specularBuffer.Release();
			_specularBuffer = null;
			_specularBufferMaterial = null;
		}
	}
	#endregion

	#region BatchBuilder
	static public int MaxNumPolygons 
	{
		get 
		{
			switch( SystemInfo.graphicsDeviceType )
			{
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGL2:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3:
				return 66;
			default:
				return 127;
			}
		} 
	}
	static public int MaxNumVertices
	{
		get 
		{
			switch( SystemInfo.graphicsDeviceType )
			{
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGL2:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3:
				return 198;
			default:
				return 1023;
			}
		} 
	}

	static public int NumPolygons { get; private set; }
	static public int NumVertices { get; private set; }
	static public Material SpecularBufferMaterial { get { return _specularBufferMaterial; } }
	static public RenderTexture SpecularBuffer { get { return _specularBuffer; } }

	static int       _lastFrameCount = -1;
	static int       _numAreaLightsUpdated = 0;
	static int[]     _polygonBufferPropertyId = new int[0];
	static int[]     _vertexBufferPropertyId = new int[0];
	static Vector4[] _dxPolygonBuffer = new Vector4[1023];
	static Vector4[] _dxVertexBuffer = new Vector4[1023];
	static Vector4[] _glPolygonDesc = new Vector4[66];
	static Vector4[] _glPolygonColor = new Vector4[66];
	static Vector4[] _glPolygonNormal = new Vector4[66];
	static Vector4[] _glPolygonTangent = new Vector4[66];
	static Vector4[] _glPolygonBitangent = new Vector4[66];
	static Vector4[] _glPolygonCentroid = new Vector4[66];
	static Vector4[] _glPolygonCircumcircle = new Vector4[66];
	static Vector4[] _glPolygonSpecularUV = new Vector4[66];
	static Vector4[] _glVertexBuffer = new Vector4[99];

	public static void CheckPropertyIds()
	{
		if( _polygonBufferPropertyId.Length == 0 )
		{
			_polygonBufferPropertyId = new int[_dxPolygonBuffer.Length];
			for( int i=0; i<_polygonBufferPropertyId.Length; i++ )
			{
				_polygonBufferPropertyId[i] = Shader.PropertyToID( "_PALPolygonBuffer" + i.ToString() );
			}
		}

		if( _vertexBufferPropertyId.Length == 0 )
		{
			_vertexBufferPropertyId = new int[_dxVertexBuffer.Length];
			for( int i=0; i<_vertexBufferPropertyId.Length; i++ )
			{
				_vertexBufferPropertyId[i] = Shader.PropertyToID( "_PALVertexBuffer" + i.ToString() );
			}
		}
	}

	public static void Update(PolygonalAreaLight updatedPolygonalAreaLight)
	{
		CheckPropertyIds();

		if( _lastFrameCount != Time.frameCount )
		{
			_lastFrameCount = Time.frameCount;
			_numAreaLightsUpdated = 1;

			RenderTexture.active = _specularBuffer;
			GL.Clear( true, true, Color.black );
			RenderTexture.active = null;
		}
		else
		{
			_numAreaLightsUpdated++;
		}

		if( _polygonalAreaLights == null )
		{
			return;
		}

		if( _numAreaLightsUpdated < _polygonalAreaLights.Count )
		{
			return;
		}

		int numSpecularPolygons = 0;

		NumPolygons = 0;
		NumVertices = 0;

		for( int i=0; i<_polygonalAreaLights.Count; i++ )
		{
			PolygonalAreaLight polygonalAreaLight = _polygonalAreaLights[i];
			if( polygonalAreaLight.Vertices.Length < 3 ) continue;

			if( ( NumPolygons + 1 ) < MaxNumPolygons && ( NumVertices + polygonalAreaLight.Vertices.Length ) < MaxNumVertices ) 
			{
				if( polygonalAreaLight.Specular ) 
				{
					numSpecularPolygons++;
				}
				NumPolygons++;
				NumVertices += polygonalAreaLight.Vertices.Length;
				polygonalAreaLight.BatchIndex = 1;
			}
			else
			{
				polygonalAreaLight.BatchIndex = 0;
			}
		}

		int specularBufferRowCapacity = Mathf.CeilToInt( Mathf.Sqrt( (float)(numSpecularPolygons) ) );

		int vertexBufferOffset = 0;

		int batchIndex = 0;
		int specularIndex = 0;
		for( int k=0; k<_polygonalAreaLights.Count; k++ )
		{
			PolygonalAreaLight polygonalAreaLight = _polygonalAreaLights[k];
			if( polygonalAreaLight.Vertices.Length < 3 ) 
			{
				continue;
			}
			if( polygonalAreaLight.BatchIndex == 0 )
			{
				continue;
			}
			polygonalAreaLight.BatchIndex = batchIndex;

			Vector3 circumcenter = polygonalAreaLight.Circumcircle;
			Vector3 tangent = new Vector3( polygonalAreaLight.Normal.y, polygonalAreaLight.Normal.z, -polygonalAreaLight.Normal.x );
			Vector3 bitangent = Vector3.Cross( tangent, polygonalAreaLight.Normal ).normalized;
			tangent = Vector3.Cross( polygonalAreaLight.Normal, bitangent ).normalized;

			for( int j=0; j<polygonalAreaLight.Vertices.Length; j++ )
			{
				_dxVertexBuffer[vertexBufferOffset+j] = polygonalAreaLight.Vertices[j];

				int compressedIndex = (vertexBufferOffset+j)/2;
				Vector3 vertexOriginOffset = polygonalAreaLight.Vertices[j] - circumcenter;
				Vector2 planarVertexCoords = new Vector2( Vector3.Dot( vertexOriginOffset, tangent ), Vector3.Dot( vertexOriginOffset, bitangent ) );

				switch( (vertexBufferOffset+j) % 2 )
				{
				case 0:
					_glVertexBuffer[compressedIndex].x = planarVertexCoords.x;
					_glVertexBuffer[compressedIndex].y = planarVertexCoords.y;
					break;
				default:
					_glVertexBuffer[compressedIndex].z = planarVertexCoords.x;
					_glVertexBuffer[compressedIndex].w = planarVertexCoords.y;
					break;
				}
			}

			// descriptor
			_dxPolygonBuffer[batchIndex*8].Set(
				vertexBufferOffset, 
				vertexBufferOffset + polygonalAreaLight.Vertices.Length,
				polygonalAreaLight.Intensity,
				polygonalAreaLight.Bias
			);

			// precalculated data
			Vector3 circumcenterOffset = polygonalAreaLight.Circumcircle;
			circumcenterOffset = circumcenterOffset - polygonalAreaLight.Vertices[0];
			_dxPolygonBuffer[batchIndex*8+1] = polygonalAreaLight.Color;
			_dxPolygonBuffer[batchIndex*8+2] = polygonalAreaLight.Normal;
			_dxPolygonBuffer[batchIndex*8+2].w = -Vector3.Dot( polygonalAreaLight.Normal, polygonalAreaLight.Vertices[0] );
			_dxPolygonBuffer[batchIndex*8+3] = tangent;
			_dxPolygonBuffer[batchIndex*8+4] = bitangent;
			_dxPolygonBuffer[batchIndex*8+5] = polygonalAreaLight.Centroid;
			_dxPolygonBuffer[batchIndex*8+6] = polygonalAreaLight.Circumcircle;

			if( polygonalAreaLight.Specular )
			{
				int cellX = specularIndex % specularBufferRowCapacity;
				int cellY = specularIndex / specularBufferRowCapacity;
				float cellSize = 1.0f / specularBufferRowCapacity;
				polygonalAreaLight.SpecularBufferUVData.Set( cellX*cellSize, cellY*cellSize, cellSize, cellSize );
				_dxPolygonBuffer[batchIndex*8+7] = polygonalAreaLight.SpecularBufferUVData;
				specularIndex++;
			}
			else
			{
				_dxPolygonBuffer[batchIndex*8+7].Set( 0,0,0,0 );
			}

			// gl-specific data
			_glPolygonDesc[batchIndex] = _dxPolygonBuffer[batchIndex*8];
			_glPolygonColor[batchIndex] = _dxPolygonBuffer[batchIndex*8+1];
			_glPolygonNormal[batchIndex] = _dxPolygonBuffer[batchIndex*8+2];
			_glPolygonTangent[batchIndex] = _dxPolygonBuffer[batchIndex*8+3];
			_glPolygonBitangent[batchIndex] = _dxPolygonBuffer[batchIndex*8+4];
			_glPolygonCentroid[batchIndex] = _dxPolygonBuffer[batchIndex*8+5];
			_glPolygonCircumcircle[batchIndex] = _dxPolygonBuffer[batchIndex*8+6];
			_glPolygonSpecularUV[batchIndex] = _dxPolygonBuffer[batchIndex*8+7];

			vertexBufferOffset += polygonalAreaLight.Vertices.Length;
			batchIndex++;
		}

		Shader.SetGlobalInt( "_PALNumPolygons", NumPolygons );
		Shader.SetGlobalInt( "_PALNumVertices", NumVertices );
		Shader.SetGlobalTexture( "_PALSpecularBuffer", _specularBuffer );

		#if UNITY_5_4_OR_NEWER
			switch( SystemInfo.graphicsDeviceType )
			{
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGL2:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3:
				Shader.SetGlobalVectorArray( "_PALPolygonDesc", _glPolygonDesc );
				Shader.SetGlobalVectorArray( "_PALPolygonColor", _glPolygonColor );
				Shader.SetGlobalVectorArray( "_PALPolygonNormal", _glPolygonNormal );
				Shader.SetGlobalVectorArray( "_PALPolygonTangent", _glPolygonTangent );
				Shader.SetGlobalVectorArray( "_PALPolygonBitangent", _glPolygonBitangent );
				Shader.SetGlobalVectorArray( "_PALPolygonCentroid", _glPolygonCentroid );
				Shader.SetGlobalVectorArray( "_PALPolygonCircumcircle", _glPolygonCircumcircle );
				Shader.SetGlobalVectorArray( "_PALPolygonSpecularUV", _glPolygonSpecularUV );
				Shader.SetGlobalVectorArray( "_PALVertexBuffer", _glVertexBuffer );
				break;
			default:
				Shader.SetGlobalVectorArray( "_PALPolygonBuffer", _dxPolygonBuffer );
				Shader.SetGlobalVectorArray( "_PALVertexBuffer", _dxVertexBuffer );
				break;
			}
		#else
			for( int i=0; i<NumPolygons; i++ )
			{
				Shader.SetGlobalVector( _polygonBufferPropertyId[i], _polygonBuffer[i] );				
			}
			for( int i=0; i<NumVertices; i++ )
			{
				Shader.SetGlobalVector( _vertexBufferPropertyId[i], _vertexBuffer[i] );
			}
		#endif

		if( _polygonalAreaLights.Count > 0 )
		{
			switch( _polygonalAreaLights[0].ProjectionMode )
			{
			case ProjectionMode.Centered:
				Shader.DisableKeyword( "_PAL_PROJECTION_WEIGHTED" );
				break;
			case ProjectionMode.Weighted:
				Shader.EnableKeyword( "_PAL_PROJECTION_WEIGHTED" );
				break;
			default:
				break;
			}
		}
	}

	public static void ExclusiveUpdate(PolygonalAreaLight polygonalAreaLight)
	{
		CheckPropertyIds();

		if( polygonalAreaLight.Vertices.Length >= 3 )
		{
			Vector3 circumcenter = polygonalAreaLight.Circumcircle;
			Vector3 tangent = new Vector3( polygonalAreaLight.Normal.y, polygonalAreaLight.Normal.z, -polygonalAreaLight.Normal.x );
			Vector3 bitangent = Vector3.Cross( tangent, polygonalAreaLight.Normal ).normalized;
			tangent = Vector3.Cross( polygonalAreaLight.Normal, bitangent ).normalized;

			for( int j=0; j<polygonalAreaLight.Vertices.Length; j++ )
			{
				_dxVertexBuffer[j] = polygonalAreaLight.Vertices[j];

				int compressedIndex = j/2;
				Vector3 vertexOriginOffset = polygonalAreaLight.Vertices[j] - circumcenter;
				Vector2 planarVertexCoords = new Vector2( Vector3.Dot( vertexOriginOffset, tangent ), Vector3.Dot( vertexOriginOffset, bitangent ) );

				switch( j % 2 )
				{
				case 0:
					_glVertexBuffer[compressedIndex].x = planarVertexCoords.x;
					_glVertexBuffer[compressedIndex].y = planarVertexCoords.y;
					break;
				default:
					_glVertexBuffer[compressedIndex].z = planarVertexCoords.x;
					_glVertexBuffer[compressedIndex].w = planarVertexCoords.y;
					break;
				}
			}

			// descriptor
			_dxPolygonBuffer[0].Set(
				0, 
				polygonalAreaLight.Vertices.Length,
				polygonalAreaLight.Intensity,
				polygonalAreaLight.Bias
			);

			// precalculated data
			Vector3 circumcenterOffset = polygonalAreaLight.Circumcircle;
			circumcenterOffset = circumcenterOffset - polygonalAreaLight.Vertices[0];
			_dxPolygonBuffer[1] = polygonalAreaLight.Color;
			_dxPolygonBuffer[2] = polygonalAreaLight.Normal;
			_dxPolygonBuffer[2].w = -Vector3.Dot( polygonalAreaLight.Normal, polygonalAreaLight.Vertices[0] );
			_dxPolygonBuffer[3] = tangent;
			_dxPolygonBuffer[4] = bitangent;
			_dxPolygonBuffer[5] = polygonalAreaLight.Centroid;
			_dxPolygonBuffer[6] = polygonalAreaLight.Circumcircle;
			_dxPolygonBuffer[7].Set( 0,0,1,1 );

			// gl-specific data
			_glPolygonDesc[0] = _dxPolygonBuffer[0];
			_glPolygonColor[0] = _dxPolygonBuffer[1];
			_glPolygonNormal[0] = _dxPolygonBuffer[2];
			_glPolygonTangent[0] = _dxPolygonBuffer[3];
			_glPolygonBitangent[0] = _dxPolygonBuffer[4];
			_glPolygonCentroid[0] = _dxPolygonBuffer[5];
			_glPolygonCircumcircle[0] = _dxPolygonBuffer[6];
			_glPolygonSpecularUV[0] = _dxPolygonBuffer[7];
		}

		Shader.SetGlobalInt( "_PALNumPolygons", 1 );
		Shader.SetGlobalInt( "_PALNumVertices", polygonalAreaLight.Vertices.Length );
		Shader.SetGlobalTexture( "_PALSpecularBuffer", _specularBuffer );

		#if UNITY_5_4_OR_NEWER
			switch( SystemInfo.graphicsDeviceType )
			{
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGL2:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2:
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3:
				Shader.SetGlobalVectorArray( "_PALPolygonDesc", _glPolygonDesc );
				Shader.SetGlobalVectorArray( "_PALPolygonColor", _glPolygonColor );
				Shader.SetGlobalVectorArray( "_PALPolygonNormal", _glPolygonNormal );
				Shader.SetGlobalVectorArray( "_PALPolygonTangent", _glPolygonTangent );
				Shader.SetGlobalVectorArray( "_PALPolygonBitangent", _glPolygonBitangent );
				Shader.SetGlobalVectorArray( "_PALPolygonCentroid", _glPolygonCentroid );
				Shader.SetGlobalVectorArray( "_PALPolygonCircumcircle", _glPolygonCircumcircle );
				Shader.SetGlobalVectorArray( "_PALPolygonSpecularUV", _glPolygonSpecularUV );
				Shader.SetGlobalVectorArray( "_PALVertexBuffer", _glVertexBuffer );
				break;
			default:
				Shader.SetGlobalVectorArray( "_PALPolygonBuffer", _dxPolygonBuffer );
				Shader.SetGlobalVectorArray( "_PALVertexBuffer", _dxVertexBuffer );
				break;
			}
		#else
			for( int i=0; i<NumPolygons; i++ )
			{
				Shader.SetGlobalVector( _polygonBufferPropertyId[i], _polygonBuffer[i] );				
			}
			for( int i=0; i<NumVertices; i++ )
			{
				Shader.SetGlobalVector( _vertexBufferPropertyId[i], _vertexBuffer[i] );
				Shader.SetGlobalVector( _pointBufferPropertyId[i], _pointBuffer[i] );
			}
		#endif

		switch( polygonalAreaLight.ProjectionMode )
		{
			case ProjectionMode.Centered:
				Shader.DisableKeyword( "_PAL_PROJECTION_WEIGHTED" );
				break;
			case ProjectionMode.Weighted:
				Shader.EnableKeyword( "_PAL_PROJECTION_WEIGHTED" );
				break;
			default:
				break;
		}
	}
	#endregion
};