// Applied directly to your floor/wall materials. Blends the surface's base texture
// with the per-surface paint RenderTexture, using the paint texture's alpha as the
// blend factor - wherever nothing's been painted, alpha is 0 and the base shows through.
// Unlit for now (no lighting response) to keep this simple before your report deadline -
// straightforward to port into a Lit shader or Shader Graph later.
Shader "Custom/PaintableSurfaceUnlit"
{
    Properties
    {
        _BaseMap ("Base Texture", 2D) = "white" {}
        _PaintMap ("Paint Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_PaintMap); SAMPLER(sampler_PaintMap);

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float4 paint = SAMPLE_TEXTURE2D(_PaintMap, sampler_PaintMap, IN.uv);
                float3 finalColor = lerp(baseColor.rgb, paint.rgb, paint.a);
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
