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

Shader "PAL/IlluminationBuffer"
{   
	Properties 
	{
	}
	
	SubShader 
	{
		Tags { "Queue"="Geometry" "RenderType"="Opaque" }
		LOD 100
		ZTest Less
		Cull Back
		ZWrite On
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
			#include "PAL.cginc"

			float _IlluminationScale;

			struct appdata 
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
			};

			struct v2f 
			{
				float4 pos         : SV_POSITION;
				float3 worldPos    : TEXCOORD0;
				float3 worldNormal : TEXCOORD1;
			};

			v2f vert (appdata v) 
			{
				v2f o;
				o.pos = mul( UNITY_MATRIX_MVP, v.vertex );
				o.worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				o.worldNormal = mul( unity_ObjectToWorld, float4( v.normal, 0 ) ).xyz;
				return o;
			}

			float4 frag (v2f i) : SV_Target  
			{
				float3 worldNormal = normalize( i.worldNormal );
				float intensity = PALIntensity( i.worldPos, worldNormal );
				return EncodeFloatRGBA( clamp( intensity * _IlluminationScale, 0, 0.9999999 ) );
			}
			ENDCG
		}
	}
}
