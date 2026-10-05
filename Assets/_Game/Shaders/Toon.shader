Shader "FPSLAN/Toon"
{
    Properties
    {
        _Color ("Cor", Color) = (1,1,1,1)
        _MainTex ("Textura", 2D) = "white" {}
        _ShadeColor ("Tom da sombra", Color) = (0.62,0.62,0.85,1)
        _RimColor ("Rim (a = força)", Color) = (1,1,1,0.25)
        _Emission ("Emissão", Color) = (0,0,0,0)
        _Checker ("Mosaico", Range(0,1)) = 0
        _CheckerScale ("Tamanho do mosaico", Float) = 2
        _Noise ("Variação", Range(0,1)) = 0
        _VertexColor ("Usar cor dos vértices", Float) = 0
        _TopLight ("Topo mais claro", Range(0,1)) = 0
        _Flash ("Flash de dano", Range(0,1)) = 0
        _Fade ("Opacidade (dither)", Range(0,1)) = 1
        _OutlineWidth ("Contorno", Float) = 0
        _OutlineColor ("Cor do contorno", Color) = (0.08,0.06,0.12,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex toon_vert
            #pragma fragment toon_frag_base
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "FPSLAN_Toon.cginc"
            ENDCG
        }

        Pass
        {
            Name "FORWARDADD"
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off
            CGPROGRAM
            #pragma vertex toon_vert
            #pragma fragment toon_frag_add
            #pragma multi_compile_fwdadd
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "FPSLAN_Toon.cginc"
            ENDCG
        }

        Pass
        {
            Name "SHADOWCASTER"
            Tags { "LightMode"="ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert_shadow
            #pragma fragment frag_shadow
            #pragma multi_compile_shadowcaster
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct v2f_shadow { V2F_SHADOW_CASTER; };
            v2f_shadow vert_shadow(appdata_base v)
            {
                v2f_shadow o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }
            float4 frag_shadow(v2f_shadow i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
