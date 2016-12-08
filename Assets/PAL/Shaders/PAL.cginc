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

#ifndef __PAL_CGINC_INCLUDED__
#define __PAL_CGINC_INCLUDED__

#include "UnityCG.cginc"

uniform float4 _PALBufferSizes;
uniform float4 _PALBuffer[1023];

#define PAL_NUM_POLYGONS (int)_PALBufferSizes.x

#define PAL_POLYGON_DESC(polygonIndex) _PALBuffer[polygonIndex*5] 
#define PAL_POLYGON_COLOR(polygonIndex) _PALBuffer[polygonIndex*5+1]
#define PAL_POLYGON_NORMAL(polygonIndex) _PALBuffer[polygonIndex*5+2]
#define PAL_POLYGON_CENTROID(polygonIndex) _PALBuffer[polygonIndex*5+3]
#define PAL_POLYGON_CIRCUMCIRCLE(polygonIndex) _PALBuffer[polygonIndex*5+4]

float PALLocalOcclusion(float4 polygonCircumcircle, float4 fragmentPlane)
{
	float distanceToFragmentPlane = dot( polygonCircumcircle.xyz, fragmentPlane.xyz ) + fragmentPlane.w;
	return clamp( (distanceToFragmentPlane+polygonCircumcircle.w)/(2*polygonCircumcircle.w), 0, 1 );
}

float4 PALDiffuseContribution(float3 worldPos, float3 worldNormal)
{
	float4 fragmentPlane = float4( worldNormal, -dot( worldNormal, worldPos ) );

	int numPolygons = PAL_NUM_POLYGONS;

	float4 diffuseColor = 0; 

	for( int i=0; i<numPolygons; i++ )
	{
		float4 polygonDesc = PAL_POLYGON_DESC(i);
		float4 polygonColor = PAL_POLYGON_COLOR(i);
		float4 polygonNormal = PAL_POLYGON_NORMAL(i);
		float4 polygonCentroid = PAL_POLYGON_CENTROID(i);
		float4 polygonCircumcircle = PAL_POLYGON_CIRCUMCIRCLE(i);
		float polygonCircumradius = polygonCircumcircle.w;

		int firstVertexIndex = (int)polygonDesc.x;
		int lastVertexIndex = (int)polygonDesc.y;
		float intensity = polygonDesc.z;
		float bias = polygonDesc.w;

		float3 pointOnPolygon = polygonCentroid.xyz;

		#if defined(_PAL_PROJECTION_WEIGHTED)
			float3 weightedPointOnPolygon = 0;
			float weightSum = 0;
			for( int j=firstVertexIndex; j<lastVertexIndex; j++ )
			{
				float3 polygonVertex = _PALBuffer[j].xyz;
				float weight = 1.0 / distance( polygonVertex, worldPos );
				weightSum += weight;
				weightedPointOnPolygon += polygonVertex * weight;
			}
			weightedPointOnPolygon *= 1.0 / weightSum;
			pointOnPolygon = weightedPointOnPolygon;
		#endif

		float3 projectionBasisZ = normalize( pointOnPolygon - worldPos );
		float sideCondition = dot( projectionBasisZ, polygonNormal );
		if( sideCondition < 0 )
		{
			float3 projectionBasisY = float3( projectionBasisZ.y, projectionBasisZ.z, -projectionBasisZ.x );
			float3 projectionBasisX = normalize( cross( projectionBasisY, projectionBasisZ ) );
			projectionBasisY = normalize( cross( projectionBasisZ, projectionBasisX ) );

			float3 biasOffset = projectionBasisZ * polygonCircumradius * bias;
			float3 biasedWorldPos = worldPos - biasOffset;

			float polygonArea = 0;

			float3 v0 = _PALBuffer[firstVertexIndex].xyz - biasedWorldPos;
			v0 = float3( dot( v0, projectionBasisX ), dot( v0, projectionBasisY ), dot( v0, projectionBasisZ ) );
			v0.xy /= v0.z;

			for( int j=firstVertexIndex+1; j<lastVertexIndex; j++ )
			{
				float3 v1 = _PALBuffer[j].xyz - biasedWorldPos; 
				v1 = float3( dot( v1, projectionBasisX ), dot( v1, projectionBasisY ), dot( v1, projectionBasisZ ) );
				v1.xy /= v1.z;

				polygonArea += v0.x*v1.y - v1.x*v0.y; 

				v0 = v1; 
			}

			diffuseColor += -0.5 * polygonArea * intensity * PALLocalOcclusion( polygonCircumcircle, fragmentPlane ) * polygonColor;
		}
	}

	return diffuseColor;
}

