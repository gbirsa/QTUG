Shader "BlurPreserveAlpha"
{
    Properties
    {
        _Radius ("Blur Radius", Range(0, 5)) = 1.5
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "BlurPreserveAlpha"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            float _Radius;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 Sample(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 t = _MainTex_TexelSize.xy * _Radius;

                half4 c0 = Sample(i.uv);

                half3 rgb =
                    c0.rgb * 0.2 +
                    Sample(i.uv + float2( t.x, 0)).rgb * 0.1 +
                    Sample(i.uv + float2(-t.x, 0)).rgb * 0.1 +
                    Sample(i.uv + float2(0,  t.y)).rgb * 0.1 +
                    Sample(i.uv + float2(0, -t.y)).rgb * 0.1 +
                    Sample(i.uv + float2( t.x,  t.y)).rgb * 0.1 +
                    Sample(i.uv + float2(-t.x,  t.y)).rgb * 0.1 +
                    Sample(i.uv + float2( t.x, -t.y)).rgb * 0.1 +
                    Sample(i.uv + float2(-t.x, -t.y)).rgb * 0.1;

                // Preserve original alpha
                return half4(rgb, c0.a);
            }
            ENDHLSL
        }
    }
}
