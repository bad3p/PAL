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

Shader "PAL/Opaque" 
{   
	Properties 
	{
		_MainTex ("Base (RGB)", 2D) = "white" {}
		[Toggle(_PAL_SPECULAR)] _Specular ("Specular", float) = 0
		_PhongExponent ("Phong Exponent", Range(1.0, 100.0)) = 2.0
		[Toggle(_PAL_BUMPY)] _Bumpiness ("Bumpiness", float) = 0
		_NormalMap ("Normal map", 2D) = "white" {}
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
			#pragma exclude_renderers d3d11_9x glcore gles gles3

			#pragma multi_compile _ _PAL_SPECULAR
			#pragma multi_compile _ _PAL_BUMPY
			#pragma multi_compile _ _PAL_PROJECTION_WEIGHTED
			#pragma multi_compile _ _PAL_ALBEDO
			#pragma multi_compile_fog

			#include "UnityCG.cginc"
			#include "PAL.cginc"

			sampler2D _MainTex;
			float4    _MainTex_ST;
			float     _PhongExponent;

			#if defined(_PAL_BUMPY)
				sampler2D _NormalMap;
			#endif

			struct appdata 
			{
				float4 vertex   : POSITION;
				float3 normal   : NORMAL;
				#if defined(_PAL_BUMPY)
					float4 tangent : TANGENT;
				#endif	
				float2 texcoord : TEXCOORD0;
			};

			struct v2f 
			{
				float4 pos          : SV_POSITION;
				float2 uv           : TEXCOORD0;
				float3 worldPos     : TEXCOORD1;
				float3 worldViewDir : TEXCOORD2;
				#if defined(_PAL_BUMPY)
					float3 tangentSpace0 : TEXCOORD3;
					float3 tangentSpace1 : TEXCOORD4;
					float3 tangentSpace2 : TEXCOORD5;
					#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                		UNITY_FOG_COORDS(6)
                	#endif
				#else
					float3 worldNormal  : TEXCOORD3;
					#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                		UNITY_FOG_COORDS(4)
                	#endif
				#endif
			};

			v2f vert (appdata v) 
			{
				v2f o;
				o.pos = mul( UNITY_MATRIX_MVP, v.vertex );
				o.uv = TRANSFORM_TEX( v.texcoord, _MainTex );
				o.worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				o.worldViewDir = WorldSpaceViewDir( v.vertex );

				#if defined(_PAL_BUMPY)
					float3 worldViewDir = normalize( UnityWorldSpaceViewDir( o.worldPos ) );
					float3 worldNormal = normalize( mul( (float3x3)unity_ObjectToWorld, v.normal ) );
					float3 worldTangent = UnityObjectToWorldDir( v.tangent.xyz );
					float3 worldBitangent = cross( worldNormal, worldTangent ) * v.tangent.w;	
					o.tangentSpace0 = float4( worldTangent.x, worldBitangent.x, worldNormal.x, worldViewDir.x );
					o.tangentSpace1 = float4( worldTangent.y, worldBitangent.y, worldNormal.y, worldViewDir.y );
					o.tangentSpace2 = float4( worldTangent.z, worldBitangent.z, worldNormal.z, worldViewDir.z );

					#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                		UNITY_TRANSFER_FOG(o,o.pos); 
                	#endif
                #else
                	o.worldNormal = mul( (float3x3)unity_ObjectToWorld, v.normal );

                	#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                		UNITY_TRANSFER_FOG(o,o.pos); 
                	#endif
				#endif
				return o;
			}

			float4 frag (v2f i) : SV_Target  
			{
				#if defined(_PAL_ALBEDO)
					return tex2D( _MainTex, i.uv );
				#else
					float4 result = tex2D( _MainTex, i.uv );

					#if defined(_PAL_BUMPY)
						float3 tangentSpaceNormal = UnpackNormal( tex2D( _NormalMap, i.uv ) ).xyz;
						float3 worldNormal = normalize( float3( dot( i.tangentSpace0, tangentSpaceNormal ), dot( i.tangentSpace1, tangentSpaceNormal ), dot( i.tangentSpace2, tangentSpaceNormal ) ) );
					#else
						float3 worldNormal = normalize( i.worldNormal );
					#endif

					result.xyz *= ( UNITY_LIGHTMODEL_AMBIENT + PALDiffuseContribution( i.worldPos, worldNormal ) );

					#if defined(_PAL_SPECULAR)
						float3 worldRefl = reflect( i.worldViewDir, worldNormal );
						result.xyz += PALBufferedSpecularContribution( i.worldPos, worldNormal, i.worldViewDir, worldRefl, _PhongExponent );
					#endif

					#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                		UNITY_APPLY_FOG( i.fogCoord, result ); 
                	#endif

					return result;
				#endif
			}
			ENDCG
		}
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
			#pragma only_renderers glcore gles gles3

			#pragma multi_compile _ _PAL_SPECULAR
			#pragma multi_compile _ _PAL_BUMPY
			#pragma multi_compile _ _PAL_PROJECTION_WEIGHTED
			#pragma multi_compile _ _PAL_ALBEDO
			#pragma multi_compile_fog

			#include "UnityCG.cginc"
			#include "PALGL.cginc"

			sampler2D _MainTex;
			float4    _MainTex_ST;
			float     _PhongExponent;

			#if defined(_PAL_BUMPY)
				sampler2D _NormalMap;
			#endif

			struct appdata 
			{
				float4 vertex   : POSITION;
				float3 normal   : NORMAL;
				#if defined(_PAL_BUMPY)
					float4 tangent : TANGENT;
				#endif	
				float2 texcoord : TEXCOORD0;
			};

			struct v2f 
			{
				float4 pos          : SV_POSITION;
				float2 uv           : TEXCOORD0;
				float3 worldPos     : TEXCOORD1;
				float3 worldViewDir : TEXCOORD2;
				#if defined(_PAL_BUMPY)
					float3 tangentSpace0 : TEXCOORD3;
					float3 tangentSpace1 : TEXCOORD4;
					float3 tangentSpace2 : TEXCOORD5;
					#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                		UNITY_FOG_COORDS(6)
                	#endif
				#else
					float3 worldNormal  : TEXCOORD3;
					#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                		UNITY_FOG_COORDS(4)
                	#endif
				#endif
			};

			v2f vert (appdata v) 
			{
				v2f o;
				o.pos = mul( UNITY_MATRIX_MVP, v.vertex );
				o.uv = TRANSFORM_TEX( v.texcoord, _MainTex );
				o.worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				o.worldViewDir = WorldSpaceViewDir( v.vertex );

				#if defined(_PAL_BUMPY)
					float3 worldViewDir = normalize( UnityWorldSpaceViewDir( o.worldPos ) );
					float3 worldNormal = normalize( mul( (float3x3)unity_ObjectToWorld, v.normal ) );
					float3 worldTangent = UnityObjectToWorldDir( v.tangent.xyz );
					float3 worldBitangent = cross( worldNormal, worldTangent ) * v.tangent.w;	
					o.tangentSpace0 = float4( worldTangent.x, worldBitangent.x, worldNormal.x, worldViewDir.x );
					o.tangentSpace1 = float4( worldTangent.y, worldBitangent.y, worldNormal.y, worldViewDir.y );
					o.tangentSpace2 = float4( worldTangent.z, worldBitangent.z, worldNormal.z, worldViewDir.z );

					#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                		UNITY_TRANSFER_FOG(o,o.pos); 
                	#endif
                #else
                	o.worldNormal = mul( (float3x3)unity_ObjectToWorld, v.normal );

                	#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                		UNITY_TRANSFER_FOG(o,o.pos); 
                	#endif
				#endif
				return o;
			}

			float4 frag (v2f i) : SV_Target  
			{
				#if defined(_PAL_ALBEDO)
					return tex2D( _MainTex, i.uv );
				#else
					float4 result = tex2D( _MainTex, i.uv );

					#if defined(_PAL_BUMPY)
						float3 tangentSpaceNormal = UnpackNormal( tex2D( _NormalMap, i.uv ) ).xyz;
						float3 worldNormal = normalize( float3( dot( i.tangentSpace0, tangentSpaceNormal ), dot( i.tangentSpace1, tangentSpaceNormal ), dot( i.tangentSpace2, tangentSpaceNormal ) ) );
					#else
						float3 worldNormal = normalize( i.worldNormal );
					#endif

					result.xyz *= ( UNITY_LIGHTMODEL_AMBIENT + PALDiffuseContribution( i.worldPos, worldNormal ) );

					#if defined(_PAL_SPECULAR)
						float3 worldRefl = reflect( i.worldViewDir, worldNormal );
						result.xyz += PALBufferedSpecularContribution( i.worldPos, worldNormal, i.worldViewDir, worldRefl, _PhongExponent );
					#endif

					#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                		UNITY_APPLY_FOG( i.fogCoord, result ); 
                	#endif

					return result;
				#endif
			}
			ENDCG
		}
	}
}
