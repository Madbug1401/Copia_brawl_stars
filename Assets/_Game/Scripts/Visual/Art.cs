using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Materiais, texturas e malhas partilhados pelos visuais gerados por código (personagens, armas, efeitos).
    /// Os materiais vêm de Resources/Art (gerados pelo editor, garantem que os shaders entram no build);
    /// se faltarem, são criados a partir dos shaders; se os shaders falharem, cai para o Standard.
    /// </summary>
    public static class Art
    {
        public static readonly int ColorId = Shader.PropertyToID("_Color");
        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly int EmissionId = Shader.PropertyToID("_Emission");
        public static readonly int FlashId = Shader.PropertyToID("_Flash");
        public static readonly int FadeId = Shader.PropertyToID("_Fade");
        public static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        public static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

        static Material s_Toon, s_ToonOutline, s_FxAdd, s_FxAlpha, s_FxAddMesh, s_FxAlphaMesh;
        static Texture2D s_Soft, s_Smoke, s_Ring, s_Star, s_Spark, s_Circle, s_Beam;
        static Material s_BeamMat;
        static Mesh s_Quad;
        static MaterialPropertyBlock s_Block;
        static readonly Dictionary<PrimitiveType, Mesh> s_Meshes = new Dictionary<PrimitiveType, Mesh>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Toon = s_ToonOutline = s_FxAdd = s_FxAlpha = s_FxAddMesh = s_FxAlphaMesh = null;
            s_Soft = s_Smoke = s_Ring = s_Star = s_Spark = s_Circle = s_Beam = null;
            s_BeamMat = null;
            s_Quad = null;
            s_Block = null;
            s_Meshes.Clear();
        }

        public static MaterialPropertyBlock Block => s_Block ?? (s_Block = new MaterialPropertyBlock());

        // ------------------------------------------------------------------ materiais

        /// <summary>Cel shading sem contorno (cenário, peças pequenas).</summary>
        public static Material Toon => s_Toon != null ? s_Toon : (s_Toon = Load("Art/M_Toon", "FPSLAN/Toon", false));

        /// <summary>Cel shading com contorno escuro (personagens, armas, projéteis).</summary>
        public static Material ToonOutline => s_ToonOutline != null ? s_ToonOutline : (s_ToonOutline = Load("Art/M_ToonOutline", "FPSLAN/ToonOutline", false));

        /// <summary>Partículas aditivas (fogo, brilhos, faíscas). Usa a cor dos vértices.</summary>
        public static Material FxAdditive => s_FxAdd != null ? s_FxAdd : (s_FxAdd = FxMaterial("Art/M_FxAdditive", "FPSLAN/FxAdditive", true, SoftTexture));

        /// <summary>Partículas com alpha (fumo, poeira, decalques). Usa a cor dos vértices.</summary>
        public static Material FxAlpha => s_FxAlpha != null ? s_FxAlpha : (s_FxAlpha = FxMaterial("Art/M_FxAlpha", "FPSLAN/FxAlpha", true, SmokeTexture));

        /// <summary>Aditivo para malhas sem cor nos vértices (núcleos de explosão, rastos, auras).</summary>
        public static Material FxAdditiveMesh => s_FxAddMesh != null ? s_FxAddMesh : (s_FxAddMesh = FxMaterial("Art/M_FxAdditive", "FPSLAN/FxAdditive", false, null));

        /// <summary>Alpha para malhas sem cor nos vértices (zonas no chão, bolhas, anéis).</summary>
        public static Material FxAlphaMesh => s_FxAlphaMesh != null ? s_FxAlphaMesh : (s_FxAlphaMesh = FxMaterial("Art/M_FxAlpha", "FPSLAN/FxAlpha", false, null));

        /// <summary>Aditivo com textura de feixe (linhas: tracers, rastos de projéteis).</summary>
        public static Material FxBeam
        {
            get
            {
                if (s_BeamMat != null) return s_BeamMat;
                s_BeamMat = new Material(FxAdditive) { name = "FxBeam", mainTexture = BeamTexture };
                return s_BeamMat;
            }
        }

        static Material Load(string resource, string shaderName, bool transparent)
        {
            var m = Resources.Load<Material>(resource);
            if (m != null && m.shader != null && m.shader.isSupported) return new Material(m);

            var shader = Shader.Find(shaderName);
            if (shader != null && shader.isSupported) return new Material(shader);

            // Último recurso: Standard (o jogo continua a funcionar, só sem o estilo cartoon)
            var std = Shader.Find("Standard");
            var fallback = new Material(std != null ? std : Shader.Find("Sprites/Default"));
            if (transparent) fallback.renderQueue = (int)RenderQueue.Transparent;
            return fallback;
        }

        static Material FxMaterial(string resource, string shaderName, bool vertexColor, Texture tex)
        {
            Material m;
            var res = Resources.Load<Material>(resource);
            if (res != null && res.shader != null && res.shader.isSupported) m = new Material(res);
            else
            {
                var shader = Shader.Find(shaderName);
                if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
                m = new Material(shader);
            }
            m.name = shaderName + (vertexColor ? "" : " (mesh)");
            if (m.HasProperty("_VertexColor")) m.SetFloat("_VertexColor", vertexColor ? 1f : 0f);
            m.mainTexture = tex != null ? tex : Texture2D.whiteTexture;
            return m;
        }

        // ------------------------------------------------------------------ cores (property blocks)

        public static void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            var b = Block;
            r.GetPropertyBlock(b);
            b.SetColor(ColorId, c);
            b.SetColor(BaseColorId, c);
            r.SetPropertyBlock(b);
        }

        public static void SetEmission(Renderer r, Color c)
        {
            if (r == null) return;
            var b = Block;
            r.GetPropertyBlock(b);
            b.SetColor(EmissionId, c);
            r.SetPropertyBlock(b);
        }

        public static void SetFloat(Renderer r, int id, float v)
        {
            if (r == null) return;
            var b = Block;
            r.GetPropertyBlock(b);
            b.SetFloat(id, v);
            r.SetPropertyBlock(b);
        }

        // ------------------------------------------------------------------ construção de peças

        public static Mesh PrimitiveMesh(PrimitiveType type)
        {
            if (s_Meshes.TryGetValue(type, out var m) && m != null) return m;
            var go = GameObject.CreatePrimitive(type);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            s_Meshes[type] = m;
            return m;
        }

        /// <summary>Peça visual (sem colisor) com cel shading.</summary>
        public static Transform Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Color color,
            bool outline = true, Vector3 localEuler = default, ShadowCastingMode shadows = ShadowCastingMode.On)
        {
            var go = new GameObject(type.ToString());
            go.layer = parent != null ? parent.gameObject.layer : 0;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.Euler(localEuler);
            t.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(type);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = outline ? ToonOutline : Toon;
            r.shadowCastingMode = shadows;
            r.receiveShadows = true;
            SetColor(r, color);
            return t;
        }

        /// <summary>Peça com material de efeito (aditivo/alpha) sem sombras.</summary>
        public static Transform FxPart(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Color color, bool additive, Vector3 localEuler = default)
        {
            var go = new GameObject("Fx" + type);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.Euler(localEuler);
            t.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(type);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = additive ? FxAdditiveMesh : FxAlphaMesh;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            SetColor(r, color);
            return t;
        }

        /// <summary>Quad horizontal com textura (decalques, anéis no chão).</summary>
        public static Transform FxQuad(Transform parent, Texture tex, Color color, bool additive)
        {
            var go = new GameObject("FxQuad");
            var t = go.transform;
            t.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            var r = go.AddComponent<MeshRenderer>();
            var baseMat = additive ? FxAdditiveMesh : FxAlphaMesh;
            var m = new Material(baseMat) { mainTexture = tex };
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            SetColor(r, color);
            return t;
        }

        /// <summary>Quad deitado no plano XZ, centrado, 1x1.</summary>
        public static Mesh Quad
        {
            get
            {
                if (s_Quad != null) return s_Quad;
                var m = new Mesh { name = "FlatQuad" };
                m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
                m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                m.RecalculateBounds();
                s_Quad = m;
                return m;
            }
        }

        // ------------------------------------------------------------------ texturas procedurais

        public static Texture2D SoftTexture => s_Soft != null ? s_Soft : (s_Soft = MakeTex("Soft", 64, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float a = Mathf.Clamp01(1f - d);
            return new Color(1f, 1f, 1f, a * a);
        }));

        public static Texture2D CircleTexture => s_Circle != null ? s_Circle : (s_Circle = MakeTex("Circle", 128, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            return new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 40f));
        }));

        public static Texture2D SmokeTexture => s_Smoke != null ? s_Smoke : (s_Smoke = MakeTex("Smoke", 64, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float n = Mathf.PerlinNoise(x * 2.3f + 5f, y * 2.3f + 9f) * 0.55f + Mathf.PerlinNoise(x * 5f + 1f, y * 5f + 3f) * 0.3f;
            float a = Mathf.Clamp01(1f - d * (0.85f + n * 0.5f));
            a = Mathf.SmoothStep(0f, 1f, a);
            float shade = 0.82f + n * 0.25f;
            return new Color(shade, shade, shade, a);
        }));

        public static Texture2D RingTexture => s_Ring != null ? s_Ring : (s_Ring = MakeTex("Ring", 128, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) / 0.14f);
            return new Color(1f, 1f, 1f, a * a);
        }));

        public static Texture2D StarTexture => s_Star != null ? s_Star : (s_Star = MakeTex("Star", 64, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float ang = Mathf.Atan2(y, x);
            float r = 0.45f + 0.5f * Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 2.5f)), 6f);
            float a = Mathf.Clamp01((r - d) * 10f);
            float core = Mathf.Clamp01(1f - d * 2.2f);
            return new Color(1f, 1f, 1f, Mathf.Max(a, core));
        }));

        public static Texture2D SparkTexture => s_Spark != null ? s_Spark : (s_Spark = MakeTex("Spark", 32, (x, y) =>
        {
            float a = Mathf.Clamp01(1f - Mathf.Abs(x)) * Mathf.Clamp01(1f - Mathf.Abs(y) * 1.6f);
            return new Color(1f, 1f, 1f, a * a);
        }));

        public static Texture2D BeamTexture => s_Beam != null ? s_Beam : (s_Beam = MakeTex("Beam", 32, (x, y) =>
        {
            float a = Mathf.Clamp01(1f - Mathf.Abs(y));
            return new Color(1f, 1f, 1f, a * a);
        }));

        static Texture2D MakeTex(string name, int size, System.Func<float, float, Color> f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    px[y * size + x] = f(u, v);
                }
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        // ------------------------------------------------------------------ utilitários de cor

        public static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        public static Color Lighten(Color c, float k) => Color.Lerp(c, Color.white, k);
        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
