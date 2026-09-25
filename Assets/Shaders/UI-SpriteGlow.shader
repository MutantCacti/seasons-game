// A UI shader that draws a pulsing glow around the silhouette of a sprite, outside it only, for
// SpriteGlow. The quad is the sprite's drawn rect grown by _GlowRadius on every side, with its UVs
// carried on past the sprite's own UV rect, so the shape comes from sampling the sprite's alpha at
// offsets around each pixel (a dilation for the core, a ring average for the halo). Vertex positions
// are no use here: the canvas batches UI meshes into canvas space, and the book's leaves rotate.
// Samples outside _UVRect count as empty, so atlas neighbours and clamped edge pixels never leak in.
// Based on UI/Default so masks keep working. The colour is the vertex colour (Graphic.color).
Shader "UI/SpriteGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _MaxWidth ("Max Width", Float) = 6
        _PulseSpeed ("Pulse Speed", Float) = 8
        _GlowRadius ("Glow Radius", Float) = 24
        _GlowIntensity ("Glow Intensity", Float) = 0.8
        _UVRect ("UV Rect (min xy, max zw)", Vector) = (0, 0, 1, 1)
        _UVPerUnit ("UV Per Rect Unit", Vector) = (0.01, 0.01, 0, 0)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            // 72 taps a pixel. Plenty for a handful of buttons; the rings are what make the edge
            // of the core a little stepped at large radii.
            #define GLOW_RINGS 6
            #define GLOW_DIRECTIONS 12

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float _MaxWidth;
            float _PulseSpeed;
            float _GlowRadius;
            float _GlowIntensity;
            float4 _UVRect;
            float4 _UVPerUnit;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);

                OUT.texcoord = v.texcoord;

                OUT.color = v.color;
                return OUT;
            }

            // zero outside the sprite's own UV rect, so an atlas neighbour or a clamped edge
            // pixel never reads as part of the silhouette
            float SampleAlpha(float2 uv, float2 dx, float2 dy)
            {
                float2 inside = step(_UVRect.xy, uv) * step(uv, _UVRect.zw);
                return tex2Dgrad(_MainTex, uv, dx, dy).a * inside.x * inside.y;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // one set of gradients for every tap: the offsets are constant across the quad,
                // so the base pixel's footprint is the right one for all of them
                float2 dx = ddx(IN.texcoord);
                float2 dy = ddy(IN.texcoord);

                float centre = SampleAlpha(IN.texcoord, dx, dy);

                float pulse = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed);
                float width = _MaxWidth * pulse;
                float ringStep = max(_GlowRadius / GLOW_RINGS, 1e-4);

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
                        float a = SampleAlpha(IN.texcoord + offset, dx, dy);

                        ringMax = max(ringMax, a);
                        ringSum += a;
                    }

                    // the core is a dilation out to the pulsing width; the ring it falls inside fades in
                    core = max(core, ringMax * saturate((width - r) / ringStep + 1.0));

                    // the halo is a blur: right on a straight edge every ring is about half covered,
                    // so it normalises to 1 there and falls to 0 at the quad's edge
                    float weight = 1.0 - (float)(j - 1) / GLOW_RINGS;
                    halo += ringSum / GLOW_DIRECTIONS * weight;
                    haloNorm += 0.5 * weight;
                }

                halo = saturate(halo / haloNorm);
                halo *= halo;
                halo *= _GlowIntensity * (0.6 + 0.4 * pulse);

                float alpha = max(core, halo);

                // the core leans toward white, so it reads as light rather than paint
                float3 coreCol = lerp(IN.color.rgb, float3(1.0, 1.0, 1.0), 0.35);
                float3 col = lerp(IN.color.rgb, coreCol, core / max(alpha, 1e-4));

                // outside the silhouette only; the sprite itself stays as drawn
                alpha *= 1.0 - centre;

                fixed4 color = fixed4(col, alpha * IN.color.a);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
