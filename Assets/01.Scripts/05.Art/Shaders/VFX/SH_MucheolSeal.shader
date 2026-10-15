Shader "Shinshi/VFX/MucheolSeal"
{
    Properties
    {
        [HDR] _Color ("Seal", Color) = (0.3,1.8,1.3,1)
        _Progress ("Confirmed Hits", Range(0,1)) = 0
        _Pulse ("Counted Hit", Range(0,1)) = 0
        _Opacity ("Opacity", Range(0,1)) = 1
        _Phase ("Elapsed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Progress,_Pulse,_Opacity,_Phase;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; return o; }
            float Stroke(float d,float w) { return 1-smoothstep(w,w+0.015,abs(d)); }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv*2-1;
                float r=length(p), angle=atan2(p.x,p.y);
                float sector=frac(angle/6.2831853+1);
                float dash=step(0.12,frac(sector*10));
                float ring=Stroke(r-0.84,0.018)*dash;
                float progress=step(sector,_Progress);
                float diamond=Stroke(abs(p.x)+abs(p.y)-0.7,0.016);
                float vertical=Stroke(p.x,0.016)*step(abs(p.y),0.43);
                float upper=Stroke(p.y-0.24,0.018)*step(abs(p.x),0.26);
                float lower=Stroke(p.y+0.08,0.018)*step(abs(p.x),0.2);
                float hooks=Stroke(abs(p.x)-0.2,0.016)*step(abs(p.y+0.18),0.11);
                float glyph=max(max(vertical,upper),max(lower,hooks));
                float energy=ring*lerp(0.24,1.2,progress)+diamond*0.5+glyph*0.9;
                energy*=1+_Pulse*1.3;
                float halo=exp(-r*r*6)*0.07;
                float alpha=saturate(energy+halo)*_Opacity;
                return half4(_Color.rgb*(energy+halo)*_Opacity,alpha);
            }
            ENDHLSL
        }
    }
}
