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

Shader "Hidden/PALSpecularBuffer"
{   
	Properties 
	{
		_PolygonIndex ( "PolygonIndex", int ) = 0
		_UVOriginAndSize ("UVOriginAndSize", Vector ) = (0,0,1,1)
	}
	
	SubShader 
	{
		Tags { "Queue"="Geometry" "RenderType"="Opaque" }
		LOD 100
		ZTest Always
		Cull Off
		ZWrite Off
		Fog { Mode off }
		
		Pass
		{
			Name "ForwardBase" 
			Tags { "LightMode"="ForwardBase" }
			Blend One One
    		
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag
			#pragma fragmentoption ARB_precision_hint_fastest
			#pragma exclude_renderers d3d11_9x glcore gles gles3 

			#include "UnityCG.cginc"
			#include "PAL.cginc"

			int    _PolygonIndex;
			float4 _UVOriginAndSize;

			struct appdata
			{
				float4 vertex   : POSITION;
				float2 texcoord : TEXCOORD0;
			};

			struct v2f 
			{
				float4 pos : SV_POSITION;
				float2 uv  : TEXCOORD0;
			};

			v2f vert (appdata v) 
			{
				v2f o;
				o.pos = mul( UNITY_MATRIX_MVP, v.vertex );
				o.uv = v.texcoord;
				return o;
			}

			// http://www.iquilezles.org/www/articles/distfunctions/distfunctions.htm
			float DistanceToLine(float2 p, float2 a, float2 b)
			{
    			float2 pa = p - a;
    			float2 ba = b - a;
    			float h = clamp( dot( pa, ba ) / dot( ba, ba ), 0.0, 1.0 );
    			return length( pa - ba * h );
			}

			float4 frag (v2f i) : SV_Target  
			{
				float2 aabbInf = _UVOriginAndSize.xy;
				float2 aabbSup = _UVOriginAndSize.xy + _UVOriginAndSize.zw;

				bool2 isGreaterOrEqualThanInf = ( i.uv >= aabbInf );
				bool2 isLesserThanSup = ( i.uv < aabbSup );
				bool2 isInAABB = ( isGreaterOrEqualThanInf && isLesserThanSup );

				if( !isInAABB.x || !isInAABB.y ) return 0;

				float2 localNormalizedCoords = ( _UVOriginAndSize.xy + 0.5 * _UVOriginAndSize.zw - i.uv ) / ( 0.5 * _UVOriginAndSize.zw );
				localNormalizedCoords = -localNormalizedCoords;

				float4 polygonDesc = PAL_POLYGON_DESC(_PolygonIndex);
				float4 polygonNormal = PAL_POLYGON_NORMAL(_PolygonIndex);
				float4 polygonTangent = PAL_POLYGON_TANGENT(_PolygonIndex);
				float4 polygonBitangent = PAL_POLYGON_BITANGENT(_PolygonIndex);
				float4 polygonCircumcircle = PAL_POLYGON_CIRCUMCIRCLE(_PolygonIndex);

				int firstVertexIndex = (int)polygonDesc.x;
				int lastVertexIndex = (int)polygonDesc.y;

				float2 localPoint = localNormalizedCoords * ( polygonCircumcircle.w * _PALPhi );

				// http://geomalgorithms.com/a03-_inclusion.html
				uint crossingNumber = 0;
				float minDistance = 3.4e38;
				for( int j=firstVertexIndex; j<lastVertexIndex; j++ )
				{
					int jPlusOne = j+1;
					if( j == lastVertexIndex-1 ) jPlusOne = firstVertexIndex;

					float4 vj = _PALVertexBuffer[j] - polygonCircumcircle;
					float4 vjPlusOne = _PALVertexBuffer[jPlusOne] - polygonCircumcircle;

					float2 pj = float2( dot( vj.xyz, polygonTangent.xyz ), dot( vj.xyz, polygonBitangent.xyz ) );
					float2 pjPlusOne = float2( dot( vjPlusOne.xyz, polygonTangent.xyz ), dot( vjPlusOne.xyz, polygonBitangent.xyz ) );

					float edgeDistanceY = ( pjPlusOne.y - pj.y );
					float edgeDistanceX = ( pjPlusOne.x - pj.x );

					if( ( ( pj.y <= localPoint.y ) && ( pjPlusOne.y > localPoint.y) ) || 
       		    		( ( pj.y > localPoint.y ) && ( pjPlusOne.y <= localPoint.y) ) ) 
       				{
            			float vt = ( localPoint.y - pj.y ) / edgeDistanceY;

            			if( localPoint.x < pj.x + vt * edgeDistanceX )
            			{
            				crossingNumber++;
            			}
					}

					float dist = DistanceToLine( localPoint, pj, pjPlusOne );
					if( dist > 0 )
					{
						minDistance = min( minDistance, dist );
					}
        		}

        		if( crossingNumber & 1 )
        		{
        			return 0;
        		}
        		else
        		{
					return minDistance;
				}
			}
			ENDCG
		}
	}

	SubShader 
	{
		Tags { "Queue"="Geometry" "RenderType"="Opaque" }
		LOD 100
		ZTest Always
		Cull Off
		ZWrite Off
		Fog { Mode off }
		
		Pass
		{
			Name "ForwardBase" 
			Tags { "LightMode"="ForwardBase" }
			Blend One One
    		
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag
			#pragma fragmentoption ARB_precision_hint_fastest
			#pragma only_renderers glcore gles gles3 
			#pragma extension GL_EXT_gpu_shader4

			#include "UnityCG.cginc"
			#include "PALGL.cginc"

			int    _PolygonIndex;
			float4 _UVOriginAndSize;

			struct appdata
			{
				float4 vertex   : POSITION;
				float2 texcoord : TEXCOORD0;
			};

			struct v2f 
			{
				float4 pos : SV_POSITION;
				float2 uv  : TEXCOORD0;
			};

			v2f vert (appdata v) 
			{
				v2f o;
				o.pos = mul( UNITY_MATRIX_MVP, v.vertex );
				o.uv = v.texcoord;
				return o;
			}

			float2 PALPlanarPolygonVertex(int index)
			{
				float4 vertexBufferData = _PALVertexBuffer[index/2];
				int caseIndex = index%2;
				return vertexBufferData.xy * ( 1 - caseIndex ) + vertexBufferData.zw * caseIndex;
			}

			// http://www.iquilezles.org/www/articles/distfunctions/distfunctions.htm
			float DistanceToLine(float2 p, float2 a, float2 b)
			{
    			float2 pa = p - a;
    			float2 ba = b - a;
    			float h = clamp( dot( pa, ba ) / dot( ba, ba ), 0.0, 1.0 );
    			return length( pa - ba * h );
			}

			float4 frag (v2f i) : SV_Target  
			{
				float2 aabbInf = _UVOriginAndSize.xy;
				float2 aabbSup = _UVOriginAndSize.xy + _UVOriginAndSize.zw;

				bool2 isGreaterOrEqualThanInf = ( i.uv >= aabbInf );
				bool2 isLesserThanSup = ( i.uv < aabbSup );
				bool2 isInAABB = ( isGreaterOrEqualThanInf * isLesserThanSup );

				if( !isInAABB.x || !isInAABB.y ) return 0;

				float2 localNormalizedCoords = ( _UVOriginAndSize.xy + 0.5 * _UVOriginAndSize.zw - i.uv ) / ( 0.5 * _UVOriginAndSize.zw );
				localNormalizedCoords = -localNormalizedCoords;

				float4 polygonDesc = _PALPolygonDesc[_PolygonIndex];
				float4 polygonNormal = _PALPolygonNormal[_PolygonIndex];
				float4 polygonTangent = _PALPolygonTangent[_PolygonIndex];
				float4 polygonBitangent = _PALPolygonBitangent[_PolygonIndex];
				float4 polygonCircumcircle = _PALPolygonCircumcircle[_PolygonIndex];

				int firstVertexIndex = (int)polygonDesc.x;
				int lastVertexIndex = (int)polygonDesc.y;

				float2 localPoint = localNormalizedCoords * ( polygonCircumcircle.w * _PALPhi );

				// http://geomalgorithms.com/a03-_inclusion.html
				uint crossingNumber = 0;
				float minDistance = 3.4e38;
				for( int j=firstVertexIndex; j<lastVertexIndex; j++ )
				{
					int jPlusOne = j+1;
					if( j == lastVertexIndex-1 ) jPlusOne = firstVertexIndex;

					float2 pj = PALPlanarPolygonVertex( j );
					float2 pjPlusOne = PALPlanarPolygonVertex( jPlusOne );

					float edgeDistanceY = ( pjPlusOne.y - pj.y );
					float edgeDistanceX = ( pjPlusOne.x - pj.x );

					if( ( ( pj.y <= localPoint.y ) && ( pjPlusOne.y > localPoint.y) ) || 
       		    		( ( pj.y > localPoint.y ) && ( pjPlusOne.y <= localPoint.y) ) ) 
       				{
            			float vt = ( localPoint.y - pj.y ) / edgeDistanceY;

            			if( localPoint.x < pj.x + vt * edgeDistanceX )
            			{
            				crossingNumber++;
            			}
					}

					float dist = DistanceToLine( localPoint, pj, pjPlusOne );
					if( dist > 0 )
					{
						minDistance = min( minDistance, dist );
					}
        		}

        		if( crossingNumber % 2 == 1 ) 
        		{
        			return 0;
        		}
        		else
        		{
					return minDistance;
				}
			}
			ENDCG
		}
	}
}
