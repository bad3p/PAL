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

public enum ProjectionMode
{
	Centered,
	Weighted,
	Pyramidal
};

public enum VertexOrder
{
	CW,
	CCW
};

[ExecuteInEditMode]
public class MeshAreaLight : MonoBehaviour
{
	#region PublicFields
	public VertexOrder VertexOrder = VertexOrder.CW;
	public int[] Vertices = new int[0];

	public Color Color = Color.white;

	[Range(0.0f, 10.0f)]
	public float Intensity = 0.5f;

	[Range(0.0f, 2.0f)]
	public float Bias = 0.0f;

	public ProjectionMode ProjectionMode = ProjectionMode.Centered;
	#endregion

	#region PrivateFields
	Transform    _thisTransform;
	MeshFilter   _thisMeshFilter;
	MeshRenderer _thisMeshRenderer;
	Mesh         _sharedMesh;
	Vector3[]    _sharedMeshVertices;
	int[]        _sharedMeshTriangles;
	Bounds       _sharedMeshBounds;
	int          _batchIndex = -1;
	#endregion

	#region MonoBehaviour
	void Awake() 
	{
		_thisTransform = GetComponent<Transform>();
		_thisMeshFilter = GetComponent<MeshFilter>();
		_thisMeshRenderer = GetComponent<MeshRenderer>();
		_sharedMesh = _thisMeshFilter.sharedMesh;
		_sharedMeshVertices = _sharedMesh.vertices;
		_sharedMeshTriangles = _sharedMesh.triangles;
		_sharedMeshBounds = _sharedMesh.bounds;

		_meshAreaLights.Add( this );

		if( _propertyId.Length != ShaderConstantBufferSize )
		{
			_propertyId = new string[ShaderConstantBufferSize];
			for( int i=0; i<ShaderConstantBufferSize; i++ )
			{
				_propertyId[i] = "_PALBuffer" + i.ToString();
			}
		}

		if( _propertyValue.Length != ShaderConstantBufferSize )
		{
			_propertyValue = new Vector4[ShaderConstantBufferSize];
		}

		_lastFrameCount = -1;
		_numAreaLightsUpdated = 0;
	}

	void OnDestroy() 
	{
		if( _meshAreaLights.Contains( this ) )
		{
			_meshAreaLights.Remove( this );
		}
	}

	void OnEnable() 
	{
		if( !_meshAreaLights.Contains( this ) )
		{
			_meshAreaLights.Add( this );
		}
	}

	void OnDisable() 
	{
		if( _meshAreaLights.Contains( this ) )
		{
			_meshAreaLights.Remove( this );
		}
	}

	void Update() 
	{
		if( _lastFrameCount != Time.frameCount )
		{
			_lastFrameCount = Time.frameCount;
			_numAreaLightsUpdated = 1;
		}
		else
		{
			_numAreaLightsUpdated++;
		}

		if( _numAreaLightsUpdated == _meshAreaLights.Count )
		{
			UpdateBatch();
		}
	}

	void OnDrawGizmos() 
	{
		/*
		if( _batchIndex >= 0 && _batchIndex < NumPolygons )
		{
			Gizmos.DrawWireSphere( _propertyValue[_batchIndex*5+4], _propertyValue[_batchIndex*5+4].w );
		}
		*/
	}
	#endregion	

	#region ShaderBridge
	const int ShaderConstantBufferSize = 2048;

	static public int NumPolygons { get; private set; }
	static public int NumVertices { get; private set; }
	static public int BufferSize { get; private set; }

	static List<MeshAreaLight> _meshAreaLights = new List<MeshAreaLight>();
	static int                 _lastFrameCount = -1;
	static int                 _numAreaLightsUpdated = 0;
	static string[]            _propertyId = new string[0];
	static Vector4[]           _propertyValue = new Vector4[0];
	static Vector4             _bufferSizes = Vector4.zero;

