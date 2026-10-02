// Temporary placeholder look for rectangle telegraphs (boss dash and line blast). It only has to show the range
// and the time flow: fill grows along the rectangle while charging, the dash lights the passed part and a head,
// the line blast flashes once. All timing is decided in C# (RectTelegraphVFXController) from server time, not here.
Shader "Shinshi/VFX/RectGround"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.22,0.018,0.003,0.15)
        [HDR] _EmissionColor("Emission Color", Color) = (3,0.45,0.04,1)
        _NoiseScale("Noise Scale", Float) = 3
        _NoiseSpeed("Noise Speed", Float) = 0.4
        _EdgeWidth("Edge Width (meters)", Range(0.01,0.5)) = 0.08
        _EdgeIntensity("Edge Intensity", Float) = 0.9
        _ExplosionIntensity("Explosion Intensity", Float) = 3
        [HideInInspector] _State("State", Vector) = (0,-1,0,0)
        [HideInInspector] _Size("Size", Vector) = (2,8,0.035,1)
        [HideInInspector] _Timing("Timing", Vector) = (0.3,0.25,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "RectGround"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _EmissionColor, _State, _Size, _Timing;
            float _NoiseScale, _NoiseSpeed, _EdgeWidth, _EdgeIntensity, _ExplosionIntensity;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 ground : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                input.positionOS.y += _Size.z;
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
                float width = _Size.x, length = _Size.y, fade = _Size.w;
                float charge = _State.x, age = _State.y, traveled = _State.z, dash = _State.w;
                float executed = age >= 0 ? 1 : 0;
                float u = i.uv.x, v = i.uv.y;

                float boundary = min(min(u,1-u)*width, min(v,1-v)*length);
                float edge = 1-smoothstep(0,max(_EdgeWidth,0.001),boundary);
                float n = Noise(i.ground*_NoiseScale + float2(0,_Time.y*_NoiseSpeed));

                float filled = step(v,charge);
                float telegraph = (0.06+0.22*charge)*(0.3+n*0.5) + filled*0.1*(1-executed);

                float flash = exp(-max(age,0)*5/max(_Timing.x,0.001));
                float lit = step(v,traveled);
                float head = exp(-abs(v-traveled)*length*1.5);
                float hit = dash > 0.5 ? (lit*0.7 + head*1.6) : (flash*_ExplosionIntensity*(0.3+n*0.5));

                float brightness = telegraph*(1-0.5*executed) + edge*_EdgeIntensity*(0.2+0.6*charge) + executed*hit;
                float3 color = _BaseColor.rgb + _EmissionColor.rgb*brightness;
                return half4(color, saturate(brightness)*fade);
            }
            ENDHLSL
        }
    }
}
