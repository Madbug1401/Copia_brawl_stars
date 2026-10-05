// Iluminação cartoon partilhada pelos shaders FPSLAN/Toon e FPSLAN/ToonOutline (pipeline built-in).
#ifndef FPSLAN_TOON_INCLUDED
#define FPSLAN_TOON_INCLUDED

#include "UnityCG.cginc"
#ifndef FPSLAN_OUTLINE_ONLY
#include "Lighting.cginc"
#include "AutoLight.cginc"
#endif

sampler2D _MainTex;
float4 _MainTex_ST;
float4 _Color;
float4 _ShadeColor;
float4 _RimColor;
float4 _Emission;
float _Checker;
float _CheckerScale;
float _TopLight;
float _Flash;
float _Fade;
float _Noise;
float _VertexColor;

// Transparência por "dithering" (sem ordenar transparentes): usada por arbustos e jogadores escondidos.
void toon_dither(float4 sp, float fade)
{
    if (fade >= 0.999) return;
    float2 p = floor(sp.xy / max(sp.w, 0.0001) * _ScreenParams.xy);
    float2 m = fmod(p, 4.0);
    // Matriz de Bayer 4x4
    float4 r0 = float4(0.0, 8.0, 2.0, 10.0);
    float4 r1 = float4(12.0, 4.0, 14.0, 6.0);
    float4 r2 = float4(3.0, 11.0, 1.0, 9.0);
    float4 r3 = float4(15.0, 7.0, 13.0, 5.0);
    float4 row = m.y < 0.5 ? r0 : (m.y < 1.5 ? r1 : (m.y < 2.5 ? r2 : r3));
    float b = m.x < 0.5 ? row.x : (m.x < 1.5 ? row.y : (m.x < 2.5 ? row.z : row.w));
    clip(fade - (b + 0.5) / 16.0);
}

#ifndef FPSLAN_OUTLINE_ONLY
struct toon_v2f
{
    float4 pos : SV_POSITION;
    float3 wn : TEXCOORD0;
    float3 wp : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float4 sp : TEXCOORD3;
    SHADOW_COORDS(4)
    UNITY_FOG_COORDS(5)
    float4 color : COLOR;
};

toon_v2f toon_vert(appdata_full v)
{
    toon_v2f o;
    o.color = lerp(float4(1, 1, 1, 1), v.color, _VertexColor);
    o.pos = UnityObjectToClipPos(v.vertex);
    o.wn = UnityObjectToWorldNormal(v.normal);
    o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
    o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
    o.sp = ComputeScreenPos(o.pos);
    TRANSFER_SHADOW(o);
    UNITY_TRANSFER_FOG(o, o.pos);
    return o;
}

float toon_hash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float4 toon_albedo(toon_v2f i, float3 n)
{
    float4 albedo = tex2D(_MainTex, i.uv) * _Color * i.color;
    // Mosaico em espaço do mundo (chão de relva/areia em quadrados)
    if (_Checker > 0.001)
    {
        float2 c = floor(i.wp.xz / max(0.05, _CheckerScale));
        float chk = fmod(abs(c.x + c.y), 2.0);
        float var = toon_hash(c) - 0.5;
        albedo.rgb *= 1.0 - _Checker * chk + var * _Checker * 0.35;
    }
    // Variação suave (manchas) para quebrar superfícies grandes
    if (_Noise > 0.001)
    {
        float2 q = floor(i.wp.xz * 1.3 + i.wp.y * 0.7);
        albedo.rgb *= 1.0 + (toon_hash(q) - 0.5) * _Noise;
    }
    // Faces de cima mais claras (blocos com "tampa")
    albedo.rgb *= 1.0 + _TopLight * saturate(n.y - 0.5) * 2.0;
    return albedo;
}

float4 toon_frag_base(toon_v2f i) : SV_Target
{
    toon_dither(i.sp, _Fade);
    float3 n = normalize(i.wn);
    float3 v = normalize(_WorldSpaceCameraPos.xyz - i.wp);
    float3 l = normalize(_WorldSpaceLightPos0.xyz);
    float4 albedo = toon_albedo(i, n);

    UNITY_LIGHT_ATTENUATION(atten, i, i.wp);
    float ndl = dot(n, l);
    // Duas bandas de luz (cel shading) + sombra projetada também em banda
    float band = smoothstep(-0.02, 0.06, ndl) * 0.75 + smoothstep(0.45, 0.55, ndl) * 0.25;
    float shadowBand = smoothstep(0.25, 0.55, atten);
    float lit = band * shadowBand;

    float3 ambient = ShadeSH9(float4(n, 1.0));
    float3 lightCol = _LightColor0.rgb;
    float3 c = albedo.rgb * (ambient + lightCol * lit);
    c *= lerp(_ShadeColor.rgb, float3(1.0, 1.0, 1.0), lit);

    // Rim light (contorno claro estilo cartoon)
    float rim = 1.0 - saturate(dot(n, v));
    c += _RimColor.rgb * _RimColor.a * smoothstep(0.55, 0.7, rim) * (0.35 + 0.65 * lit);

    // Brilho especular pequeno e duro
    float3 h = normalize(l + v);
    float spec = smoothstep(0.965, 0.975, saturate(dot(n, h))) * lit;
    c += lightCol * spec * 0.25 * albedo.a;

    c += _Emission.rgb;
    c = lerp(c, float3(1.0, 1.0, 1.0), saturate(_Flash));

    float4 col = float4(c, 1.0);
    UNITY_APPLY_FOG(i.fogCoord, col);
    return col;
}

float4 toon_frag_add(toon_v2f i) : SV_Target
{
    toon_dither(i.sp, _Fade);
    float3 n = normalize(i.wn);
    float3 l = normalize(UnityWorldSpaceLightDir(i.wp));
    float4 albedo = toon_albedo(i, n);
    UNITY_LIGHT_ATTENUATION(atten, i, i.wp);
    float lit = smoothstep(0.0, 0.1, dot(n, l)) * atten;
    float4 col = float4(albedo.rgb * _LightColor0.rgb * lit, 0.0);
    UNITY_APPLY_FOG_COLOR(i.fogCoord, col, float4(0, 0, 0, 0));
    return col;
}

#endif // FPSLAN_OUTLINE_ONLY

// ------------------------------------------------------------------ contorno (inverted hull)

float _OutlineWidth;
float4 _OutlineColor;

struct outline_v2f
{
    float4 pos : SV_POSITION;
    float4 sp : TEXCOORD0;
    UNITY_FOG_COORDS(1)
};

outline_v2f outline_vert(appdata_base v)
{
    outline_v2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    float3 viewNormal = mul((float3x3)UNITY_MATRIX_IT_MV, v.normal);
    float2 off = TransformViewToProjection(viewNormal.xy);
    float len = max(length(off), 0.0001);
    off /= len;
    off.x *= _ScreenParams.y / max(1.0, _ScreenParams.x);
    // Espessura constante em pixels, mas limitada ao longe para não "engordar" objetos distantes
    float w = _OutlineWidth * min(o.pos.w, 12.0);
    o.pos.xy += off * w;
    o.sp = ComputeScreenPos(o.pos);
    UNITY_TRANSFER_FOG(o, o.pos);
    return o;
}

float4 outline_frag(outline_v2f i) : SV_Target
{
    toon_dither(i.sp, _Fade);
    float4 col = float4(lerp(_OutlineColor.rgb, float3(1.0, 1.0, 1.0), saturate(_Flash) * 0.6), 1.0);
    UNITY_APPLY_FOG(i.fogCoord, col);
    return col;
}

#endif
