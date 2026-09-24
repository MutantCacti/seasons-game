// A UI shader that draws a rectangular outline just inside an Image's rect, with its
// pixel width pulsing in and out over time. Based on UI/Default so masks keep working.
// Distance to the edge comes from the UVs scaled by _RectSize, so the Image must be Simple
// with no sprite (UnseenHighlight enforces that). Vertex positions are no use here: the
// canvas batches UI meshes into canvas space, and the book's leaves rotate and mirror.
// A soft glow halo falls inward from the core band so the highlight reads as breathing
// light rather than a painted border.
Shader "UI/PulsingOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (1, 0.76, 0.23, 1)
        _MaxWidth ("Max Width", Float) = 6
        _PulseSpeed ("Pulse Speed", Float) = 5
        _GlowWidth ("Glow Width", Float) = 14
        _GlowIntensity ("Glow Intensity", Float) = 0.8
        _RectSize ("Rect Size", Vector) = (580, 740, 0, 0)

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

            fixed4 _OutlineColor;
            float _MaxWidth;
            float _PulseSpeed;
            float _GlowWidth;
            float _GlowIntensity;
            float4 _RectSize;
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

            fixed4 frag(v2f IN) : SV_Target
            {
                // UVs run 0..1 across a Simple, spriteless Image, so this is rect units to the nearest edge
                float2 edge = min(IN.texcoord, 1.0 - IN.texcoord) * _RectSize.xy;
                float dist = min(edge.x, edge.y);

                float pulse = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed);
                float width = _MaxWidth * pulse;
                width = max(width, 0.0);

                float aa = fwidth(dist);
                float core = saturate((width - dist) / max(aa, 1e-4));

                float glowDist = max(dist - width, 0.0);
                float glow = _GlowIntensity * exp(-glowDist / _GlowWidth);
                glow *= 0.6 + 0.4 * pulse;
                glow = saturate(glow);

                float alpha = max(core, glow);

                // The core brightens toward the rect edge, so it reads as light, not paint.
                float corePos = 1.0 - saturate(dist / max(width, 1e-4));
                float3 coreCol = lerp(_OutlineColor.rgb, float3(1.0, 1.0, 1.0), corePos * 0.6);

                float3 col = lerp(_OutlineColor.rgb, coreCol, core / max(alpha, 1e-4));

                fixed4 color = fixed4(col * IN.color.rgb, alpha * IN.color.a);

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
