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

#ifndef __PAL_CGINC_INCLUDED__
#define __PAL_CGINC_INCLUDED__

#include "UnityCG.cginc"

uniform float4 _PALBufferSizes;
uniform float4 _PALPolygonBuffer[1023];
uniform float4 _PALVertexBuffer[1023];
uniform float4 _PALPointBuffer[1023];

#define PAL_NUM_POLYGONS (int)_PALBufferSizes.x
#define PAL_NUM_VERTICES (int)_PALBufferSizes.y

#define PAL_POLYGON_DESC(polygonIndex) _PALPolygonBuffer[polygonIndex*7] 
#define PAL_POLYGON_COLOR(polygonIndex) _PALPolygonBuffer[polygonIndex*7+1]
#define PAL_POLYGON_NORMAL(polygonIndex) _PALPolygonBuffer[polygonIndex*7+2]
#define PAL_POLYGON_TANGENT(polygonIndex) _PALPolygonBuffer[polygonIndex*7+3]
#define PAL_POLYGON_BITANGENT(polygonIndex) _PALPolygonBuffer[polygonIndex*7+4]
#define PAL_POLYGON_CENTROID(polygonIndex) _PALPolygonBuffer[polygonIndex*7+5]
#define PAL_POLYGON_CIRCUMCIRCLE(polygonIndex) _PALPolygonBuffer[polygonIndex*7+6]

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
				float3 polygonVertex = _PALVertexBuffer[j].xyz;
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

			float3 v0 = _PALVertexBuffer[firstVertexIndex].xyz - biasedWorldPos;
			v0 = float3( dot( v0, projectionBasisX ), dot( v0, projectionBasisY ), dot( v0, projectionBasisZ ) );
			v0.xy /= v0.z;

			float3 vLoop = v0;

			for( int j=firstVertexIndex+1; j<lastVertexIndex; j++ )
			{
				float3 v1 = _PALVertexBuffer[j].xyz - biasedWorldPos; 
				v1 = float3( dot( v1, projectionBasisX ), dot( v1, projectionBasisY ), dot( v1, projectionBasisZ ) );
				v1.xy /= v1.z;

				polygonArea += v0.x*v1.y - v1.x*v0.y; 

				v0 = v1; 
			}

			float3 v1 = vLoop;
			polygonArea += v0.x*v1.y - v1.x*v0.y;

			diffuseColor += -0.5 * polygonArea * intensity * PALLocalOcclusion( polygonCircumcircle, fragmentPlane ) * polygonColor;
		}
	}

	return diffuseColor;
}

float PALDiffuseIntensity(float3 worldPos, float3 worldNormal)
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

		float3 pointOnPolygon = polygonCentroid.xyz;

		#if defined(_PAL_PROJECTION_WEIGHTED)
			float3 weightedPointOnPolygon = 0;
			float weightSum = 0;
			for( int j=firstVertexIndex; j<lastVertexIndex; j++ )
			{
				float3 polygonVertex = _PALVertexBuffer[j].xyz;
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

			float3 v0 = _PALVertexBuffer[firstVertexIndex].xyz - biasedWorldPos;
			v0 = float3( dot( v0, projectionBasisX ), dot( v0, projectionBasisY ), dot( v0, projectionBasisZ ) );
			v0.xy /= v0.z;

			for( int j=firstVertexIndex+1; j<lastVertexIndex; j++ )
			{
				float3 v1 = _PALVertexBuffer[j].xyz - biasedWorldPos; 
				v1 = float3( dot( v1, projectionBasisX ), dot( v1, projectionBasisY ), dot( v1, projectionBasisZ ) );
				v1.xy /= v1.z;

				polygonArea += v0.x*v1.y - v1.x*v0.y; 

				v0 = v1; 
			}

			result += -0.5 * polygonArea * intensity * PALLocalOcclusion( polygonCircumcircle, fragmentPlane );
		}
	}

	return result;
}


float3 RayPlaneIntersection(float4 plane, float3 pointOnPlane, float3 rayOrigin, float3 rayDirection)
{
	float3 w = rayOrigin - pointOnPlane;
	float s = -dot( plane.xyz, w ) / dot( plane.xyz, rayDirection );
	return rayOrigin + rayDirection * s;
}

