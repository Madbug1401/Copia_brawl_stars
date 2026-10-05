Shader "FPSLAN/ToonOutline"
{
    Properties
    {
        _Color ("Cor", Color) = (1,1,1,1)
        _MainTex ("Textura", 2D) = "white" {}
        _ShadeColor ("Tom da sombra", Color) = (0.62,0.62,0.85,1)
        _RimColor ("Rim (a = força)", Color) = (1,1,1,0.3)
        _Emission ("Emissão", Color) = (0,0,0,0)
        _Checker ("Mosaico", Range(0,1)) = 0
        _CheckerScale ("Tamanho do mosaico", Float) = 2
        _Noise ("Variação", Range(0,1)) = 0
        _TopLight ("Topo mais claro", Range(0,1)) = 0
        _Flash ("Flash de dano", Range(0,1)) = 0
        _Fade ("Opacidade (dither)", Range(0,1)) = 1
        _OutlineWidth ("Contorno", Float) = 0.0045
        _OutlineColor ("Cor do contorno", Color) = (0.08,0.06,0.12,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "OUTLINE"
            Cull Front
            CGPROGRAM
            #pragma vertex outline_vert
            #pragma fragment outline_frag
            #pragma multi_compile_fog
            #pragma target 3.0
            #define FPSLAN_OUTLINE_ONLY
            #include "FPSLAN_Toon.cginc"
            ENDCG
        }

        UsePass "FPSLAN/Toon/FORWARD"
        UsePass "FPSLAN/Toon/FORWARDADD"
        UsePass "FPSLAN/Toon/SHADOWCASTER"
    }
    Fallback "Diffuse"
}
