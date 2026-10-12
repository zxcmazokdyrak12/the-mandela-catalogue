Shader "Mandela/BeamCone"
{
    // Fake volumetric flashlight beam: additive cone, brightest along the axis, fades with distance,
    // soft where it touches geometry (needs the camera depth texture) and stirred by slow dust noise.
    Properties
    {
        _Color ("Color", Color) = (1, 0.93, 0.8, 1)
        _Intensity ("Intensity", Float) = 0.1
        _SoftDepth ("Soft depth", Float) = 0.5
        _NoiseScale ("Dust scale", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _SoftDepth;
                float _NoiseScale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
            };

            Varyings vert (Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                o.screenPos = ComputeScreenPos(p.positionCS);
                return o;
            }

            float hash (float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vnoise (float3 x)
            {
                float3 i = floor(x); float3 f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(i), hash(i + float3(1,0,0)), f.x), lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), f.x), lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            half4 frag (Varyings i) : SV_Target
            {
                float along = saturate(i.uv.y);
                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
                // seen from the side the shaft is brightest where we look through its core; seen along the axis
                // (the usual first-person case) every pixel looks through the volume, so use an even glow
                float3 axisWS = normalize(TransformObjectToWorldDir(float3(0, 0, 1)));
                float alongView = abs(dot(axisWS, viewDir));
                float sideFacing = pow(abs(dot(normalize(i.normalWS), viewDir)), 1.5);
                float edge = smoothstep(0.0, 0.55, abs(dot(normalize(i.normalWS), viewDir)));   // hide the silhouette rim
                float facing = lerp(sideFacing, 0.4 * edge, smoothstep(0.6, 0.95, alongView));
                float fall = pow(1.0 - along, 3.0) * smoothstep(0.0, 0.05, along);
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float selfDepth = -TransformWorldToView(i.positionWS).z;
                float soft = saturate((sceneDepth - selfDepth) / _SoftDepth);
                float camFade = saturate((selfDepth - 0.25) / 0.8);
                float dust = 0.6 + 0.8 * vnoise(i.positionWS * _NoiseScale + float3(0, _Time.y * 0.12, _Time.y * 0.05));
                float a = _Intensity * facing * fall * soft * camFade * dust;
                return half4(_Color.rgb * a, 0);
            }
            ENDHLSL
        }
    }
}
