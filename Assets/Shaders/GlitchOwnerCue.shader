Shader "Capstone/GlitchOwnerCue"
{
    // 비소유자에게 보여주는 글리치 오버레이용 URP 호환 Unlit 셰이더.
    // 보통 "원본 메시를 살짝 부풀린 자식 GameObject"에 이 셰이더 머티리얼을 적용한다.
    // 시간 기반 wobble + RGB shift + scanline + 가끔씩 튀는 노이즈 블록.
    Properties
    {
        _BaseColor       ("Base Color (RGBA)",     Color)       = (0.55, 0.20, 0.85, 0.55)
        _GlitchAmount    ("Glitch Amount",         Range(0,1))  = 0.5
        _GlitchSpeed     ("Glitch Speed",          Float)       = 6.0
        _RGBShift        ("RGB Shift",             Range(0,0.05))= 0.012
        _ScanlineFreq    ("Scanline Frequency",    Float)       = 220.0
        _ScanlineStrength("Scanline Strength",     Range(0,1))  = 0.45
        _RowDensity      ("Row Density",           Float)       = 30.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            Name "GlitchOwnerCue"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attr
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct V2F
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _GlitchAmount;
                float  _GlitchSpeed;
                float  _RGBShift;
                float  _ScanlineFreq;
                float  _ScanlineStrength;
                float  _RowDensity;
            CBUFFER_END

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            V2F Vert(Attr v)
            {
                V2F o;

                float t   = _Time.y * _GlitchSpeed;
                float row = floor(v.positionOS.y * _RowDensity);
                float n   = Hash(float2(row, floor(t * 3.0)));

                // 가끔(상위 15%) 튀는 가로 변위
                float kick = step(0.85, n) * (n - 0.85) * 6.6;

                v.positionOS.x += ((n - 0.5) * 0.03 + kick * 0.05) * _GlitchAmount;

                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv          = v.uv;
                return o;
            }

            float4 Frag(V2F i) : SV_Target
            {
                float t   = _Time.y * _GlitchSpeed;
                float2 uv = i.uv;

                // RGB shift — R/B 채널을 시간·UV 기반으로 미세하게 어긋나게
                float rOff = _RGBShift * sin(t       + uv.y * 30.0);
                float bOff = _RGBShift * sin(t * 1.3 + uv.y * 30.0);

                float3 col = _BaseColor.rgb;
                col.r *= 0.85 + 0.30 * sin((uv.x + rOff) * 60.0 + t);
                col.b *= 0.85 + 0.30 * sin((uv.x - bOff) * 60.0 + t * 1.3);

                // Scanlines
                float scan = sin(uv.y * _ScanlineFreq + t * 4.0) * 0.5 + 0.5;
                col *= lerp(1.0, scan, _ScanlineStrength);

                // 가끔씩 튀는 가로 노이즈 블록 (밝은 플리커)
                float row     = floor(uv.y * 50.0);
                float n       = Hash(float2(row, floor(t * 5.0)));
                float flicker = step(0.92, n) * 0.7;
                col += flicker;

                return float4(col, _BaseColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
