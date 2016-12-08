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

public class PALBatchBuilder
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
	const int ShaderConstantBufferSize = 1023;

	static public int NumPolygons { get; private set; }
	static public int NumVertices { get; private set; }
	static public int BufferSize { get; private set; }

	static int                 _lastFrameCount = -1;
	static int                 _numAreaLightsUpdated = 0;
	static string[]            _propertyId = new string[0];
	static Vector4[]           _propertyValue = new Vector4[0];
	static Vector4             _bufferSizes = Vector4.zero;	

	public static void Update(PolygonalAreaLight updatedPolygonalAreaLight)
	{
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

		if( _lastFrameCount != Time.frameCount )
		{
			_lastFrameCount = Time.frameCount;
			_numAreaLightsUpdated = 1;
		}
		else
		{
			_numAreaLightsUpdated++;
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
			}
		}

		int vertexBufferOffset = NumPolygons * 5;

		int batchIndex = 0;
		for( int k=0; k<_polygonalAreaLights.Count; k++ )
		{
			PolygonalAreaLight polygonalAreaLight = _polygonalAreaLights[k];
			if( polygonalAreaLight.Vertices.Length < 3 ) continue;
			polygonalAreaLight.BatchIndex = batchIndex;

			for( int j=0; j<polygonalAreaLight.Vertices.Length; j++ )
			{
				_propertyValue[vertexBufferOffset+j] = polygonalAreaLight.Vertices[j];
			}

			// descriptor
			_propertyValue[batchIndex*5].Set(
				vertexBufferOffset, 
				vertexBufferOffset + polygonalAreaLight.Vertices.Length,
				polygonalAreaLight.Intensity,
				polygonalAreaLight.Bias
			);

			// precalculated data
			_propertyValue[batchIndex*5+1] = polygonalAreaLight.Color;
			_propertyValue[batchIndex*5+2] = polygonalAreaLight.Normal;
			_propertyValue[batchIndex*5+3] = polygonalAreaLight.Centroid;
			_propertyValue[batchIndex*5+4] = polygonalAreaLight.Circumcircle;

			vertexBufferOffset += polygonalAreaLight.Vertices.Length;
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
		if( _propertyId.Length != ShaderConstantBufferSize )
		{
			_propertyId = new string[ShaderConstantBufferSize];
			for( int i=0; i<ShaderConstantBufferSize; i++ )
			{
				_propertyId[i] = "_PALBuffer" + i.ToString();
			}
		}

		int numPolygonsEx = 0;
		int numVerticesEx = 0;
		int bufferSizeEx = 0;

		if( polygonalAreaLight.Vertices.Length >= 3 )
		{
			int requiredBufferSize = 5 + polygonalAreaLight.Vertices.Length;
			if( bufferSizeEx + requiredBufferSize < ShaderConstantBufferSize )
			{
				numPolygonsEx++;
				numVerticesEx += polygonalAreaLight.Vertices.Length;
				bufferSizeEx += requiredBufferSize;
			}
		}

		int vertexBufferOffset = numPolygonsEx * 5;

		if( polygonalAreaLight.Vertices.Length >= 3 )
		{
			for( int j=0; j<polygonalAreaLight.Vertices.Length; j++ )
			{
				_propertyValue[vertexBufferOffset+j] = polygonalAreaLight.Vertices[j];
			}

			// descriptor
			_propertyValue[0].Set(
				vertexBufferOffset, 
				vertexBufferOffset + polygonalAreaLight.Vertices.Length,
				polygonalAreaLight.Intensity,
				polygonalAreaLight.Bias
			);

			// precalculated data
			_propertyValue[1] = polygonalAreaLight.Color;
			_propertyValue[2] = polygonalAreaLight.Normal;
			_propertyValue[3] = polygonalAreaLight.Centroid;
			_propertyValue[4] = polygonalAreaLight.Circumcircle;

			vertexBufferOffset += polygonalAreaLight.Vertices.Length;
		}

		_bufferSizes.Set( numPolygonsEx, 0, 0, 0 );
		Shader.SetGlobalVector( "_PALBufferSizes", _bufferSizes );

		#if UNITY_5_4_OR_NEWER
			Shader.SetGlobalVectorArray( "_PALBuffer", _propertyValue );
		#else
			for( int i=0; i<BufferSize; i++ )
			{
				Shader.SetGlobalVector( _propertyId[i], _propertyValue[i] );
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