float PALIntensity(float3 worldPos, float3 worldNormal)
{
	float4 fragmentPlane = float4( worldNormal, -dot( worldNormal, worldPos ) );

	int numPolygons = PAL_NUM_POLYGONS;

	float result = 0; 

	for( int i=0; i<numPolygons; i++ )
	{
		float4 polygonDesc = PAL_POLYGON_DESC(i);
		float4 polygonColor = PAL_POLYGON_COLOR(i);
		float4 polygonNormal = PAL_POLYGON_NORMAL(i);
		float4 polygonCentroid = PAL_POLYGON_CENTROID(i);
		float4 polygonCircumcircle = PAL_POLYGON_CIRCUMCIRCLE(i);
		float polygonCircumradius = polygonCircumcircle.w;

		int firstVertexIndex = (int)polygonDesc.x;
		int lastVertexIndex = (int)polygonDesc.y;
		float intensity = polygonDesc.z;
		float bias = polygonDesc.w;
		float coherencyTreshold = polygonNormal.w;

		float3 pointOnPolygon = polygonCentroid.xyz;

		#if defined(_PAL_PROJECTION_WEIGHTED)
			float3 weightedPointOnPolygon = 0;
			float weightSum = 0;
			for( int j=firstVertexIndex; j<lastVertexIndex; j++ )
			{
				float3 polygonVertex = _PALBuffer[j].xyz;
				float weight = 1.0 / distance( polygonVertex, worldPos );
				weightSum += weight;
				weightedPointOnPolygon += polygonVertex * weight;
			}
			weightedPointOnPolygon *= 1.0 / weightSum;
			pointOnPolygon = weightedPointOnPolygon;
		#endif

		float3 projectionBasisZ = normalize( pointOnPolygon - worldPos );
		float sideCondition = dot( projectionBasisZ, polygonNormal );
		if( sideCondition < 0 )
		{
			float3 projectionBasisY = float3( projectionBasisZ.y, projectionBasisZ.z, -projectionBasisZ.x );
			float3 projectionBasisX = normalize( cross( projectionBasisY, projectionBasisZ ) );
			projectionBasisY = normalize( cross( projectionBasisZ, projectionBasisX ) );

			float3 biasOffset = projectionBasisZ * polygonCircumradius * bias;
			float3 biasedWorldPos = worldPos - biasOffset;

			float polygonArea = 0;

			float3 v0 = _PALBuffer[firstVertexIndex].xyz - biasedWorldPos;
			v0 = float3( dot( v0, projectionBasisX ), dot( v0, projectionBasisY ), dot( v0, projectionBasisZ ) );
			v0.xy /= v0.z;

			for( int j=firstVertexIndex+1; j<lastVertexIndex; j++ )
			{
				float3 v1 = _PALBuffer[j].xyz - biasedWorldPos; 
				v1 = float3( dot( v1, projectionBasisX ), dot( v1, projectionBasisY ), dot( v1, projectionBasisZ ) );
				v1.xy /= v1.z;

				polygonArea += v0.x*v1.y - v1.x*v0.y; 

				v0 = v1; 
			}

			float coherencyFactor = abs( dot( -projectionBasisZ, polygonNormal ) );
			coherencyFactor = saturate( ( coherencyFactor - coherencyTreshold ) / coherencyTreshold );

			result += -0.5 * polygonArea * intensity * coherencyFactor * PALLocalOcclusion( polygonCircumcircle, fragmentPlane );
		}
	}

	return result;
}

#endif