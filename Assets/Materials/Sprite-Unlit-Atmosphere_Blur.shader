Shader "Universal Render Pipeline/2D/Sprite-Unlit-HazeBlur"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0

        _Desaturate ("Desaturate", Range(0,1)) = 0
        _HazeColor ("Haze Color", Color) = (0.75,0.85,1,1)
        _Haze ("Haze Amount", Range(0,1)) = 0
        _HazeCurve ("Haze Curve", Range(0.5, 4)) = 2.0

        _Blur ("Blur", Range(0, 3)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            // Works with URP (and 2D renderer) as an unlit transparent pass
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Desaturate;
                half4 _HazeColor;
                float _Haze;
                float _HazeCurve;
                float _Blur;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            half3 DesaturateFn(half3 rgb, half amount)
            {
                half luma = dot(rgb, half3(0.2126h, 0.7152h, 0.0722h));
                return lerp(rgb, half3(luma, luma, luma), amount);
            }

            // 5-tap blur that preserves alpha from the center sample.
            half4 SampleBlur5(float2 uv, float blurPx)
            {
                // blurPx is in "pixels" of the texture; convert to UV offsets
                float2 t = _MainTex_TexelSize.xy * blurPx;

                half4 c0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half3 rgb =
                    c0.rgb * 0.5h +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( t.x, 0)).rgb * 0.125h +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-t.x, 0)).rgb * 0.125h +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0,  t.y)).rgb * 0.125h +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, -t.y)).rgb * 0.125h;

                return half4(rgb, c0.a); // preserve alpha from center to avoid halos
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // Blur amount: 0 = off, up to ~3 pixels
                float blurPx = max(_Blur, 0.0);

                half4 tex = (blurPx > 0.0001)
                    ? SampleBlur5(i.uv, blurPx)
                    : SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // Multiply by vertex/material color (like normal sprite tinting)
                half4 outCol = tex * i.color;

                // Apply haze/desaturation to RGB only, preserve alpha
                half3 rgb = outCol.rgb;

                // 1) desaturate
                half des = saturate((half)_Desaturate);
                rgb = DesaturateFn(rgb, des);

                // 2) haze toward color with nonlinear curve (controllable)
                half h = saturate((half)_Haze);
                h = pow(h, (half)max(_HazeCurve, 0.001));
                rgb = lerp(rgb, _HazeColor.rgb, h);

                outCol.rgb = saturate(rgb);
                return outCol;
            }
            ENDHLSL
        }
    }
}
