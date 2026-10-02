// Hand-written HLSL on purpose: the ground shader must evaluate the same start-time formula as
// SectorVFXController.StartAt, and a line-by-line match with the C# code is easier to keep than a node graph.
// Do not change this formula without changing StartAt.
Shader "Shinshi/VFX/SectorGround"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.22,0.018,0.003,0.15)
        [HDR] _EmissionColor("Emission Color", Color) = (3,0.45,0.04,1)
        _NoiseScale("Noise Scale", Float) = 3
        _NoiseSpeed("Noise Speed", Float) = 0.4
        _EdgeWidth("Edge Width (meters)", Range(0.01,0.5)) = 0.06
        _EdgeIntensity("Edge Intensity", Float) = 0.7
        _DissolveAmount("Dissolve Amount", Range(0,1)) = 0
        _FlowSpeed("Flow Speed", Float) = 2
        _ExplosionIntensity("Explosion Intensity", Float) = 4
        [HideInInspector] _Clock("Clock", Float) = 0
        [HideInInspector] _Timing("Timing", Vector) = (1,0.35,0.25,0.45)
        [HideInInspector] _Sweep("Sweep", Vector) = (2,0,4,0.04)
        [HideInInspector] _Shape("Shape", Vector) = (0,6,2.094,0.035)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "SectorGround"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _EmissionColor, _Timing, _Sweep, _Shape;
            float _NoiseScale, _NoiseSpeed, _EdgeWidth, _EdgeIntensity;
            float _DissolveAmount, _FlowSpeed, _ExplosionIntensity, _Clock;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 ground : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                input.positionOS.y += _Shape.w;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.ground = input.positionOS.xz;
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
                float u = _Sweep.y > 0.5 ? 1-i.uv.x : i.uv.x;
                float segments = max(1,_Sweep.z);
                float segment = min(segments-1, floor(saturate(u)*segments));
                float travel = _Sweep.x < 0.5 ? 0 : (_Sweep.x < 1.5 ? i.uv.y : u);
                float start = _Timing.x + travel*_Timing.w + segment*_Sweep.w;
                float age = _Clock-start;
                float charge = saturate(_Clock/max(_Timing.x,0.001));
                float explode = step(0,age)*exp(-max(age,0)*5/max(_Timing.y,0.001));
                float dissolve = saturate((age-_Timing.y)/max(_Timing.z,0.001));
                float radius = lerp(_Shape.x,_Shape.y,i.uv.y);
                float radialEdge = min(i.uv.y*(_Shape.y-_Shape.x),(1-i.uv.y)*(_Shape.y-_Shape.x));
                if (_Shape.x < 0.001) radialEdge = (1-i.uv.y)*_Shape.y;
                float angularEdge = min(i.uv.x,1-i.uv.x)*_Shape.z*radius;
                // A full circle has no artificial angular seam.
                float boundary = _Shape.z > 6.28 ? radialEdge : min(radialEdge,angularEdge);
                float edge = 1-smoothstep(0,max(_EdgeWidth,0.001),boundary);
                float n = Noise(i.ground*_NoiseScale + float2(0,_Clock*_NoiseSpeed));
                float detail = Noise(i.ground*_NoiseScale*3);
                float cracks = pow(saturate(1-abs(detail-0.5)*14),3);
                float flow = pow(saturate(0.5+0.5*sin(radius*9-_Clock*_FlowSpeed*6)),8);
                float shock = step(0,age)*(1-smoothstep(0,0.075,abs(age-0.035)));
                float visible = (1-dissolve)*(1-_DissolveAmount);
                visible *= smoothstep(dissolve-0.15,dissolve+0.15,n+0.2);
                float telegraph = (0.06+0.22*charge)*(0.15+n*0.25+cracks*0.6+flow*0.2);
                float brightness = telegraph + edge*_EdgeIntensity*(0.2+0.6*charge)
                    + explode*_ExplosionIntensity*(cracks+0.2+n*0.3) + shock*1.5;
                float3 color = _BaseColor.rgb + _EmissionColor.rgb*brightness;
                return half4(color, saturate(brightness)*visible);
            }
            ENDHLSL
        }
    }
}
