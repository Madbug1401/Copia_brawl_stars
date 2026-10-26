// Visão térmica da luneta da Lince (pipeline built-in). Usado como "replacement shader" pela câmara do jogador
// (ThermalScope): todo o cenário fica frio (azul-escuro, com a forma dada pelo brilho da textura e pela normal) e quem
// tem _Thermal = 1 (jogadores) fica quente, de vermelho a branco. Respeita o _Fade em dithering, por isso os arbustos
// (que a luneta deixa quase transparentes) não escondem ninguém. Transparentes (efeitos) não são desenhados.
Shader "Hidden/FPSLAN/Thermal"
{
    Properties
    {
        _MainTex ("Textura", 2D) = "white" {}
        _Color ("Cor", Color) = (1,1,1,1)
        _Fade ("Opacidade (dither)", Range(0,1)) = 1
        _Thermal ("Quente", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Fade;
            float _Thermal;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float4 sp : TEXCOORD3;
            };

            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.sp = ComputeScreenPos(o.pos);
                return o;
            }

            void dither(float4 sp, float fade)
            {
                if (fade >= 0.999) return;
                float2 p = floor(sp.xy / max(sp.w, 0.0001) * _ScreenParams.xy);
                float2 m = fmod(p, 4.0);
                float4 r0 = float4(0.0, 8.0, 2.0, 10.0);
                float4 r1 = float4(12.0, 4.0, 14.0, 6.0);
                float4 r2 = float4(3.0, 11.0, 1.0, 9.0);
                float4 r3 = float4(15.0, 7.0, 13.0, 5.0);
                float4 row = m.y < 0.5 ? r0 : (m.y < 1.5 ? r1 : (m.y < 2.5 ? r2 : r3));
                float b = m.x < 0.5 ? row.x : (m.x < 1.5 ? row.y : (m.x < 2.5 ? row.z : row.w));
                clip(fade - (b + 0.5) / 16.0);
            }

            float3 hotRamp(float t)
            {
                // vermelho escuro → laranja → amarelo → branco
                float3 c = lerp(float3(0.55, 0.02, 0.10), float3(1.00, 0.35, 0.02), saturate(t * 2.0));
                c = lerp(c, float3(1.00, 0.86, 0.18), saturate(t * 2.0 - 0.7));
                return lerp(c, float3(1.00, 1.00, 0.92), saturate(t * 3.0 - 2.2));
            }

            float4 frag(v2f i) : SV_Target
            {
                dither(i.sp, _Fade);
                float3 n = normalize(i.wn);
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                if (_Thermal > 0.5)
                {
                    // corpo quente: mais quente de frente para a câmara, bordas a arrefecer; pulsa ligeiramente
                    float facing = saturate(dot(n, v));
                    float t = 0.45 + 0.5 * facing + 0.05 * sin(_Time.y * 6.0 + i.wp.y * 4.0);
                    return float4(hotRamp(t), 1);
                }
                float3 albedo = tex2D(_MainTex, i.uv).rgb * _Color.rgb;
                float lum = dot(albedo, float3(0.30, 0.59, 0.11));
                float shade = 0.55 + 0.45 * saturate(n.y * 0.5 + 0.5);
                float dist = length(_WorldSpaceCameraPos - i.wp);
                float cold = saturate((0.12 + lum * 0.5) * shade) * lerp(1.0, 0.45, saturate(dist / 60.0));
                float3 c = lerp(float3(0.02, 0.03, 0.10), float3(0.20, 0.36, 0.52), cold);
                return float4(c, 1);
            }
            ENDCG
        }
    }
}
