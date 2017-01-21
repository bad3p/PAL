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
	public Vector3 Normal = Vector3.zero;
	public Vector3 Centroid = Vector3.zero;
	public Vector4 Circumcircle = Vector4.zero;
	public ProjectionMode ProjectionMode = ProjectionMode.Centered;
	public Vector3[] Vertices = new Vector3[0];
	public int BatchIndex = 0;
};

static public class PALBatchBuilder
{
	#region Interface
	private static List<PolygonalAreaLight> _polygonalAreaLights = null;

	public static void RegisterPolygonalAreaLight(PolygonalAreaLight polygonalAreaLight)
	{
		if( _polygonalAreaLights == null )
		{
			_polygonalAreaLights = new List<PolygonalAreaLight>();
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
		}
	}
	#endregion

	#region BatchBuilder
	public const int ShaderConstantBufferSize = 1023;

	static public int NumPolygons { get; private set; }
	static public int NumVertices { get; private set; }
	static public int BufferSize { get; private set; }

	static int                 _lastFrameCount = -1;
	static int                 _numAreaLightsUpdated = 0;
	static string[]            _polygonBufferPropertyId = new string[0];
	static string[]            _vertexBufferPropertyId = new string[0];
	static string[]            _pointBufferPropertyId = new string[0];
	static Vector4[]           _polygonBuffer = new Vector4[0];
	static Vector4[]           _vertexBuffer = new Vector4[0];
	static Vector4[]           _pointBuffer = new Vector4[0];
	static Vector4             _bufferSizes = Vector4.zero;	

