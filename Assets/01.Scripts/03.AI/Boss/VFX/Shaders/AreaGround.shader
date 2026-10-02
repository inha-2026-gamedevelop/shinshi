// Temporary placeholder look for the persistent boss ground area (wolbaek P05). It only has to show the circle,
// that it is alive, the server-confirmed hit pulses and the fade-out. All timing is decided in C#
// (AreaGroundVFXController) from server time, not here.
Shader "Shinshi/VFX/AreaGround"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.12,0.02,0.2,0.2)
        [HDR] _EmissionColor("Emission Color", Color) = (1.4,0.35,2.4,1)
        _NoiseScale("Noise Scale", Float) = 2.5
        _NoiseSpeed("Noise Speed", Float) = 0.5
        _EdgeWidth("Edge Width (meters)", Range(0.02,0.5)) = 0.12
        _EdgeIntensity("Edge Intensity", Float) = 1
        _PulseIntensity("Pulse Intensity", Float) = 2.5
        [HideInInspector] _State("State", Vector) = (1,99,1,3)
        [HideInInspector] _Size("Size", Vector) = (3,0.035,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "AreaGround"
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
            float _NoiseScale, _NoiseSpeed, _EdgeWidth, _EdgeIntensity, _PulseIntensity;
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
                float life = _State.x, pulseAge = _State.y, fade = _State.z, radius = max(_State.w, 0.01);
                float d = length(i.uv*2-1);
                clip(1-d);

                float toEdge = (1-d)*radius;
                float edge = 1-smoothstep(0,max(_EdgeWidth,0.001),toEdge);
                float n = Noise(i.ground*_NoiseScale + float2(_Time.y*_NoiseSpeed,0));
                float body = 0.18 + 0.32*n;

                // Server-confirmed hit: a flash and a ring that sweeps outward from the center.
                float flash = exp(-pulseAge*5);
                float ring = exp(-abs(d - saturate(pulseAge*1.6))*14) * step(pulseAge,0.65);
                float brightness = body*(0.6+0.4*life) + edge*_EdgeIntensity + (flash*0.5 + ring)*_PulseIntensity*(0.3+0.7*n);

                float3 color = _BaseColor.rgb + _EmissionColor.rgb*brightness;
                return half4(color, saturate(brightness)*fade);
            }
            ENDHLSL
        }
    }
}
