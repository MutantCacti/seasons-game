// One pulsing outline around the whole compendium book, however its parts are posed. Drawn by two
// Render Objects features (after the skybox, so the sky cannot paint over it) that re-draw the book's
// layer with this as the override material:
//   pass 0 marks every pixel the book covers in stencil bit 128
//   pass 1 draws each part again, grown outward, only where that bit is not set
// so the parts' grown hulls merge into one silhouette band instead of an outline per part.
// Colour, width and pulse are globals set by BookGlow, so the material asset never changes at runtime.
Shader "Seasons/BookGlowOutline"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // set by BookGlow
        float4 _BookGlowColor;
        float _BookGlowPulseSpeed;
        float4 _BookGlowWidth;  // x min, y max, in screen pixels
        float4 _BookGlowAlpha;  // x min, y max

        // 0..1 on the same clock as UI-PulsingOutline, so the book and the page outlines pulse together
        float BookGlowPulse()
        {
            return 0.5 + 0.5 * sin(_Time.y * _BookGlowPulseSpeed);
        }
        ENDHLSL

        Pass
        {
            Name "BookGlowMask"

            // ZTest Always rather than LEqual: this re-draws the book with a different vertex shader from
            // the Lit pass that wrote its depth, so tiny position differences would fail LEqual on some
            // pixels, leave them out of the mask, and speckle the hull across the book's own faces. The
            // book always floats in front of the world, so an unconditional mask costs nothing visible.
            ColorMask 0
            ZWrite Off
            ZTest Always
            Cull Off    // the pages are two-sided quads

            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "BookGlowHull"

            // LEqual so world geometry in front still hides the outline
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            // Replace on pass as well as testing, so where two parts' hulls overlap only the first one
            // blends and the band stays one even colour
            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 outward : TEXCOORD3;     // baked by BookOutlinePostprocessor: -1, 0 or 1 per axis
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // Each object axis in world space, normalised, so the push is the same width on every
                // face whatever the part's scale
                float4x4 objectToWorld = GetObjectToWorldMatrix();
                float3 axisX = normalize(objectToWorld._m00_m10_m20);
                float3 axisY = normalize(objectToWorld._m01_m11_m21);
                float3 axisZ = normalize(objectToWorld._m02_m12_m22);
                float3 outwardWS = input.outward.x * axisX + input.outward.y * axisY + input.outward.z * axisZ;

                // World size of one screen pixel at this vertex: the view is 2 * w / P11 tall at clip w
                // (for ortho w is 1 and 2 / P11 is the ortho height, so the same line covers both).
                // abs because URP can flip the projection when rendering to a texture.
                float clipW = TransformWorldToHClip(positionWS).w;
                float pixelWS = 2.0 * clipW / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);

                float widthPixels = lerp(_BookGlowWidth.x, _BookGlowWidth.y, BookGlowPulse());

                positionWS += outwardWS * widthPixels * pixelWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float alpha = lerp(_BookGlowAlpha.x, _BookGlowAlpha.y, BookGlowPulse());
                return half4(_BookGlowColor.rgb, alpha * _BookGlowColor.a);
            }
            ENDHLSL
        }
    }
}