	public static void Update(PolygonalAreaLight updatedPolygonalAreaLight)
	{
		if( _polygonBufferPropertyId.Length != ShaderConstantBufferSize )
		{
			_polygonBufferPropertyId = new string[ShaderConstantBufferSize];
			for( int i=0; i<ShaderConstantBufferSize; i++ )
			{
				_polygonBufferPropertyId[i] = "_PALPolygonBuffer" + i.ToString();
			}
		}

		if( _vertexBufferPropertyId.Length != ShaderConstantBufferSize )
		{
			_vertexBufferPropertyId = new string[ShaderConstantBufferSize];
			for( int i=0; i<ShaderConstantBufferSize; i++ )
			{
				_vertexBufferPropertyId[i] = "_PALVertexBuffer" + i.ToString();
			}
		}

		if( _pointBufferPropertyId.Length != ShaderConstantBufferSize )
		{
			_pointBufferPropertyId = new string[ShaderConstantBufferSize];
			for( int i=0; i<ShaderConstantBufferSize; i++ )
			{
				_pointBufferPropertyId[i] = "_PALPointBuffer" + i.ToString();
			}
		}

		if( _polygonBuffer.Length != ShaderConstantBufferSize )
		{
			_polygonBuffer = new Vector4[ShaderConstantBufferSize];
		}

		if( _vertexBuffer.Length != ShaderConstantBufferSize )
		{
			_vertexBuffer = new Vector4[ShaderConstantBufferSize];
		}

		if( _pointBuffer.Length != ShaderConstantBufferSize )
		{
			_pointBuffer = new Vector4[ShaderConstantBufferSize];
		}

		if( _lastFrameCount != Time.frameCount )
		{
			_lastFrameCount = Time.frameCount;
			_numAreaLightsUpdated = 1;
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

		NumPolygons = 0;
		NumVertices = 0;
		BufferSize = 0;

		for( int i=0; i<_polygonalAreaLights.Count; i++ )
		{
			PolygonalAreaLight polygonalAreaLight = _polygonalAreaLights[i];
			if( polygonalAreaLight.Vertices.Length < 3 ) continue;

			int requiredBufferSize = 5 + polygonalAreaLight.Vertices.Length;
			if( BufferSize + requiredBufferSize < ShaderConstantBufferSize )
			{
				NumPolygons++;
				NumVertices += polygonalAreaLight.Vertices.Length;
				BufferSize += requiredBufferSize;
				polygonalAreaLight.BatchIndex = 1;
			}
			else
			{
				polygonalAreaLight.BatchIndex = 0;
			}
		}

		int vertexBufferOffset = 0;

		int batchIndex = 0;
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

			Vector3 tangent = new Vector3( polygonalAreaLight.Normal.y, polygonalAreaLight.Normal.z, -polygonalAreaLight.Normal.x );
			Vector3 bitangent = Vector3.Cross( tangent, polygonalAreaLight.Normal ).normalized;
			tangent = Vector3.Cross( polygonalAreaLight.Normal, bitangent ).normalized;

			for( int j=0; j<polygonalAreaLight.Vertices.Length; j++ )
			{
				_vertexBuffer[vertexBufferOffset+j] = polygonalAreaLight.Vertices[j];

				Vector3 point = polygonalAreaLight.Vertices[j] - polygonalAreaLight.Vertices[0];
				_pointBuffer[vertexBufferOffset+j].Set( Vector3.Dot( point, tangent ), Vector3.Dot( point, bitangent ), 0, 0 );

				if( j > 0 )
				{
					_pointBuffer[vertexBufferOffset+j-1].z = _pointBuffer[vertexBufferOffset+j].x - _pointBuffer[vertexBufferOffset+j-1].x;
					_pointBuffer[vertexBufferOffset+j-1].w = _pointBuffer[vertexBufferOffset+j].y - _pointBuffer[vertexBufferOffset+j-1].y;
					if( j == polygonalAreaLight.Vertices.Length-1 )
					{
						_pointBuffer[vertexBufferOffset+j].z = _pointBuffer[vertexBufferOffset].x - _pointBuffer[vertexBufferOffset+j].x;
						_pointBuffer[vertexBufferOffset+j].w = _pointBuffer[vertexBufferOffset].y - _pointBuffer[vertexBufferOffset+j].y;
					}
				}
			}

			// descriptor
			_polygonBuffer[batchIndex*7].Set(
				vertexBufferOffset, 
				vertexBufferOffset + polygonalAreaLight.Vertices.Length,
				polygonalAreaLight.Intensity,
				polygonalAreaLight.Bias
			);

			// precalculated data
			_polygonBuffer[batchIndex*7+1] = polygonalAreaLight.Color;
			_polygonBuffer[batchIndex*7+2] = polygonalAreaLight.Normal;
			_polygonBuffer[batchIndex*7+2].w = -Vector3.Dot( polygonalAreaLight.Normal, polygonalAreaLight.Vertices[0] );
			_polygonBuffer[batchIndex*7+3] = tangent;
			_polygonBuffer[batchIndex*7+4] = bitangent;
			_polygonBuffer[batchIndex*7+5] = polygonalAreaLight.Centroid;
			_polygonBuffer[batchIndex*7+6] = polygonalAreaLight.Circumcircle;

			vertexBufferOffset += polygonalAreaLight.Vertices.Length;
			batchIndex++;
		}

		_bufferSizes.Set( NumPolygons, NumVertices, 0, 0 );
		Shader.SetGlobalVector( "_PALBufferSizes", _bufferSizes );

		#if UNITY_5_4_OR_NEWER
			Shader.SetGlobalVectorArray( "_PALPolygonBuffer", _polygonBuffer );
			Shader.SetGlobalVectorArray( "_PALVertexBuffer", _vertexBuffer );
			Shader.SetGlobalVectorArray( "_PALPointBuffer", _pointBuffer );
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
		if( _polygonBufferPropertyId.Length != ShaderConstantBufferSize )
		{
			_polygonBufferPropertyId = new string[ShaderConstantBufferSize];
			for( int i=0; i<ShaderConstantBufferSize; i++ )
			{
				_polygonBufferPropertyId[i] = "_PALPolygonBuffer" + i.ToString();
			}
		}

		if( _vertexBufferPropertyId.Length != ShaderConstantBufferSize )
		{
			_vertexBufferPropertyId = new string[ShaderConstantBufferSize];
			for( int i=0; i<ShaderConstantBufferSize; i++ )
			{
				_vertexBufferPropertyId[i] = "_PALVertexBuffer" + i.ToString();
			}
		}

		if( _pointBufferPropertyId.Length != ShaderConstantBufferSize )
		{
			_pointBufferPropertyId = new string[ShaderConstantBufferSize];
			for( int i=0; i<ShaderConstantBufferSize; i++ )
			{
				_pointBufferPropertyId[i] = "_PALPointBuffer" + i.ToString();
			}
		}

		if( _polygonBuffer.Length != ShaderConstantBufferSize )
		{
			_polygonBuffer = new Vector4[ShaderConstantBufferSize];
		}

		if( _vertexBuffer.Length != ShaderConstantBufferSize )
		{
			_vertexBuffer = new Vector4[ShaderConstantBufferSize];
		}

		if( _pointBuffer.Length != ShaderConstantBufferSize )
		{
			_pointBuffer = new Vector4[ShaderConstantBufferSize];
		}

		if( polygonalAreaLight.Vertices.Length >= 3 )
		{
			Vector3 tangent = new Vector3( polygonalAreaLight.Normal.y, polygonalAreaLight.Normal.z, -polygonalAreaLight.Normal.x );
			Vector3 bitangent = Vector3.Cross( tangent, polygonalAreaLight.Normal ).normalized;
			tangent = Vector3.Cross( polygonalAreaLight.Normal, bitangent ).normalized;

			for( int j=0; j<polygonalAreaLight.Vertices.Length; j++ )
			{
				_vertexBuffer[j] = polygonalAreaLight.Vertices[j];

				Vector3 point = polygonalAreaLight.Vertices[j] - polygonalAreaLight.Vertices[0];
				_pointBuffer[j].Set( Vector3.Dot( point, tangent ), Vector3.Dot( point, bitangent ), 0, 0 );

				if( j > 0 )
				{
					_pointBuffer[j-1].z = _pointBuffer[j].x - _pointBuffer[j-1].x;
					_pointBuffer[j-1].w = _pointBuffer[j].y - _pointBuffer[j-1].y;
					if( j == polygonalAreaLight.Vertices.Length-1 )
					{
						_pointBuffer[j].z = _pointBuffer[0].x - _pointBuffer[j].x;
						_pointBuffer[j].w = _pointBuffer[0].y - _pointBuffer[j].y;
					}
				}
			}

			// descriptor
			_polygonBuffer[0].Set(
				0, 
				polygonalAreaLight.Vertices.Length,
				polygonalAreaLight.Intensity,
				polygonalAreaLight.Bias
			);

			// precalculated data
			_polygonBuffer[1] = polygonalAreaLight.Color;
			_polygonBuffer[2] = polygonalAreaLight.Normal;
			_polygonBuffer[2].w = -Vector3.Dot( polygonalAreaLight.Normal, polygonalAreaLight.Vertices[0] );
			_polygonBuffer[3] = tangent;
			_polygonBuffer[4] = bitangent;
			_polygonBuffer[5] = polygonalAreaLight.Centroid;
			_polygonBuffer[6] = polygonalAreaLight.Circumcircle;
		}

		_bufferSizes.Set( 1, polygonalAreaLight.Vertices.Length, 0, 0 );
		Shader.SetGlobalVector( "_PALBufferSizes", _bufferSizes );

		#if UNITY_5_4_OR_NEWER
			Shader.SetGlobalVectorArray( "_PALPolygonBuffer", _polygonBuffer );
			Shader.SetGlobalVectorArray( "_PALVertexBuffer", _vertexBuffer );
			Shader.SetGlobalVectorArray( "_PALPointBuffer", _pointBuffer );
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