// Temporary placeholder look for the persistent boss ground beam (wolbaek phase 2 P02). The mesh is already clipped to the arena circle,
// so the shader only draws the telegraph fill, the active beam, the server-confirmed hit pulses and the fade-out.
// All timing is decided in C# (BeamGroundVFXController) from server time, not here.
// uv.x runs across the strip width (0..1), uv.y runs along the beam (0..1).
Shader "Shinshi/VFX/BossBeam"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.22,0.018,0.003,0.15)
        [HDR] _EmissionColor("Emission Color", Color) = (3,0.45,0.04,1)
        _NoiseScale("Noise Scale", Float) = 2.5
        _NoiseSpeed("Noise Speed", Float) = 1.2
        _EdgeWidth("Edge Width (meters)", Range(0.02,0.5)) = 0.12
        _EdgeIntensity("Edge Intensity", Float) = 1
        _PulseIntensity("Pulse Intensity", Float) = 2.5
        _TelegraphAlpha("Telegraph Alpha", Range(0,1)) = 0.35
        [HideInInspector] _State("State", Vector) = (0,0,99,1)
        [HideInInspector] _Size("Size", Vector) = (2,0.035,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "BossBeam"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _EmissionColor, _State, _Size;
            float _NoiseScale, _NoiseSpeed, _EdgeWidth, _EdgeIntensity, _PulseIntensity, _TelegraphAlpha;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 ground : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                input.positionOS.y += _Size.y;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                o.ground = world.xz;
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),
                    lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // _State: x = mode (0 telegraph, 1 active), y = telegraph progress or remaining life ratio,
                //         z = seconds since the last server-confirmed hit, w = fade (1 visible .. 0 gone)
                float mode = _State.x, progress = _State.y, pulseAge = _State.z, fade = _State.w;
                float width = max(_Size.x, 0.01);
                float toEdge = min(i.uv.x, 1 - i.uv.x) * width;
                float edge = 1 - smoothstep(0, max(_EdgeWidth, 0.001), toEdge);

                if (mode < 0.5)
                {
                    // Telegraph: dim fill that rises with progress, edges always visible so the fixed lane is readable.
                    float fill = _TelegraphAlpha * saturate(progress);
                    float brightness = fill + edge * _EdgeIntensity * (0.4 + 0.6 * saturate(progress));
                    float3 colorT = _BaseColor.rgb + _EmissionColor.rgb * brightness;
                    return half4(colorT, saturate(brightness) * fade);
                }

                float n = Noise(i.ground * _NoiseScale + float2(_Time.y * _NoiseSpeed, 0));
                float body = 0.35 + 0.4 * n;
                float flash = exp(-pulseAge * 6);
                float center = 1 - abs(i.uv.x * 2 - 1);
                float brightnessA = body * (0.6 + 0.4 * progress) + edge * _EdgeIntensity + flash * _PulseIntensity * (0.3 + 0.7 * center);
                float3 color = _BaseColor.rgb + _EmissionColor.rgb * brightnessA;
                return half4(color, saturate(brightnessA) * fade);
            }
            ENDHLSL
        }
    }
}
