// The highlight glow around a world sprite: UI/SpriteGlow's silhouette glow, ported to a world quad for
// AnimalGlow. The quad is the sprite's rect grown by _GlowRadius on every side, with its UVs carried on
// past the sprite's own UV rect, so the shape comes from sampling the sprite's alpha at offsets around
// each pixel (a dilation for the core, a ring average for the halo). It follows the drawn pixels, not
// the rect: samples outside _UVRect count as empty, and anywhere with no opaque pixel nearby is clear.
//
// Everything is in the sprite's local units (_GlowRadius, _MaxWidth, _UVPerUnit), so it scales with it.
//
// While the animal is hidden (_Hidden 1, a burrowed worm) the silhouette itself glows too, so it reads
// as the animal's shape rather than an empty outline, and AnimalGlow sets _ZTest to Always so it shows
// through the ground. _Flare (1 fading to 0) is the burst when the highlight is first switched on: the
// core runs out wider and brighter from the outline and settles.
//
// URP: an unlit transparent pass tagged UniversalForward, as Seasons/Sea is.
Shader "Seasons/SpriteGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Colour", Color) = (0.941, 0.804, 0.431, 1)     // #F0CD6E, the star's warm gold
        _MaxWidth ("Max Width", Float) = 0.04
        _PulseSpeed ("Pulse Speed", Float) = 8
        _GlowRadius ("Glow Radius", Float) = 0.15
        _GlowIntensity ("Glow Intensity", Float) = 0.8
        _UVRect ("UV Rect (min xy, max zw)", Vector) = (0, 0, 1, 1)
        _UVPerUnit ("UV Per Unit", Vector) = (1, 1, 0, 0)
        _Hidden ("Hidden", Float) = 0
        _HiddenFill ("Hidden Fill", Range(0, 1)) = 0.55
        _Flare ("Flare", Float) = 0
        _FlareWidth ("Flare Width", Float) = 0.1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent+1"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Name "SpriteGlow"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 72 taps a pixel, as UI/SpriteGlow. Only a highlighted animal's quad pays it
            #define GLOW_RINGS 6
            #define GLOW_DIRECTIONS 12

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _MaxWidth;
            float _PulseSpeed;
            float _GlowRadius;
            float _GlowIntensity;
            float4 _UVRect;
            float4 _UVPerUnit;
            float _Hidden;
            half _HiddenFill;
            float _Flare;
            float _FlareWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };


            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }


            // zero outside the sprite's own UV rect, so an atlas neighbour or a clamped edge pixel never
            // reads as part of the silhouette
            float SampleAlpha(float2 uv, float2 dx, float2 dy)
            {
                float2 inside = step(_UVRect.xy, uv) * step(uv, _UVRect.zw);
                return SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, uv, dx, dy).a * inside.x * inside.y;
            }


            half4 frag(Varyings input) : SV_Target
            {
                // one set of gradients for every tap: the offsets are constant across the quad
                float2 dx = ddx(input.uv);
                float2 dy = ddy(input.uv);

                float centre = SampleAlpha(input.uv, dx, dy);

                float pulse = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed);
                float flare = saturate(_Flare);
                float width = _MaxWidth * pulse + _FlareWidth * flare;
                float ringStep = max(_GlowRadius / GLOW_RINGS, 1e-5);

                float core = 0.0;
                float halo = 0.0;
                float haloNorm = 0.0;

                [unroll]
                for (int j = 1; j <= GLOW_RINGS; j++)
                {
                    float r = ringStep * j;
                    float ringMax = 0.0;
                    float ringSum = 0.0;

                    // odd rings sit half a spoke round, so thin bits between spokes still get caught
                    float twist = (j % 2) * 0.5;

                    [unroll]
                    for (int k = 0; k < GLOW_DIRECTIONS; k++)
                    {
                        float angle = (k + twist) * (6.2831853 / GLOW_DIRECTIONS);
                        float2 offset = float2(cos(angle), sin(angle)) * r * _UVPerUnit.xy;
                        float a = SampleAlpha(input.uv + offset, dx, dy);

                        ringMax = max(ringMax, a);
                        ringSum += a;
                    }

                    // the core is a dilation out to the pulsing width; the ring it falls inside fades in
                    core = max(core, ringMax * saturate((width - r) / ringStep + 1.0));

                    // the halo is a blur: right on a straight edge every ring is about half covered, so
                    // it normalises to 1 there and falls to 0 at the quad's edge
                    float weight = 1.0 - (float)(j - 1) / GLOW_RINGS;
                    halo += ringSum / GLOW_DIRECTIONS * weight;
                    haloNorm += 0.5 * weight;
                }

                halo = saturate(halo / haloNorm);
                halo *= halo;
                halo *= _GlowIntensity * (0.6 + 0.4 * pulse) * (1.0 + flare);

                float alpha = saturate(max(core, halo));

                // the core leans toward white, so it reads as light rather than paint
                half3 coreCol = lerp(_Color.rgb, half3(1.0, 1.0, 1.0), 0.35 + 0.4 * flare);
                half3 col = lerp(_Color.rgb, coreCol, core / max(alpha, 1e-4));

                if (_Hidden > 0.5)
                {
                    // hidden, the animal's own shape glows too, so it reads as a worm under the ground
                    alpha = max(alpha, centre * _HiddenFill * (0.7 + 0.3 * pulse));
                }
                else
                {
                    // shown, outside the silhouette only; the sprite itself stays as drawn
                    alpha *= 1.0 - centre;
                }

                return half4(col, alpha * _Color.a);
            }
            ENDHLSL
        }
    }
}
