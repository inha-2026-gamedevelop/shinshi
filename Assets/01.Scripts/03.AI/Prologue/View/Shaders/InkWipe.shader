// 프롤로그 일러스트 전환용 UI 셰이더. 오프라인에서 구워 둔 먹 매트(흑백 프레임 묶음)를 읽어 이전 장 그림의 불투명도를 1 - M으로 만든다.
// M은 먹 농도로, 0이면 이전 그림이고 1이면 새 그림이 드러난 곳이며 중간 값은 두 그림이 섞여 보이는 젖은 경계다.
// 매트는 한 장의 아틀라스(_MatteCols x _MatteRows 타일, 왼쪽 위부터 가로 순서)에 선형 R8로 들어 있고, _Progress(0~1)에 맞는 두 프레임을 섞어 읽는다.
// 매트 좌표는 화면 기준이다. _FitScale은 이 그림이 화면에서 차지하는 비율, _MatteScale은 매트를 화면에 덮어씌울 때 자르는 비율이라
// 화면 비율이 달라도 먹방울이 찌그러지지 않는다. 그림 좌표는 스프라이트 UV를 쓰므로 스프라이트 아틀라스에 묶인 그림에는 쓰지 않는다
Shader "Shinshi/UI/InkWipe"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [NoScaleOffset] _MatteAtlas ("Ink Matte Atlas (linear R8)", 2D) = "black" {}
        _Progress ("Progress", Range(0, 1)) = 0
        _MatteCols ("Matte Columns", Float) = 8
        _MatteRows ("Matte Rows", Float) = 8
        _MatteFrames ("Matte Frames", Float) = 60
        _FitScale ("Image Fit Scale (xy)", Vector) = (1, 1, 0, 0)
        _MatteScale ("Matte Cover Scale (xy)", Vector) = (1, 1, 0, 0)
        _WetTint ("Wet Edge Ink Amount", Range(0, 1)) = 0.5
        _InkColor ("Wet Ink Color", Color) = (0.05, 0.045, 0.04, 1)
        _InkBlack ("Visible Black Ink (0 = blend, 1 = black)", Range(0, 1)) = 0
        _InkLag ("Black Ink Reveal Lag", Range(0.1, 1)) = 0.5

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
            sampler2D _MatteAtlas;
            float4 _MatteAtlas_TexelSize;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float _Progress;
            float _MatteCols;
            float _MatteRows;
            float _MatteFrames;
            float4 _FitScale;
            float4 _MatteScale;
            float _WetTint;
            fixed4 _InkColor;
            float _InkBlack;
            float _InkLag;

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

            // 프레임 하나를 아틀라스에서 읽는다. 타일 경계의 이웃 프레임이 섞이지 않게 타일 안쪽 반 텍셀로 가둔다
            float sampleFrame(float frame, float2 muv)
            {
                float col = fmod(frame, _MatteCols);
                float row = floor(frame / _MatteCols);

                float2 tile = float2(1.0 / _MatteCols, 1.0 / _MatteRows);
                float2 pad = 0.5 * _MatteAtlas_TexelSize.xy;
                float2 local = clamp(muv, 0.0, 1.0) * tile;
                local = clamp(local, pad, tile - pad);

                // 텍스처 v는 아래에서 위로 늘고 타일 번호는 위에서 아래로 센다
                float2 uv = float2(col * tile.x, 1.0 - (row + 1.0) * tile.y) + local;
                return tex2Dlod(_MatteAtlas, float4(uv, 0, 0)).r;
            }

            // 진행도에 맞는 두 프레임을 섞어 읽는다
            float sampleMatte(float progress, float2 muv)
            {
                float f = saturate(progress) * (_MatteFrames - 1.0);
                float f0 = floor(f);
                float f1 = min(f0 + 1.0, _MatteFrames - 1.0);
                return lerp(sampleFrame(f0, muv), sampleFrame(f1, muv), f - f0);
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // 그림 UV -> 화면 UV -> 매트 UV, 매트는 화면을 덮도록 한 비율로만 자르므로 방울이 원형을 유지한다
                float2 screenUv = 0.5 + (IN.texcoord - 0.5) * _FitScale.xy;
                float2 matteUv = 0.5 + (screenUv - 0.5) * _MatteScale.xy;

                // 검은 먹 모드는 같은 매트를 두 번 쓴다. p1은 먹이 퍼지며 이전 그림을 덮는 진행도, p2는 조금 늦게 먹이 걷히며
                // 새 그림이 드러나는 진행도다. 둘 사이의 시차가 곧 검은 먹 덩어리가 화면에 머무는 두께이고, 모드를 끄면 둘이 같아진다
                float lag = _InkBlack * _InkLag;
                float span = 1.0 + lag;
                float mCover = sampleMatte(saturate(_Progress * span), matteUv);
                float mReveal = (lag > 0.0) ? sampleMatte(saturate(_Progress * span - lag), matteUv) : mCover;

                // 8비트 계단이 회색 결에서 띠로 보이지 않게 반 단계 안쪽의 디더를 더한다. 끝 프레임은 0과 1이 그대로 유지된다
                float dither = (hash21(IN.vertex.xy) - 0.5) / 255.0;
                mCover = saturate(mCover + dither * step(0.002, mCover) * step(mCover, 0.998));
                mReveal = saturate(mReveal + dither * step(0.002, mReveal) * step(mReveal, 0.998));

                // 블렌드 모드: 중간 농도의 이전 그림만 먹색으로 젖는다. 테두리가 아니라 농도에 비례하므로 두께가 일정하지 않고,
                // M이 0과 1이면 색조가 남지 않는다
                float wet = 4.0 * mCover * (1.0 - mCover);
                color.rgb = lerp(color.rgb, _InkColor.rgb, _WetTint * wet * (1.0 - _InkBlack));

                // 검은 먹 모드: 먹이 닿은 곳은 이전 그림이 먹색으로 덮이고, 먹이 걷힌 곳은 새 그림이 드러난다.
                // 먹이 지나간 자리마다 이전 그림 -> 검정 -> 새 그림 순서다
                color.rgb = lerp(color.rgb, _InkColor.rgb, saturate(mCover * 1.6) * _InkBlack);
                color.a *= 1.0 - mReveal;

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
