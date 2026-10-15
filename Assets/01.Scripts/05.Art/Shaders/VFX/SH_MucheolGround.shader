Shader "Shinshi/VFX/MucheolGround"
{
    Properties
    {
        [HDR] _EdgeColor ("Boundary", Color) = (2.1,0.68,0.14,1)
        _FillColor ("Earth", Color) = (0.22,0.055,0.018,0.23)
        _Shape ("Circle / Sector / Rectangle", Float) = 1
        _Radius ("Radius", Float) = 5
        _Angle ("Full Angle", Float) = 120
        _Width ("Width", Float) = 3
        _Length ("Length", Float) = 18
        _Extent ("Quad Half Extent", Float) = 5
        _Progress ("Telegraph Progress", Range(0,1)) = 0.5
        _Impact ("Confirmed Impact", Float) = 0
        _Opacity ("Opacity", Range(0,1)) = 1
        _Phase ("Server Elapsed Seconds", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent" }
        Pass
        {
            Name "Ground"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _EdgeColor, _FillColor;
                float _Shape, _Radius, _Angle, _Width, _Length, _Extent;
                float _Progress, _Impact, _Opacity, _Phase;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }
            float2 Hash(float2 p)
            {
                return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3))))*43758.5453);
            }
            float Crack(float2 p)
            {
                float2 cell=floor(p), f=frac(p);
                float first=8, second=8;
                for(int y=-1;y<=1;++y)
                {
                    for(int x=-1;x<=1;++x)
                    {
                        float2 offset=float2(x,y);
                        float2 d=offset+Hash(cell+offset)-f;
                        float v=dot(d,d);
                        if(v<first) { second=first; first=v; }
                        else { second=min(second,v); }
                    }
                }
                return 1-smoothstep(0.012,0.055,second-first);
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p=(input.uv*2-1)*_Extent;
                float r=length(p);
                float angle=abs(atan2(p.x,p.y));
                float halfAngle=radians(_Angle*0.5);
                float distanceInside=_Radius-r;
                float along=r/max(_Radius,0.001);
                if(_Shape>1.5)
                {
                    distanceInside=min(_Width*0.5-abs(p.x),min(p.y,_Length-p.y));
                    along=p.y/max(_Length,0.001);
                }
                else if(_Shape>0.5)
                {
                    // 315-degree attacks keep the rear 45 degrees empty, including impact cracks.
                    clip(halfAngle-angle);
                    float side=r*sin(min(halfAngle-angle,1.570796));
                    distanceInside=min(distanceInside,side);
                }
                clip(distanceInside);
                float aa=max(fwidth(distanceInside),0.015);
                float lineWidth=clamp(_Extent*0.008,0.035,0.11);
                float edge=1-smoothstep(lineWidth,lineWidth+aa,distanceInside);
                float inset=(1-smoothstep(lineWidth*0.4,lineWidth*0.4+aa,abs(distanceInside-lineWidth*3.6)))*0.33;
                float progressLine=1-smoothstep(0.007,0.019,abs(along-_Progress));
                float ripple=pow(saturate(1-abs(along-frac(_Phase*0.48))*15),3)*0.15;
                float crack=Crack(p*1.35);
                float striation=0.5+0.5*sin(p.x*19+p.y*7+sin(p.y*4));
                float fill=_FillColor.a*(0.65+0.35*striation)*lerp(0.6,1,step(along,_Progress));
                float warning=edge+inset+progressLine*0.45+ripple;
                float impactFront=1-smoothstep(0.08,0.18,abs(along-saturate(_Phase*2.8)));
                float impact=crack*(0.35+0.65*exp(-_Phase*3))+impactFront*0.75;
                float energy=lerp(warning,edge*exp(-_Phase*5)+impact,_Impact);
                float alpha=saturate(fill+energy)*_Opacity;
                float3 color=(_FillColor.rgb*fill+_EdgeColor.rgb*energy)*_Opacity;
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}
