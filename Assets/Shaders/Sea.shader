// The sea sheet baked by TerrainBaker ("Sea Level"). One shader, two looks picked per material with
// the Style dropdown, so swapping looks is dragging a different material onto the renderer:
//   Tidepool  flat toon bands by water depth, breathing contact foam, twinkling glints
//   Glass     clear shallows: the seafloor refracted through a whole-texel wobble, a stepped tint by
//             depth, a sky-coloured sheen, and the same contact foam
// Everything snaps to the terrain's 1/32 world grid so the water steps like the pixel art. Night,
// the sky and the skirt's floor are globals pushed by SeaLevel, so no material needs wiring.
//
// The skirt: the mesh always has quads around its edge (uv1.x = 1, bottom edge uv1.y = 1). With Skirt
// off every skirt vertex sits on the sheet's edge, so the quads have no area and draw nothing; with it
// on the bottom edge drops to the void (_SeaFloorY) and the world reads as a cut-away diorama.
// Skirt sorting comes from the mesh: skirts are first in the index buffer, and blending follows
// primitive order within a draw, so the sheet always blends over the skirt behind it.
// The skirt lies exactly on the terrain's outer walls, so the pass is depth offset towards the camera
// (the ShaderLab Offset docs' decal case) to win that tie instead of z-fighting it.
Shader "Seasons/Sea"
{
    Properties
    {
        [KeywordEnum(Tidepool, Glass)] _Style ("Style", Float) = 0
        [ToggleUI] _Skirt ("Skirt", Float) = 0

        [Header(Day)]
        _ShallowColour ("Shallow", Color) = (0.349, 0.710, 0.671, 1)        // #59B5AB
        _MidColour ("Mid", Color) = (0.404, 0.718, 0.886, 1)                // #67B7E2
        _DeepColour ("Deep", Color) = (0.235, 0.537, 0.722, 1)              // #3C89B8
        _DeeperColour ("Deeper", Color) = (0.149, 0.424, 0.620, 1)          // #266C9E
        _FoamColour ("Foam", Color) = (0.863, 0.918, 0.941, 1)              // #DCEAF0
        _Opacity ("Opacity", Range(0, 1)) = 0.92

        [Header(Night)]
        _NightTint ("Night Tint", Color) = (0.235, 0.255, 0.439, 1)         // #3C4170, the clock's night sky
        _NightStrength ("Night Strength", Range(0, 1)) = 0.55
        _FoamNightColour ("Foam Night", Color) = (0.600, 0.643, 0.733, 1)
        _GlintNightColour ("Glint Night", Color) = (0.929, 0.769, 0.231, 1) // #EDC43B

        [Header(Pixel Grid)]
        _PixelsPerUnit ("Pixels Per Unit", Float) = 32

        [Header(Depth Bands)]
        _BandDepths ("Band Depths xyz", Vector) = (0.15, 0.4, 0.8, 0)

        [Header(Contact Foam)]
        _FoamWidth ("Foam Width", Float) = 0.06
        _FoamBreathe ("Foam Breathe", Float) = 0.02
        _FoamBreatheSpeed ("Foam Breathe Speed", Float) = 1.2

        [Header(Glints)]
        _GlintDensity ("Glint Density", Range(0, 0.05)) = 0.008
        _GlintSpeed ("Glint Speed", Float) = 0.6

        [Header(Glass)]
        _RefractionPixels ("Refraction Pixels", Float) = 1.5
        _WobbleFrequency ("Wobble Frequency", Float) = 4
        _WobbleSpeed ("Wobble Speed", Float) = 1
        _Absorption ("Absorption", Float) = 3
        _AbsorptionSteps ("Absorption Steps", Float) = 4
        _TintBase ("Tint Base", Range(0, 1)) = 0.25
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 1
        _FresnelPower ("Fresnel Power", Float) = 2

        [Header(Skirt)]
        _SkirtBandHeight ("Skirt Band Height", Float) = 1.1
        _SkirtLip ("Skirt Lip", Float) = 0.07
        _SkirtOpacity ("Skirt Opacity", Range(0, 1)) = 0.6
        _DepthOffsetFactor ("Depth Offset Factor", Float) = -1
        _DepthOffsetUnits ("Depth Offset Units", Float) = -1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float _Skirt;
        half4 _ShallowColour;
        half4 _MidColour;
        half4 _DeepColour;
        half4 _DeeperColour;
        half4 _FoamColour;
        half _Opacity;
        half4 _NightTint;
        half _NightStrength;
        half4 _FoamNightColour;
        half4 _GlintNightColour;
        float _PixelsPerUnit;
        float4 _BandDepths;
        float _FoamWidth;
        float _FoamBreathe;
        float _FoamBreatheSpeed;
        float _GlintDensity;
        float _GlintSpeed;
        float _RefractionPixels;
        float _WobbleFrequency;
        float _WobbleSpeed;
        float _Absorption;
        float _AbsorptionSteps;
        half _TintBase;
        half _FresnelStrength;
        float _FresnelPower;
        float _SkirtBandHeight;
        float _SkirtLip;
        half _SkirtOpacity;
        CBUFFER_END

        // set by SeaLevel
        float _SeaDaylight;     // 0 night .. 1 noon
        float4 _SeaSkyColour;   // the camera's clear colour, as drawn
        float _SeaFloorY;       // world y the skirt drops to
        ENDHLSL

        Pass
        {
            Name "Sea"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset [_DepthOffsetFactor], [_DepthOffsetUnits]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // shader_feature keeps a variant for every material the build includes, and a style
            // is picked per material, so each dropped-in material ships its own. Tidepool is the
            // no-keyword variant, so a fresh material that was never inspected still draws
            #pragma shader_feature_local_fragment _ _STYLE_GLASS

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float surfaceY : TEXCOORD1;
                float skirt : TEXCOORD2;
            };


            // MARK: shared helpers

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // the centre of the terrain texel this point is in, so every effect is flat per texel
            float3 SnapToGrid(float3 positionWS)
            {
                positionWS.xz = (floor(positionWS.xz * _PixelsPerUnit) + 0.5) / _PixelsPerUnit;
                return positionWS;
            }

            // the inverse of ComputeWorldSpacePosition below, so a snapped point samples the scene
            // textures at the right place
            float2 ScreenUVOf(float3 positionWS)
            {
                return ComputeNormalizedDeviceCoordinates(positionWS, UNITY_MATRIX_VP);
            }

            // world y of whatever opaque thing is behind the water at this screen uv, following
            // the URP docs' reconstruct-world-position recipe
            float SceneWorldY(float2 uv)
            {
            #if UNITY_REVERSED_Z
                float depth = SampleSceneDepth(uv);
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
            #endif
                return ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP).y;
            }

            half Night()
            {
                return 1.0 - saturate(_SeaDaylight);
            }

            half3 ApplyNight(half3 colour)
            {
                return lerp(colour, _NightTint.rgb, Night() * _NightStrength);
            }

            half3 FoamNow()
            {
                return lerp(_FoamColour.rgb, _FoamNightColour.rgb, Night());
            }

            // vertical depth, so it means the same from every camera angle
            float WaterDepthAt(float3 snappedWS)
            {
                return max(0.0, snappedWS.y - SceneWorldY(ScreenUVOf(snappedWS)));
            }

            bool InContactFoam(float waterDepth)
            {
                return waterDepth < _FoamWidth + _FoamBreathe * sin(_Time.y * _FoamBreatheSpeed);
            }

            half3 DepthBandColour(float waterDepth)
            {
                if (waterDepth < _BandDepths.x) return _ShallowColour.rgb;
                if (waterDepth < _BandDepths.y) return _MidColour.rgb;
                if (waterDepth < _BandDepths.z) return _DeepColour.rgb;
                return _DeeperColour.rgb;
            }

            // a sparse scatter of texels that wink on and off, like the white dots in the water tiles.
            // They go gold at night, as if catching lantern light
            half3 ApplyGlints(half3 colour, float3 snappedWS)
            {
                float2 cell = floor(snappedWS.xz * _PixelsPerUnit);
                float pick = Hash21(cell);
                if (pick >= _GlintDensity) return colour;

                float twinkle = frac(Hash21(cell + 17.0) + _Time.y * _GlintSpeed);
                if (twinkle > 0.3) return colour;

                return lerp(_FoamColour.rgb, _GlintNightColour.rgb, Night());
            }


            // MARK: styles

            half4 Tidepool(float3 snappedWS)
            {
                float waterDepth = WaterDepthAt(snappedWS);

                half3 colour = ApplyNight(DepthBandColour(waterDepth));
                colour = ApplyGlints(colour, snappedWS);
                if (InContactFoam(waterDepth)) colour = FoamNow();

                return half4(colour, _Opacity);
            }


            // opaque over the camera's opaque copy: the water composites the floor itself
            half4 Glass(float3 snappedWS)
            {
                float2 straightUV = ScreenUVOf(snappedWS);
                float waterDepth = max(0.0, snappedWS.y - SceneWorldY(straightUV));

                // the wobble moves the sample by whole terrain texels, so the floor shifts like
                // pixel art rather than smearing
                float time = _Time.y * _WobbleSpeed;
                float2 wobble = float2(sin(snappedWS.z * _WobbleFrequency + time),
                                       cos(snappedWS.x * _WobbleFrequency + time * 0.8));
                float3 refractedWS = snappedWS;
                refractedWS.xz += round(wobble * _RefractionPixels) / _PixelsPerUnit;

                // a sample that lands on something standing out of the water (a shore, an item) would
                // drag it into the water, so fall back to looking straight down
                float2 uv = ScreenUVOf(refractedWS);
                float floorY = SceneWorldY(uv);
                if (floorY > snappedWS.y)
                {
                    uv = straightUV;
                    floorY = snappedWS.y - waterDepth;
                }

                float absorb = 1.0 - exp(-(snappedWS.y - floorY) * _Absorption);
                absorb = floor(absorb * _AbsorptionSteps) / _AbsorptionSteps;

                // the floor is already lit (and darkened at night) by the scene, so only the tint
                // takes the night tint
                half3 tint = ApplyNight(lerp(_ShallowColour.rgb, _MidColour.rgb, absorb));
                half3 colour = lerp(SampleSceneColor(uv), tint, _TintBase + absorb * (1.0 - _TintBase));

                // the sky is a flat clear colour, so reflecting it is just that colour
                half facing = saturate(GetWorldSpaceNormalizeViewDir(snappedWS).y);
                colour = lerp(colour, _SeaSkyColour.rgb, _FresnelStrength * pow(1.0 - facing, _FresnelPower));

                colour = ApplyGlints(colour, snappedWS);
                if (InContactFoam(waterDepth)) colour = FoamNow();

                return half4(colour, 1.0);
            }


            // MARK: skirt

            // the side of the water slab: bands getting deeper every _SkirtBandHeight, with a foam lip
            // where it meets the sheet. The same in every style, tinted by the style's colours
            half4 Skirt(float3 positionWS, float surfaceY)
            {
                float depth = surfaceY - positionWS.y;
                depth = floor(depth * _PixelsPerUnit) / _PixelsPerUnit;

                int band = min(3, (int)(depth / _SkirtBandHeight));
                half3 colour = band == 0 ? _ShallowColour.rgb
                    : band == 1 ? _MidColour.rgb
                    : band == 2 ? _DeepColour.rgb
                    : _DeeperColour.rgb;
                half alpha = saturate(_SkirtOpacity + 0.1 * band);

                colour = ApplyNight(colour);
                if (depth < _SkirtLip)
                {
                    colour = FoamNow();
                    alpha = 1.0;
                }

                return half4(colour, alpha);
            }


            // MARK: passes

            Varyings vert(Attributes input)
            {
                Varyings output;

                // the sheet and the top of the skirt are at local y = 0. The skirt's bottom edge is
                // baked below it, but only goes there when the skirt is on, and then all the way to
                // the void; with it off the skirt collapses onto the sheet's edge
                float3 positionOS = input.positionOS.xyz;
                positionOS.y = 0.0;
                float3 positionWS = TransformObjectToWorld(positionOS);

                if (input.uv1.y > 0.5 && _Skirt > 0.5) positionWS.y = _SeaFloorY;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.surfaceY = TransformObjectToWorld(float3(0.0, 0.0, 0.0)).y;
                output.skirt = input.uv1.x;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (input.skirt > 0.5) return Skirt(input.positionWS, input.surfaceY);

                float3 snappedWS = SnapToGrid(input.positionWS);
                snappedWS.y = input.surfaceY;

            #if defined(_STYLE_GLASS)
                return Glass(snappedWS);
            #else
                return Tidepool(snappedWS);
            #endif
            }
            ENDHLSL
        }
    }
}
