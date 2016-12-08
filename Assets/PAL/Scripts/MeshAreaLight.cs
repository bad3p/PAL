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
	PolygonalAreaLight _polygonalAreaLight = new PolygonalAreaLight();
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
		PALBatchBuilder.RegisterPolygonalAreaLight( _polygonalAreaLight );
	}

	void OnDestroy() 
	{		
		PALBatchBuilder.UnregisterPolygonalAreaLight( _polygonalAreaLight );
	}

	void OnEnable() 
	{
	}

	void OnDisable() 
	{
	}

	void Update() 
	{
		/*
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
		*/

		UpdatePolygonalAreaLight();
		PALBatchBuilder.Update( _polygonalAreaLight );
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

	void UpdatePolygonalAreaLight()
	{
		_polygonalAreaLight.Color = this.Color;
		_polygonalAreaLight.Intensity = this.Intensity;
		_polygonalAreaLight.Bias = this.Bias;
		_polygonalAreaLight.ProjectionMode = this.ProjectionMode;

		if( _polygonalAreaLight.Vertices.Length < this.Vertices.Length )
		{
			_polygonalAreaLight.Vertices = new Vector3[this.Vertices.Length];
		}

		Matrix4x4 meshAreaLightTransform = _thisTransform.localToWorldMatrix;
		for( int j=0; j<this.Vertices.Length; j++ )
		{
			int vertexIndex = ( VertexOrder == VertexOrder.CW ) ? ( j ) : ( Vertices.Length - j - 1 );
			_polygonalAreaLight.Vertices[j] = meshAreaLightTransform.MultiplyPoint( _sharedMeshVertices[Vertices[vertexIndex]] );
		}

		Vector3 e0 = ( _polygonalAreaLight.Vertices[1] - _polygonalAreaLight.Vertices[0] ).normalized;
		Vector3 e1 = Vector3.zero;
		float bestDotProduct = 1.0f;
		for( int j=2; j<_polygonalAreaLight.Vertices.Length; j++ )
		{
			Vector3 e = ( _polygonalAreaLight.Vertices[j] - _polygonalAreaLight.Vertices[0] ).normalized;
			float dotProduct = Mathf.Abs( Vector3.Dot( e0, e1 ) );
			if( dotProduct < bestDotProduct )
			{
				e1 = e;
				bestDotProduct = dotProduct;
			}
		}
		_polygonalAreaLight.Normal = Vector3.Cross( e0, e1 ).normalized;

		_polygonalAreaLight.Centroid = Vector4.zero;
		for( int j=0; j<_polygonalAreaLight.Vertices.Length; j++ )
		{
			_polygonalAreaLight.Centroid += _polygonalAreaLight.Vertices[j];
		}
		_polygonalAreaLight.Centroid *= 1.0f / _polygonalAreaLight.Vertices.Length;

		// circumcircle
		Vector3 meshBoundsExtents = _sharedMeshBounds.extents - _polygonalAreaLight.Normal * Vector3.Dot( _sharedMeshBounds.extents, _polygonalAreaLight.Normal );
		Vector3 meshCircumcenter = meshAreaLightTransform.MultiplyPoint( _sharedMeshBounds.center );
		float meshCircumradius = 0;
		for( int j=0; j<_polygonalAreaLight.Vertices.Length; j++ )
		{
			float distance = Vector3.Distance( meshCircumcenter, _polygonalAreaLight.Vertices[j] );
			meshCircumradius = Mathf.Max( meshCircumradius, distance );
		}
		_polygonalAreaLight.Circumcircle.Set( meshCircumcenter.x, meshCircumcenter.y, meshCircumcenter.z, meshCircumradius );
	}
	#endregion	

	#region MeshAreaLight
	public Bounds PolygonBounds
	{
		get
		{
			return _sharedMeshBounds;
		}
	}

	public Vector3 PolygonNormal
	{
		get
		{
			Vector3 v0 = _sharedMeshVertices[_sharedMeshTriangles[0]];
			Vector3 v1 = _sharedMeshVertices[_sharedMeshTriangles[1]];
			Vector3 v2 = _sharedMeshVertices[_sharedMeshTriangles[2]];
			Vector3 meshNormal = Vector3.Cross( v1-v0, v2-v0 );
			meshNormal = _thisTransform.localToWorldMatrix.MultiplyVector( meshNormal ).normalized;
			return meshNormal;
		}
	}

	public Vector3 PolygonCentroid
	{
		get
		{
			Matrix4x4 localToWorldMatrix = _thisTransform.localToWorldMatrix;
			Vector3 centroid = Vector3.zero;
			for( int j=0; j<Vertices.Length; j++ )
			{
				centroid += localToWorldMatrix.MultiplyPoint( _sharedMeshVertices[Vertices[j]] );
			}
			centroid *= 1.0f / Vertices.Length;
			return centroid;
		}
	}

	public Vector4 PolygonCircumcircle
	{
		get
		{
			Matrix4x4 localToWorldMatrix = _thisTransform.localToWorldMatrix;
			Vector3 meshNormal = PolygonNormal;
			Vector3 meshBoundsExtents = _sharedMeshBounds.extents - meshNormal * Vector3.Dot( _sharedMeshBounds.extents, meshNormal );
			Vector3 meshCircumcenter = localToWorldMatrix.MultiplyPoint( _sharedMeshBounds.center );
			float meshCircumradius = 0;
			for( int j=0; j<Vertices.Length; j++ )
			{
				Vector3 vertex = localToWorldMatrix.MultiplyPoint( _sharedMeshVertices[Vertices[j]] );
				float distance = Vector3.Distance( meshCircumcenter, vertex );
				meshCircumradius = Mathf.Max( meshCircumradius, distance );
			}
			return new Vector4( meshCircumcenter.x, meshCircumcenter.y, meshCircumcenter.z, meshCircumradius );
		}
	}

	public void PrepareIrradianceTransfer()
	{
		/*
		UpdateBatch( this );
		*/
		UpdatePolygonalAreaLight();
		PALBatchBuilder.ExclusiveUpdate( _polygonalAreaLight );
	}
	#endregion	
}
