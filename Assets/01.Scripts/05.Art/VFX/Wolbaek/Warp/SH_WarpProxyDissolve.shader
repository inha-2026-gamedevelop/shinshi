// 개발용 neutral proxy의 깊이와 실제 구간별 분해를 위해 별도 사용한다.
// 기존 Particle는 ZWrite Off라 겹친 인체면이 비치며 3D dissolve가 없다.
// 승인된 전용 shader 범위에서 간단한 proxy AO/normal/dissolve를 작성했다.
// 실제 캐릭터 Lit 재질의 대체가 아니며 공용/공급자 shader는 수정하지 않는다.
Shader "Shinshi/VFX/WarpProxyDissolve"
{
    Properties
    {
        _BaseColor ("Neutral Body", Color) = (0.6, 0.66, 0.74, 0.46)
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        [HDR] _EdgeColor ("Moon Edge", Color) = (0.7, 1.3, 2, 1)
        _NoiseScale ("Noise Scale", Float) = 18
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EdgeColor;
                float _Dissolve;
                float _NoiseScale;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
                float3 viewWS : TEXCOORD3;
            };
            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float Noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x),
                    lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                    lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.viewWS = GetWorldSpaceViewDir(TransformObjectToWorld(input.positionOS.xyz));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float density = Noise(input.positionOS * _NoiseScale) * 0.72
                    + Noise(input.positionOS * _NoiseScale * 2.7 + 5) * 0.28;
                float distance = density - _Dissolve;
                clip(distance - 0.0001);
                Light light = GetMainLight();
                float diffuse = saturate(dot(normalize(input.normalWS), light.direction));
                float3 color = _BaseColor.rgb * input.color.rgb * (0.45 + diffuse * 0.65);
                float edge = (1 - smoothstep(0, 0.13, distance)) * step(0.015, _Dissolve);
                float rim = pow(1 - saturate(dot(normalize(input.normalWS), normalize(input.viewWS))), 3);
                return half4(color + _EdgeColor.rgb * (edge * 2.8 + rim * 0.3), max(_BaseColor.a, edge * 0.9));
            }
            ENDHLSL
        }
    }
}
