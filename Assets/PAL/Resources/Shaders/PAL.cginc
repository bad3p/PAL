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

#define PAL_POLYGON_DESC(polygonIndex) _PALPolygonBuffer[polygonIndex*8] 
#define PAL_POLYGON_COLOR(polygonIndex) _PALPolygonBuffer[polygonIndex*8+1]
#define PAL_POLYGON_NORMAL(polygonIndex) _PALPolygonBuffer[polygonIndex*8+2]
#define PAL_POLYGON_TANGENT(polygonIndex) _PALPolygonBuffer[polygonIndex*8+3]
#define PAL_POLYGON_BITANGENT(polygonIndex) _PALPolygonBuffer[polygonIndex*8+4]
#define PAL_POLYGON_CENTROID(polygonIndex) _PALPolygonBuffer[polygonIndex*8+5]
#define PAL_POLYGON_CIRCUMCIRCLE(polygonIndex) _PALPolygonBuffer[polygonIndex*8+6]
#define PAL_POLYGON_LOCAL_CIRCUMCIRCLE(polygonIndex) _PALPolygonBuffer[polygonIndex*8+7]

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

int isLeft(float2 p0, float2 p1, float2 p2)
{
    return ( (p1.x - p0.x) * (p2.y - p0.y) - (p2.x -  p0.x) * (p1.y - p0.y) );
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
		float4 polygonLocalCircumcircle = PAL_POLYGON_LOCAL_CIRCUMCIRCLE(i);
		float intensity = polygonDesc.z;

		float3 worldPosToPolygonDir = polygonCentroid.xyz - worldPos;

		if( dot( polygonNormal.xyz, worldRefl ) > 0 && dot( polygonNormal.xyz, worldPosToPolygonDir ) < 0 )
		{
			int firstVertexIndex = (int)polygonDesc.x;
			int lastVertexIndex = (int)polygonDesc.y;

			float3 intersectionPoint = RayPlaneIntersection( polygonNormal, _PALVertexBuffer[firstVertexIndex].xyz, worldPos, worldRefl );
			float3 intersectionOffset = intersectionPoint - _PALVertexBuffer[firstVertexIndex].xyz;
			float2 localPoint = float2( dot( polygonTangent, intersectionOffset ), dot( polygonBitangent, intersectionOffset ) );

			if( distance( localPoint, polygonLocalCircumcircle.xy ) < polygonLocalCircumcircle.z )
			{
				// http://geomalgorithms.com/a03-_inclusion.html
				uint crossingNumber = 0;
				for( int j=firstVertexIndex; j<lastVertexIndex; j++ )
				{
					int jPlusOne = j+1;
					if( j == lastVertexIndex-1 ) jPlusOne = firstVertexIndex;

					//float edgeDistanceY = ( _PALPointBuffer[jPlusOne].y - _PALPointBuffer[j].y );
					//float edgeDistanceX = ( _PALPointBuffer[jPlusOne].x - _PALPointBuffer[j].x );

					float edgeDistanceY = _PALPointBuffer[j].w;
					float edgeDistanceX = _PALPointBuffer[j].z;

       				if( ( ( _PALPointBuffer[j].y <= localPoint.y ) && ( _PALPointBuffer[jPlusOne].y > localPoint.y) ) || 
       		    		( ( _PALPointBuffer[j].y > localPoint.y ) && ( _PALPointBuffer[jPlusOne].y <= localPoint.y) ) ) 
       				{
            			float vt = ( localPoint.y - _PALPointBuffer[j].y ) / edgeDistanceY;

            			if( localPoint.x < _PALPointBuffer[j].x + vt * edgeDistanceX )
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
	}

	return specularColor;
}

float2 PointInPolygonSpace(float4 plane, float3 planeOrigin, float3 planeTangent, float3 planeBitangent, float3 worldRayOrigin, float3 worldRayDir)
{
	float3 intersectionPoint = RayPlaneIntersection( plane, planeOrigin, worldRayOrigin, worldRayDir );
	float3 intersectionOffset = intersectionPoint - planeOrigin;
	return float2( dot( planeTangent, intersectionOffset ), dot( planeBitangent, intersectionOffset ) );
}

float4 PALSmoothSpecularContribution(float3 worldPos, float3 worldRefl1, float3 worldRefl2, float3 worldRefl3, float3 worldRefl4)
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

		if( dot( polygonNormal.xyz, worldRefl1 ) > 0 && dot( polygonNormal.xyz, worldPosToPolygonDir ) < 0 )
		{
			int firstVertexIndex = (int)polygonDesc.x;
			int lastVertexIndex = (int)polygonDesc.y;

			float3 planeOrigin = _PALVertexBuffer[firstVertexIndex].xyz;

			float2 localPoint1 = PointInPolygonSpace( polygonNormal, planeOrigin, polygonTangent, polygonBitangent, worldPos, worldRefl1 );
			float2 localPoint2 = PointInPolygonSpace( polygonNormal, planeOrigin, polygonTangent, polygonBitangent, worldPos, worldRefl2 );
			float2 localPoint3 = PointInPolygonSpace( polygonNormal, planeOrigin, polygonTangent, polygonBitangent, worldPos, worldRefl3 );
			float2 localPoint4 = PointInPolygonSpace( polygonNormal, planeOrigin, polygonTangent, polygonBitangent, worldPos, worldRefl4 );
			float4 localPointY = float4( localPoint1.y, localPoint2.y, localPoint3.y, localPoint4.y );

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

				//float edgeDistanceY = _PALPointBuffer[j].w;
				//float edgeDistanceX = _PALPointBuffer[j].z;

				float4 vtj = ( localPointY - _PALPointBuffer[j].y ) / edgeDistanceY;
				vtj = _PALPointBuffer[j].x + vtj * edgeDistanceX;

				float4 vyj = float4( _PALPointBuffer[j].y, _PALPointBuffer[j].y, _PALPointBuffer[j].y, _PALPointBuffer[j].y );
				float4 vyjPlusOne = float4( _PALPointBuffer[jPlusOne].y, _PALPointBuffer[jPlusOne].y, _PALPointBuffer[jPlusOne].y, _PALPointBuffer[jPlusOne].y );

				bool4 vyj_lesserOrEqualThan_lpy = ( vyj <= localPointY );
				bool4 vyjPlusOne_greaterThan_lpy = ( vyjPlusOne > localPointY );
				bool4 vyj_greaterThan_lpy = ( vyj > localPointY );
				bool4 vyjPlusOne_lesserOrEqualThan_lpy = ( vyjPlusOne <= localPointY );
				bool4 vyj_lesserOrEqualThan_lpy_and_vyjPlusOne_greaterThan_lpy = vyj_lesserOrEqualThan_lpy && vyjPlusOne_greaterThan_lpy;
				bool4 vyj_greaterThan_lpy_and_vyjPlusOne_lesserOrEqualThan_lpy = vyj_greaterThan_lpy && vyjPlusOne_lesserOrEqualThan_lpy;
				bool4 finalCondition = vyj_lesserOrEqualThan_lpy_and_vyjPlusOne_greaterThan_lpy || vyj_greaterThan_lpy_and_vyjPlusOne_lesserOrEqualThan_lpy;

				//if( ( ( _PALPointBuffer[j].y <= localPoint1.y ) && ( _PALPointBuffer[jPlusOne].y > localPoint1.y) ) || 
       		    //	( ( _PALPointBuffer[j].y > localPoint1.y ) && ( _PALPointBuffer[jPlusOne].y <= localPoint1.y) ) ) 
       		    if( finalCondition.x )
       			{
            		//float vt = ( localPoint1.y - _PALPointBuffer[j].y ) / edgeDistanceY;
            		//if( localPoint1.x < _PALPointBuffer[j].x + vtj.x * edgeDistanceX )
            		if( localPoint1.x < vtj.x )
            		{
            			crossingNumber1++;
            		}
				}

				//if( ( ( _PALPointBuffer[j].y <= localPoint2.y ) && ( _PALPointBuffer[jPlusOne].y > localPoint2.y) ) || 
       		    //	( ( _PALPointBuffer[j].y > localPoint2.y ) && ( _PALPointBuffer[jPlusOne].y <= localPoint2.y) ) ) 
       		    if( finalCondition.y )
       			{
            		//float vt = ( localPoint2.y - _PALPointBuffer[j].y ) / edgeDistanceY;
            		//if( localPoint2.x < _PALPointBuffer[j].x + vtj.y * edgeDistanceX )
            		if( localPoint2.x < vtj.y )
            		{
            			crossingNumber2++;
            		}
				}

				//if( ( ( _PALPointBuffer[j].y <= localPoint3.y ) && ( _PALPointBuffer[jPlusOne].y > localPoint3.y) ) || 
       		    //	( ( _PALPointBuffer[j].y > localPoint3.y ) && ( _PALPointBuffer[jPlusOne].y <= localPoint3.y) ) ) 
       		    if( finalCondition.z )
       			{
            		//float vt = ( localPoint3.y - _PALPointBuffer[j].y ) / edgeDistanceY;
            		//if( localPoint3.x < _PALPointBuffer[j].x + vtj.z * edgeDistanceX )
            		if( localPoint3.x < vtj.z )
            		{
            			crossingNumber3++;
            		}
				}

				//if( ( ( _PALPointBuffer[j].y <= localPoint4.y ) && ( _PALPointBuffer[jPlusOne].y > localPoint4.y) ) || 
       		    //	( ( _PALPointBuffer[j].y > localPoint4.y ) && ( _PALPointBuffer[jPlusOne].y <= localPoint4.y) ) ) 
       		    if( finalCondition.w )
       			{
            		//float vt = ( localPoint4.y - _PALPointBuffer[j].y ) / edgeDistanceY;
            		//if( localPoint4.x < _PALPointBuffer[j].x + vtj.w * edgeDistanceX )
            		if( localPoint4.x < vtj.w )
            		{
            			crossingNumber4++;
            		}
				}
        	}

        	float averageIntensity = 0;
        	if( crossingNumber1 & 1 ) averageIntensity += 0.25;
        	if( crossingNumber2 & 1 ) averageIntensity += 0.25;
        	if( crossingNumber3 & 1 ) averageIntensity += 0.25;
        	if( crossingNumber4 & 1 ) averageIntensity += 0.25;

        	specularColor += averageIntensity * intensity * polygonColor;

        }
	}

	return specularColor;
}

#endif