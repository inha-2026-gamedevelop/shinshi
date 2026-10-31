Shader "Shinshi/Lobby/Title Mist Skybox"
{
    Properties
    {
        _MistColor ("Mist Color", Color) = (0.20, 0.21, 0.23, 1)
        _Density ("Mist Density", Range(0, 1)) = 0.22
        _Speed ("Drift Speed", Range(0, 1)) = 0.035
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 vertex : SV_POSITION; float4 screen : TEXCOORD0; };
            float4 _MistColor;
            float _Density;
            float _Speed;

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                            lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                uv.x *= _ScreenParams.x / _ScreenParams.y;
                float time = _Time.y * _Speed;
                float2 p = uv * float2(3.5, 5.0) + float2(time, time * 0.21);
                float clouds = noise(p) * 0.55 + noise(p * 2.03 - time * 0.33) * 0.28
                    + noise(p * 4.1 + time * 0.19) * 0.17;
                float band = smoothstep(0.13, 0.42, uv.y) * (1.0 - smoothstep(0.58, 0.90, uv.y));
                float mist = smoothstep(0.43, 0.74, clouds) * band * _Density;
                return fixed4(_MistColor.rgb * mist, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
