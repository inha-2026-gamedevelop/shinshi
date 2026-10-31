// 프롤로그 일러스트 전환용 UI 셰이더. 이전 장 그림 위에 노이즈 마스크를 씌워 먹이 번지며 지워지듯 알파를 걷어 내고,
// 지워지는 경계에 먹색 테두리를 남긴다. _Progress가 0이면 그림 전체가 보이고 1이면 모두 지워진다.
// 마스크 좌표는 스프라이트 UV를 쓰므로 스프라이트 아틀라스에 묶인 그림에는 쓰지 않는다(일러스트는 단독 스프라이트다)
Shader "Shinshi/UI/InkWipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Progress ("Progress", Range(0, 1)) = 0
        _Softness ("Softness", Range(0.01, 0.5)) = 0.14
        _EdgeWidth ("Ink Edge Width", Range(0, 0.3)) = 0.07
        _EdgeColor ("Ink Edge Color", Color) = (0.08, 0.06, 0.05, 0.9)
        _NoiseScale ("Noise Scale", Float) = 3.2
        _Seed ("Seed", Float) = 0
        _Center ("Start Center", Vector) = (0.5, 0.5, 0, 0)
        _RadialWeight ("Radial Weight", Range(0, 1)) = 0.4

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float _Progress;
            float _Softness;
            float _EdgeWidth;
            fixed4 _EdgeColor;
            float _NoiseScale;
            float _Seed;
            float4 _Center;
            float _RadialWeight;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 다섯 겹을 쌓은 번짐 무늬, 겹마다 좌표를 비틀어 먹물이 퍼지는 불규칙한 가장자리를 만든다
            float inkNoise(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 5; i++)
                {
                    value += amplitude * valueNoise(p);
                    p = p * 2.03 + float2(17.1, 9.2);
                    amplitude *= 0.5;
                }
                return value;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                float2 uv = IN.texcoord;
                float2 seeded = uv * _NoiseScale + _Seed;
                float base = inkNoise(seeded);
                float detail = inkNoise(seeded * 2.7 + base * 3.0 + _Seed * 1.7);
                float mask = lerp(base, detail, 0.35);

                // 시작 지점에서 멀수록 늦게 지워진다. 가중치가 클수록 한 점에서 번져 나가는 모양이 되고, 작을수록 얼룩덜룩하게 지워진다
                float radial = saturate(length(uv - _Center.xy) / 0.85);
                float t = lerp(mask, radial, _RadialWeight);

                // t가 문턱보다 작으면 지워진 곳, 문턱에서 _Softness 안쪽은 반쯤 사라지는 번짐, 그 바로 안쪽은 먹색 테두리
                float threshold = lerp(-_Softness, 1.0 + _Softness + _EdgeWidth, _Progress);
                float keep = smoothstep(threshold, threshold + _Softness, t);
                float edge = 1.0 - smoothstep(threshold + _Softness, threshold + _Softness + _EdgeWidth, t);
                edge *= keep;

                color.rgb = lerp(color.rgb, _EdgeColor.rgb, saturate(edge * _EdgeColor.a));
                color.a *= keep;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
