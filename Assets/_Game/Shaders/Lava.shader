Shader "FPSLAN/Lava"
{
    // Lava do mapa do Fogo (Built-in RP). Duas texturas em coordenadas do mundo (X/Z), por isso o fluxo é contínuo
    // entre peças: a crosta negra (placas, alfa = cobertura) desliza devagar; o calor (faixas alongadas) corre mais
    // depressa e abre canais na crosta. Fissuras vermelhas → fluxo laranja → núcleos amarelos.
    // A emissão é limitada (cores ≤ 1, sem HDR) para não lavar o ecrã; um pulso lento dá a sensação de respiração.
    Properties
    {
        _CrustTex ("Crosta (RGB rocha, A cobertura)", 2D) = "white" {}
        _HeatTex ("Calor (R)", 2D) = "gray" {}
        _CrustColor ("Cor da crosta", Color) = (0.55,0.5,0.5,1)
        _CrackColor ("Fissuras", Color) = (0.62,0.06,0.02,1)
        _HotColor ("Fluxo", Color) = (1,0.42,0.05,1)
        _CoreColor ("Núcleo", Color) = (1,0.86,0.3,1)
        _Tiling ("Escala (por metro)", Float) = 0.22
        _FlowDir ("Direção do fluxo (XZ)", Vector) = (1,0.25,0,0)
        _FlowSpeed ("Velocidade do calor", Float) = 0.035
        _CrustSpeed ("Velocidade da crosta", Float) = 0.012
        _Emission ("Intensidade", Range(0,2)) = 1.05
        _Pulse ("Pulso", Range(0,0.5)) = 0.12
        _CrustAmount ("Quantidade de crosta", Range(-0.5,0.5)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _CrustTex, _HeatTex;
            float4 _CrustTex_TexelSize;
            float4 _CrustColor, _CrackColor, _HotColor, _CoreColor, _FlowDir;
            float _Tiling, _FlowSpeed, _CrustSpeed, _Emission, _Pulse, _CrustAmount;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 flow = normalize(_FlowDir.xy + 1e-4);
                float2 side = float2(-flow.y, flow.x);
                float2 uv = i.wpos.xz * _Tiling;

                // calor: duas camadas a velocidades diferentes (viscosidade) + ondulação lateral lenta
                float h1 = tex2D(_HeatTex, uv * 0.55 + flow * t * _FlowSpeed).r;
                float h2 = tex2D(_HeatTex, uv * 1.15 + flow * t * _FlowSpeed * 0.6 + side * sin(t * 0.21) * 0.05).r;
                float heat = saturate(h1 * 0.62 + h2 * 0.38);

                // crosta: arrasta-se devagar e é deformada pelo calor
                float2 cuv = uv + flow * t * _CrustSpeed + (heat - 0.5) * 0.06 * side;
                float4 crust = tex2D(_CrustTex, cuv);
                float cover = crust.a + (0.48 - heat) * 0.75 + _CrustAmount;
                float crustMask = smoothstep(0.36, 0.6, cover);
                float crackEdge = smoothstep(0.22, 0.42, cover) * (1 - crustMask);   // orla vermelha junto às placas

                // relevo falso da crosta (gradiente do alfa) para a luz do sol
                float2 du = float2(_CrustTex_TexelSize.x * 3, 0);
                float ax = tex2D(_CrustTex, cuv + du.xy).a - tex2D(_CrustTex, cuv - du.xy).a;
                float az = tex2D(_CrustTex, cuv + du.yx).a - tex2D(_CrustTex, cuv - du.yx).a;
                float3 n = normalize(float3(-ax * 2.5, 1, -az * 2.5));
                float ndl = saturate(dot(n, normalize(_WorldSpaceLightPos0.xyz)));
                float3 lit = crust.rgb * _CrustColor.rgb * (UNITY_LIGHTMODEL_AMBIENT.rgb * 1.2 + _LightColor0.rgb * ndl * 0.85);

                // cor quente: fissura → fluxo → núcleo
                float3 hot = lerp(_CrackColor.rgb, _HotColor.rgb, smoothstep(0.25, 0.7, heat));
                hot = lerp(hot, _CoreColor.rgb, smoothstep(0.72, 0.95, heat));
                float pulse = 1 + _Pulse * sin(t * 1.3 + heat * 5.0 + i.wpos.x * 0.15);
                float3 emis = hot * _Emission * pulse;

                float3 col = lerp(emis, lit + _CrackColor.rgb * 0.06, crustMask);
                col = lerp(col, _CrackColor.rgb * _Emission * 0.9, crackEdge * 0.6);
                float4 c = float4(saturate(col), 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
