// Utility shader used only via Graphics.Blit (never applied to a visible mesh).
// Draws one soft-edged circular stroke per Blit call, alpha-blended onto whatever
// is already in the destination RenderTexture - this is what makes paint accumulate
// smoothly on the canvas instead of being grid-snapped.
Shader "Hidden/PaintBrush"
{
    Properties
    {
        _PaintUV ("Paint UV", Vector) = (0.5, 0.5, 0, 0)
        _PaintRadiusUV ("Paint Radius UV (per axis)", Vector) = (0.05, 0.05, 0, 0)
        _PaintColor ("Paint Color", Color) = (1, 1, 1, 1)
        _Softness ("Edge Softness (0-1)", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

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

            float4 _PaintUV;
            float4 _PaintRadiusUV;
            float4 _PaintColor;
            float _Softness;

            // Note: this passes position straight through as clip space. Graphics.Blit's
            // internal quad is already defined in clip space, so there's no camera/MVP
            // matrix to apply here - don't reuse this vertex shader pattern for anything
            // rendered normally by a camera (see PaintableSurfaceUnlit.shader for that case).
            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = float4(IN.positionOS.xy, 0.0, 1.0);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                // Dividing by a per-axis radius (rather than one scalar radius) keeps the
                // stroke a true circle in world space even if this surface's UV mapping
                // isn't perfectly square.
                float2 delta = (IN.uv - _PaintUV.xy) / _PaintRadiusUV.xy;
                float dist = length(delta);
                float alpha = 1.0 - smoothstep(1.0 - _Softness, 1.0, dist);
                return float4(_PaintColor.rgb, _PaintColor.a * alpha);
            }
            ENDHLSL
        }
    }
}
