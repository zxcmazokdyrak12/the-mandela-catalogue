Shader "Mandela/PSXPost"
{
    // Full-screen PS1 look: chunky virtual pixels, 15-bit-ish colour with ordered (Bayer) dithering.
    // Globals from PSXMode: _PSXOn, _PSXRes (virtual lines), _PSXLevels (levels per channel), _PSXDither.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Pass
        {
            Name "PSXPost"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _PSXOn;
            float _PSXRes;
            float _PSXLevels;
            float _PSXDither;

            static const float kBayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            half4 Frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                if (_PSXOn < 0.5)
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                float2 vres = float2(_PSXRes * (_ScreenParams.x / _ScreenParams.y), _PSXRes);
                float2 pix = floor(uv * vres);
                float2 suv = (pix + 0.5) / vres;
                half3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, suv).rgb;
                // quantise in gamma space so the dark end keeps its steps
                float3 g = LinearToSRGB(saturate(c));
                int ix = (int)fmod(pix.x, 4.0);
                int iy = (int)fmod(pix.y, 4.0);
                float d = (kBayer[ix + iy * 4] + 0.5) / 16.0 - 0.5;
                float L = max(2.0, _PSXLevels - 1.0);
                g = floor(g * L + 0.5 + d * _PSXDither) / L;
                c = SRGBToLinear(saturate(g));
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