float4 PALSpecularContribution(float3 worldPos, float3 worldRefl)
{
	int numPolygons = PAL_NUM_POLYGONS;

	float4 specularColor = 0; 

	for( int i=0; i<numPolygons; i++ )
	{
		float4 polygonDesc = PAL_POLYGON_DESC(i);
		float4 polygonColor = PAL_POLYGON_COLOR(i);
		float4 polygonNormal = PAL_POLYGON_NORMAL(i);
		float4 polygonCentroid = PAL_POLYGON_CENTROID(i);
		float4 polygonTangent = PAL_POLYGON_TANGENT(i);
		float4 polygonBitangent = PAL_POLYGON_BITANGENT(i);
		float intensity = polygonDesc.z;

		float3 worldPosToPolygonDir = polygonCentroid.xyz - worldPos;

		if( dot( polygonNormal.xyz, worldRefl ) > 0 && dot( polygonNormal.xyz, worldPosToPolygonDir ) < 0 )
		{
			int firstVertexIndex = (int)polygonDesc.x;
			int lastVertexIndex = (int)polygonDesc.y;

			float3 intersectionPoint = RayPlaneIntersection( polygonNormal, _PALVertexBuffer[firstVertexIndex].xyz, worldPos, worldRefl );
			float3 intersectionOffset = intersectionPoint - _PALVertexBuffer[firstVertexIndex].xyz;
			float2 localPoint = float2( dot( polygonTangent, intersectionOffset ), dot( polygonBitangent, intersectionOffset ) );

			// The Crossing Number method
			// http://geomalgorithms.com/a03-_inclusion.html

			uint crossingNumber = 0;
			for( int j=firstVertexIndex; j<lastVertexIndex; j++ )
			{
				int jPlusOne = j+1;
				if( j == lastVertexIndex-1 ) jPlusOne = firstVertexIndex;

       			if( ( ( _PALPointBuffer[j].y <= localPoint.y ) && ( _PALPointBuffer[jPlusOne].y > localPoint.y) ) || 
       		    	( ( _PALPointBuffer[j].y > localPoint.y ) && ( _PALPointBuffer[jPlusOne].y <= localPoint.y) ) ) 
       			{
            		float vt = ( localPoint.y - _PALPointBuffer[j].y ) / ( _PALPointBuffer[jPlusOne].y - _PALPointBuffer[j].y );

            		if( localPoint.x < _PALPointBuffer[j].x + vt * ( _PALPointBuffer[jPlusOne].x - _PALPointBuffer[j].x) )
            		{
            			crossingNumber++;
            		}
				}
        	}

        	if( crossingNumber % 2 != 0 )
        	{
        		specularColor += intensity * polygonColor;
        	}
        }
	}

	return specularColor;
}

float2 PointInPolygonSpace(float4 plane, float3 planeOrigin, float3 planeTangent, float3 planeBitangent, float3 worldRayOrigin, float3 worldRayDir)
{
	float3 intersectionPoint = RayPlaneIntersection( plane, planeOrigin, worldRayOrigin, worldRayDir );
	float3 intersectionOffset = intersectionPoint - planeOrigin;
	return float2( dot( planeTangent, intersectionOffset ), dot( planeBitangent, intersectionOffset ) );
}