	static void UpdateBatch()
	{
		NumPolygons = 0;
		NumVertices = 0;
		BufferSize = 0;

		for( int i=0; i<_meshAreaLights.Count; i++ )
		{
			MeshAreaLight meshAreaLight = _meshAreaLights[i];
			if( meshAreaLight.Vertices.Length < 3 ) continue;

			int requiredBufferSize = 5 + meshAreaLight.Vertices.Length;
			if( BufferSize + requiredBufferSize < ShaderConstantBufferSize )
			{
				NumPolygons++;
				NumVertices += meshAreaLight.Vertices.Length;
				BufferSize += requiredBufferSize;
			}
		}

		int vertexBufferOffset = NumPolygons * 5;

		int batchIndex = 0;
		for( int k=0; k<_meshAreaLights.Count; k++ )
		{
			MeshAreaLight meshAreaLight = _meshAreaLights[k];
			if( meshAreaLight.Vertices.Length < 3 ) continue;
			meshAreaLight._batchIndex = batchIndex;

			if( batchIndex == NumPolygons-1 )
			{
				_propertyValue[_propertyValue.Length-1] = _propertyValue[0];
			}
				
			Matrix4x4 meshAreaLightTransform = meshAreaLight._thisTransform.localToWorldMatrix;
			for( int j=0; j<meshAreaLight.Vertices.Length; j++ )
			{
				int vertexIndex = ( meshAreaLight.VertexOrder == VertexOrder.CW ) ? ( j ) : ( meshAreaLight.Vertices.Length - j - 1 );
				_propertyValue[vertexBufferOffset+j] = meshAreaLightTransform.MultiplyPoint( meshAreaLight._sharedMeshVertices[meshAreaLight.Vertices[vertexIndex]] );
			}

			// descriptor
			_propertyValue[batchIndex*5].Set(
				vertexBufferOffset, 
				vertexBufferOffset + meshAreaLight.Vertices.Length,
				meshAreaLight.Intensity,
				meshAreaLight.Bias
			);

			// color
			_propertyValue[batchIndex*5+1] = meshAreaLight.Color;

			// normal
			Vector3 v0 = meshAreaLight._sharedMeshVertices[meshAreaLight._sharedMeshTriangles[0]];
			Vector3 v1 = meshAreaLight._sharedMeshVertices[meshAreaLight._sharedMeshTriangles[1]];
			Vector3 v2 = meshAreaLight._sharedMeshVertices[meshAreaLight._sharedMeshTriangles[2]];
			Vector3 meshNormal = Vector3.Cross( v1-v0, v2-v0 );
			meshNormal = meshAreaLightTransform.MultiplyVector( meshNormal ).normalized;
			_propertyValue[batchIndex*5+2] = meshNormal;

			// centroid
			Vector4 centroid = Vector4.zero;
			for( int j=0; j<meshAreaLight.Vertices.Length; j++ )
			{
				centroid += _propertyValue[vertexBufferOffset+j];
			}
			centroid *= 1.0f / meshAreaLight.Vertices.Length;
			_propertyValue[batchIndex*5+3] = centroid;

			// circumcircle
			Vector3 meshBoundsExtents = meshAreaLight._sharedMeshBounds.extents - meshNormal * Vector3.Dot( meshAreaLight._sharedMeshBounds.extents, meshNormal );
			Vector3 meshCircumcenter = meshAreaLightTransform.MultiplyPoint( meshAreaLight._sharedMeshBounds.center );
			float meshCircumradius = 0;
			for( int j=0; j<meshAreaLight.Vertices.Length; j++ )
			{
				float distance = Vector3.Distance( meshCircumcenter, _propertyValue[vertexBufferOffset+j] );
				meshCircumradius = Mathf.Max( meshCircumradius, distance );
			}
			_propertyValue[batchIndex*5+4].Set( meshCircumcenter.x, meshCircumcenter.y, meshCircumcenter.z, meshCircumradius );

			vertexBufferOffset += ( meshAreaLight.Vertices.Length );
			batchIndex++;
		}

		_bufferSizes.Set( NumPolygons, 0, 0, 0 );
		Shader.SetGlobalVector( "_PALBufferSizes", _bufferSizes );

		#if UNITY_5_4_OR_NEWER
			Shader.SetGlobalVectorArray( "_PALBuffer", _propertyValue );
		#else
			for( int i=0; i<BufferSize; i++ )
			{
				Shader.SetGlobalVector( _propertyId[i], _propertyValue[i] );
			}
		#endif

		switch( _meshAreaLights[0].ProjectionMode )
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
}
