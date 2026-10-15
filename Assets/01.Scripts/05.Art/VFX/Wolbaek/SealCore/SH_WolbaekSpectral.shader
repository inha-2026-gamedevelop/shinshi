Shader "Shinshi/VFX/WolbaekSpectral"
{
    Properties
    {
        _BaseMap ("Shape (A)", 2D) = "white" {}
        _NoiseMap ("Cloud Noise (RG)", 2D) = "gray" {}
        [HDR] _Color ("Color", Color) = (0.5, 1.0, 1.7, 1)
        [HDR] _EdgeColor ("Veil Color", Color) = (0.09, 0.22, 0.5, 1)
        _Mode ("Flow / Core / Flame / Flash", Float) = 0
        _NoiseScale ("Cloud Scale", Vector) = (2, 3, 0, 0)
        _Erosion ("Cloud Erosion", Range(0, 0.8)) = 0.36
        _Distortion ("Cloud Distortion", Range(0, 0.25)) = 0.09
        _Brightness ("Brightness", Range(0, 6)) = 1
        _Opacity ("Layer Opacity", Range(0, 1)) = 1
        _VeilWidth ("Veil Spread (m)", Range(0, 0.5)) = 0
        _SoftFade ("Soft Fade (m)", Float) = 0.16
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "WolbaekSpectral"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Color;
                float4 _EdgeColor;
                float4 _NoiseScale;
                float _Mode;
                float _Erosion;
                float _Distortion;
                float _Brightness;
                float _Opacity;
                float _VeilWidth;
                float _SoftFade;
                float _Cull;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float eyeDepth : TEXCOORD4;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                if (_Mode < 0.5)
                {
                    input.positionOS.y += (input.uv.x - 0.5) * _VeilWidth;
                }
                else if (_Mode > 1.5 && _Mode < 2.5)
                {
                    input.positionOS.xyz += input.normalOS * _VeilWidth * saturate(input.uv.y * 4.0);
                }
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.screenPos = ComputeScreenPos(position.positionCS);
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.eyeDepth = -position.positionVS.z;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // The player supplies the absolute clock phase through its restored MPB.
                // Integer texture velocities make the cyclic phase continuous at the wrap.
                float phase = _BaseMap_ST.w;
                float2 uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                float2 cloudUv = input.uv * _NoiseScale.xy;
                float2 broad = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap,
                    cloudUv + float2(phase, -phase)).rg;
                float2 distorted = uv + (broad - 0.5) * _Distortion;
                float shape = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, distorted).a;
                float detail = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap,
                    cloudUv * 2.0 + float2(-phase * 2.0, phase * 3.0) + broad * 0.18).g;
                float cloud = broad.r * 0.72 + detail * 0.28;
                float density = smoothstep(_Erosion, _Erosion + 0.32, cloud);
                float vein = 1.0 - smoothstep(0.025, 0.105, abs(cloud - 0.57));
                float alpha = shape * density * input.color.a * _Color.a;
                float3 tint = lerp(_EdgeColor.rgb, _Color.rgb, saturate(density + vein * 0.25));
                float intensity = 0.55 + density * 0.6 + vein * 0.42;

                if (_Mode > 0.5 && _Mode < 1.5)
                {
                    float facing = abs(dot(normalize(input.normalWS),
                        normalize(GetWorldSpaceViewDir(input.positionWS))));
                    float rim = pow(saturate(1.0 - facing), 2.8);
                    float islands = smoothstep(_Erosion + 0.08, _Erosion + 0.3, cloud);
                    float inner = saturate((input.color.r - 0.7) * 2.0);
                    float body = max(islands, inner * 0.4);
                    alpha = shape * (body * 0.75 + rim * 0.38) * input.color.a * _Color.a;
                    tint = lerp(_EdgeColor.rgb, _Color.rgb,
                        saturate(body + vein * 0.5 + rim * 0.35));
                    intensity = 0.65 + body * 0.85 + vein * 0.15;
                }
                else if (_Mode > 1.5 && _Mode < 2.5)
                {
                    float leading = pow(saturate(1.0 - input.uv.y), 2.0);
                    alpha = shape * lerp(density, max(density, 0.48), leading)
                        * input.color.a * _Color.a;
                    // Fade the original geometry end independently of the scrolling mask.
                    alpha *= 1.0 - smoothstep(0.58, 1.0, input.uv.y);
                    tint = lerp(tint, _Color.rgb * 1.18, leading);
                    intensity += leading * 0.8;
                }
                else if (_Mode > 2.5)
                {
                    float facing = abs(dot(normalize(input.normalWS),
                        normalize(GetWorldSpaceViewDir(input.positionWS))));
                    float falloff = smoothstep(0.0, 0.82, facing);
                    float shell = 1.0 - smoothstep(0.2, 0.5, input.color.a);
                    alpha = shape * input.color.a * _Color.a
                        * lerp(1.0, falloff * falloff * lerp(0.65, 1.0, density), shell);
                    tint = _Color.rgb;
                    intensity = 1.0;
                }

                float sceneDepth = LinearEyeDepth(
                    SampleSceneDepth(input.screenPos.xy / input.screenPos.w), _ZBufferParams);
                alpha *= saturate((sceneDepth - input.eyeDepth) / max(_SoftFade, 0.0001));
                alpha *= _Opacity;
                float3 color = tint * input.color.rgb * alpha * intensity * _Brightness;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