float4 PALSmoothSpecularContribution(float3 worldPos, float3 worldRefl0, float3 worldRefl1, float3 worldRefl2, float3 worldRefl3, float3 worldRefl4)
{
	int numPolygons = PAL_NUM_POLYGONS;

	float4 specularColor = 0; 

	for( int i=0; i<numPolygons; i++ )
	{
		float4 polygonDesc = PAL_POLYGON_DESC(i);
		float4 polygonColor = PAL_POLYGON_COLOR(i);
		float4 polygonNormal = PAL_POLYGON_NORMAL(i);
		float4 polygonCentroid = PAL_POLYGON_CENTROID(i);
		float4 polygonTangent = PAL_POLYGON_TANGENT(i);
		float4 polygonBitangent = PAL_POLYGON_BITANGENT(i);
		float intensity = polygonDesc.z;

		float3 worldPosToPolygonDir = polygonCentroid.xyz - worldPos;

		if( dot( polygonNormal.xyz, worldRefl0 ) > 0 && dot( polygonNormal.xyz, worldPosToPolygonDir ) < 0 )
		{
			int firstVertexIndex = (int)polygonDesc.x;
			int lastVertexIndex = (int)polygonDesc.y;

			float3 planeOrigin = _PALVertexBuffer[firstVertexIndex].xyz;

			float2 localPoint0 = PointInPolygonSpace( polygonNormal, planeOrigin, polygonTangent, polygonBitangent, worldPos, worldRefl0 );
			float2 localPoint1 = PointInPolygonSpace( polygonNormal, planeOrigin, polygonTangent, polygonBitangent, worldPos, worldRefl1 );
			float2 localPoint2 = PointInPolygonSpace( polygonNormal, planeOrigin, polygonTangent, polygonBitangent, worldPos, worldRefl2 );
			float2 localPoint3 = PointInPolygonSpace( polygonNormal, planeOrigin, polygonTangent, polygonBitangent, worldPos, worldRefl3 );
			float2 localPoint4 = PointInPolygonSpace( polygonNormal, planeOrigin, polygonTangent, polygonBitangent, worldPos, worldRefl4 );

			uint crossingNumber0 = 0;
			uint crossingNumber1 = 0;
			uint crossingNumber2 = 0;
			uint crossingNumber3 = 0;
			uint crossingNumber4 = 0;

			for( int j=firstVertexIndex; j<lastVertexIndex; j++ )
			{
				int jPlusOne = j+1;
				if( j == lastVertexIndex-1 ) jPlusOne = firstVertexIndex;

				float edgeDistanceY = ( _PALPointBuffer[jPlusOne].y - _PALPointBuffer[j].y );
				float edgeDistanceX = ( _PALPointBuffer[jPlusOne].x - _PALPointBuffer[j].x );

       			if( ( ( _PALPointBuffer[j].y <= localPoint0.y ) && ( _PALPointBuffer[jPlusOne].y > localPoint0.y) ) || 
       		    	( ( _PALPointBuffer[j].y > localPoint0.y ) && ( _PALPointBuffer[jPlusOne].y <= localPoint0.y) ) ) 
       			{
            		float vt = ( localPoint0.y - _PALPointBuffer[j].y ) / edgeDistanceY;
            		if( localPoint0.x < _PALPointBuffer[j].x + vt * edgeDistanceX )
            		{
            			crossingNumber0++;
            		}
				}

				if( ( ( _PALPointBuffer[j].y <= localPoint1.y ) && ( _PALPointBuffer[jPlusOne].y > localPoint1.y) ) || 
       		    	( ( _PALPointBuffer[j].y > localPoint1.y ) && ( _PALPointBuffer[jPlusOne].y <= localPoint1.y) ) ) 
       			{
            		float vt = ( localPoint1.y - _PALPointBuffer[j].y ) / edgeDistanceY;
            		if( localPoint1.x < _PALPointBuffer[j].x + vt * edgeDistanceX )
            		{
            			crossingNumber1++;
            		}
				}

				if( ( ( _PALPointBuffer[j].y <= localPoint2.y ) && ( _PALPointBuffer[jPlusOne].y > localPoint2.y) ) || 
       		    	( ( _PALPointBuffer[j].y > localPoint2.y ) && ( _PALPointBuffer[jPlusOne].y <= localPoint2.y) ) ) 
       			{
            		float vt = ( localPoint2.y - _PALPointBuffer[j].y ) / edgeDistanceY;
            		if( localPoint2.x < _PALPointBuffer[j].x + vt * edgeDistanceX )
            		{
            			crossingNumber2++;
            		}
				}

				if( ( ( _PALPointBuffer[j].y <= localPoint3.y ) && ( _PALPointBuffer[jPlusOne].y > localPoint3.y) ) || 
       		    	( ( _PALPointBuffer[j].y > localPoint3.y ) && ( _PALPointBuffer[jPlusOne].y <= localPoint3.y) ) ) 
       			{
            		float vt = ( localPoint3.y - _PALPointBuffer[j].y ) / edgeDistanceY;
            		if( localPoint3.x < _PALPointBuffer[j].x + vt * edgeDistanceX )
            		{
            			crossingNumber3++;
            		}
				}

				if( ( ( _PALPointBuffer[j].y <= localPoint4.y ) && ( _PALPointBuffer[jPlusOne].y > localPoint4.y) ) || 
       		    	( ( _PALPointBuffer[j].y > localPoint4.y ) && ( _PALPointBuffer[jPlusOne].y <= localPoint4.y) ) ) 
       			{
            		float vt = ( localPoint4.y - _PALPointBuffer[j].y ) / edgeDistanceY;
            		if( localPoint4.x < _PALPointBuffer[j].x + vt * edgeDistanceX )
            		{
            			crossingNumber4++;
            		}
				}
        	}

        	float averageIntensity = 0;
        	if( crossingNumber0 % 2 != 0 ) averageIntensity += 0.2;
        	if( crossingNumber1 % 2 != 0 ) averageIntensity += 0.2;
        	if( crossingNumber2 % 2 != 0 ) averageIntensity += 0.2;
        	if( crossingNumber3 % 2 != 0 ) averageIntensity += 0.2;
        	if( crossingNumber4 % 2 != 0 ) averageIntensity += 0.2;

        	specularColor += averageIntensity * intensity * polygonColor;

        }
	}

	return specularColor;
}

#endif