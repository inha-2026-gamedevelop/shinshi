// Hand-written HLSL on purpose: the ground shader must evaluate the same start-time formula as
// SectorVFXController.StartAt, and a line-by-line match with the C# code is easier to keep than a node graph.
// Do not change this formula without changing StartAt.
Shader "Shinshi/VFX/SectorParticle"
{
    Properties { _Softness("Softness", Range(0.1,8)) = 2 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Softness;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.color=i.color; o.uv=i.uv; return o; }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv*2-1;
                float mask=pow(saturate(1-dot(p,p)),_Softness);
                return half4(i.color.rgb,i.color.a*mask);
            }
            ENDHLSL
        }
    }
}
