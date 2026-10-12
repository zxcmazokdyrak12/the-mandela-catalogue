Shader "Mandela/MadDistortion"
{
    // Full-screen M.A.D. effect for the URP Full Screen Pass renderer feature.
    // Driven by two global floats set from SanitySystem:
    //   _MadIntensity 0..1  tearing, wave, colour split, grey crush, noise, scanlines
    //   _MadFlash     0..1  negative flash
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Pass
        {
            Name "MadDistortion"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _MadIntensity;
            float _MadFlash;

            float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }

            half4 Frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv0 = input.texcoord;
                float k = saturate(_MadIntensity);
                if (k < 0.001 && _MadFlash < 0.001)
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv0);
                float t = _Time.y;
                float2 uv = uv0;
                // torn horizontal bands, like a chewed tape
                float band = floor(uv.y * 48.0 + floor(t * 14.0) * 3.0);
                float on = step(1.0 - k * 0.35, hash11(band * 1.7 + floor(t * 9.0)));
                uv.x += (hash11(band + floor(t * 20.0)) - 0.5) * 0.12 * k * on;
                // slow sick wave
                uv.x += sin(uv.y * 22.0 + t * 5.0) * 0.004 * k;
                uv.y += sin(uv.x * 14.0 + t * 3.0) * 0.003 * k;
                // colour split
                float2 dir = (uv - 0.5) * 0.012 * k + float2(0.006 * k * sin(t * 30.0), 0.0);
                half r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dir).r;
                half g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g;
                half b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - dir).b;
                half3 c = half3(r, g, b);
                // crush towards hard black and white
                half l = dot(c, half3(0.299, 0.587, 0.114));
                c = lerp(c, l.xxx, k * 0.8);
                c = saturate(c * (1.0 + k * 0.5));   // lift a little so the dark scene stays readable
                // blocky noise and scanlines
                float n = hash11(dot(floor(uv0 * float2(320.0, 180.0)), float2(1.0, 57.0)) + floor(t * 24.0));
                c += (n - 0.5) * 0.22 * k * k;
                c *= 1.0 - k * 0.25 * (0.5 + 0.5 * sin(uv0.y * 900.0));
                // negative flash
                c = lerp(c, 1.0 - c, saturate(_MadFlash));
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
