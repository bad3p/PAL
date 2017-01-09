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

Shader "PAL/MergeBuffer"
{   
	Properties 
	{
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
			Blend One Zero
    		
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag
			#pragma fragmentoption ARB_precision_hint_fastest
			#pragma exclude_renderers d3d11_9x

			#include "UnityCG.cginc"

			sampler2D_float _NormalBuffer;
			sampler2D_float _GeometryBuffer;
			float4          _PixelSize;

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

			float AreEqual(float3 v0, float3 v1)
			{
				return step( 0.9999, abs( dot( v0, v1 ) ) );
			}

			float IsTangent(float3 vec, float3 norm)
			{
				return step( abs( dot( vec, norm ) ), 0.0001 );
			}

			float4 frag (v2f i) : SV_Target  
			{
				float4 result = float4( 0,0,0,1 );

				float3 fragmentWorldNormal = normalize( tex2D( _NormalBuffer, i.uv ).xyz );
				float4 geometryBufferSample = tex2D( _GeometryBuffer, i.uv );
				float3 fragmentWorldPos = geometryBufferSample.xyz;

				if( i.uv.x > 0 )
				{
					float2 leftFragmentCoords = i.uv - float2( _PixelSize.x, 0 );
					float3 leftFragmentWorldNormal = normalize( tex2D( _NormalBuffer, leftFragmentCoords ).xyz );
					float4 leftGeometryBufferSample = tex2D( _GeometryBuffer, leftFragmentCoords );
					float3 leftFragmentWorldPos = leftGeometryBufferSample.xyz;
					float3 leftSurfaceWorldTangent = normalize( leftFragmentWorldPos - fragmentWorldPos );
					result.r = IsTangent( leftSurfaceWorldTangent, fragmentWorldNormal ) * AreEqual( fragmentWorldNormal, leftFragmentWorldNormal );
				}

				if( i.uv.y > 0 )
				{
					float2 lowerFragmentCoords = i.uv - float2( 0, _PixelSize.y );
					float3 lowerFragmentWorldNormal = normalize( tex2D( _NormalBuffer, lowerFragmentCoords ).xyz );
					float4 lowerGeometryBufferSample = tex2D( _GeometryBuffer, lowerFragmentCoords );
					float3 lowerFragmentWorldPos = lowerGeometryBufferSample.xyz;
					float3 lowerSurfaceWorldTangent = normalize( lowerFragmentWorldPos - fragmentWorldPos );
					result.g = IsTangent( lowerSurfaceWorldTangent, fragmentWorldNormal ) * AreEqual( fragmentWorldNormal, lowerFragmentWorldNormal );
				}

				return result;
			}
			ENDCG
		}
	}
}
