using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Marcas parceiras no jogo (GameJamPlus Cabo Verde): SITA (tintas — emote de spray com o logótipo em todas as
    /// cores) e UPR Animal (rações UPRA — saco, tigela e fórmulas por espécie para alimentar o pet).
    /// Texturas em Resources/Branding; malhas construídas com primitivas no estilo do resto do jogo.
    /// </summary>
    public static class Branding
    {
        // ------------------------------------------------------------------ texturas

        static Texture2D s_SitaStencil, s_Splat, s_UpraLogo, s_UpraSack;
        static Material s_SplatMat, s_SitaMat, s_UpraMat;

        public static Texture2D SitaStencil => Load(ref s_SitaStencil, "Branding/SitaLogoStencil");
        public static Texture2D PaintSplat => Load(ref s_Splat, "Branding/PaintSplat");
        public static Texture2D UpraLogo => Load(ref s_UpraLogo, "Branding/UpraLogo");
        public static Texture2D UpraSack => Load(ref s_UpraSack, "Branding/UpraSack");

        static Texture2D Load(ref Texture2D cache, string path)
        {
            if (cache != null) return cache;
            cache = Resources.Load<Texture2D>(path);
            if (cache != null) cache.wrapMode = TextureWrapMode.Clamp;
            else cache = Texture2D.whiteTexture;
            return cache;
        }

        /// <summary>Mancha de tinta (cor por property block).</summary>
        public static Material SplatMaterial => s_SplatMat != null ? s_SplatMat : (s_SplatMat = TexMaterial(PaintSplat, "SitaSplat", 0));
        /// <summary>Logótipo SITA com contorno branco (desenhado por cima da mancha).</summary>
        public static Material SitaMaterial => s_SitaMat != null ? s_SitaMat : (s_SitaMat = TexMaterial(SitaStencil, "SitaStencil", 2));
        public static Material UpraMaterial => s_UpraMat != null ? s_UpraMat : (s_UpraMat = TexMaterial(UpraLogo, "UpraLogo", 1));

        static Material TexMaterial(Texture tex, string name, int queueOffset)
        {
            var m = new Material(Art.FxAlphaMesh) { name = name, mainTexture = tex };
            m.renderQueue = (int)RenderQueue.Transparent + 10 + queueOffset;
            return m;
        }

        // ------------------------------------------------------------------ SITA: paleta do spray

        public struct SprayColor
        {
            public string name;
            public Color color;
            public SprayColor(string n, Color c) { name = n; color = c; }
        }

        /// <summary>As 3 cores do logótipo SITA + 5 inspiradas nas fachadas de Cabo Verde. A ordem é a da roda (topo, sentido horário).</summary>
        public static readonly SprayColor[] SprayColors =
        {
            new SprayColor("VERMELHO SITA", new Color(0.89f, 0.12f, 0.2f)),
            new SprayColor("LARANJA MORABEZA", new Color(1f, 0.52f, 0.1f)),
            new SprayColor("AMARELO SITA", new Color(1f, 0.86f, 0.08f)),
            new SprayColor("VERDE SITA", new Color(0f, 0.6f, 0.36f)),
            new SprayColor("TURQUESA MAR", new Color(0.08f, 0.78f, 0.74f)),
            new SprayColor("AZUL ATLÂNTICO", new Color(0.12f, 0.42f, 0.9f)),
            new SprayColor("ROSA BUGANVÍLIA", new Color(0.93f, 0.3f, 0.66f)),
            new SprayColor("BRANCO CAL", new Color(0.97f, 0.96f, 0.93f)),
        };

        public static Color SprayColorOf(int index) => SprayColors[Mathf.Clamp(index, 0, SprayColors.Length - 1)].color;

        // ------------------------------------------------------------------ UPR Animal: fórmulas por espécie

        public static readonly Color UpraBlue = new Color(0.11f, 0.35f, 0.67f);
        public static readonly Color SackColor = new Color(0.93f, 0.86f, 0.7f);
        public static readonly Color GrainColor = new Color(0.78f, 0.53f, 0.25f);
        public const string UpraSlogan = "UPR Animal · Faz crescer o teu jogo.";

        public static string UpraFormula(PetSpecies species)
        {
            switch (species)
            {
                case PetSpecies.Dog: return "UPRA Cão";
                case PetSpecies.Cat: return "UPRA Gato";
                case PetSpecies.Pig: return "UPRA Suínos";
                case PetSpecies.Rooster: return "UPRA Aves";
                case PetSpecies.Rabbit: return "UPRA Coelhos";
                case PetSpecies.Turtle: return "UPRA Répteis";
                default: return "UPRA";
            }
        }

        public static string UpraFormulaTagline(PetSpecies species)
        {
            switch (species)
            {
                case PetSpecies.Dog: return "Dog formula · cães ativos";
                case PetSpecies.Cat: return "Cat formula · gatos felizes";
                case PetSpecies.Pig: return "Pig formula · crescer forte";
                case PetSpecies.Rooster: return "Poultry · do pinto ao galo";
                case PetSpecies.Rabbit: return "Rabbit · fibra e energia";
                case PetSpecies.Turtle: return "Reptile · casco forte";
                default: return "Ração UPR Animal";
            }
        }

        // ------------------------------------------------------------------ peças 3D

        /// <summary>Quad com textura voltado para <paramref name="faceDir"/> (local do pai), com o "cima" da imagem no +Y do pai.</summary>
        public static Transform Decal(Transform parent, Material mat, Vector3 localPos, float width, float height, Vector3 faceDir, Color color)
        {
            var go = new GameObject("Decal");
            go.layer = parent != null ? parent.gameObject.layer : 0;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            var up = Mathf.Abs(Vector3.Dot(faceDir.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            t.localRotation = Quaternion.LookRotation(up, faceDir);
            t.localScale = new Vector3(width, 1f, height);
            go.AddComponent<MeshFilter>().sharedMesh = Art.Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            Art.SetColor(r, color);
            return t;
        }

        /// <summary>Saco de ração UPRA (frente = +Z). Altura ≈ 0,62 × size, base em y = 0.</summary>
        public static Transform BuildSack(Transform parent, float size, ShadowCastingMode shadows = ShadowCastingMode.On)
        {
            var root = new GameObject("UpraSack").transform;
            root.gameObject.layer = parent != null ? parent.gameObject.layer : 0;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * size;
            var tie = Art.Darken(SackColor, 0.9f);
            Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.25f, 0f), new Vector3(0.4f, 0.5f, 0.24f), SackColor, true, default, shadows);
            Art.Part(PrimitiveType.Sphere, root, new Vector3(0f, 0.04f, 0f), new Vector3(0.44f, 0.12f, 0.27f), SackColor, true, default, shadows);
            Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.53f, 0f), new Vector3(0.3f, 0.08f, 0.17f), tie, true, default, shadows);
            Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.585f, 0f), new Vector3(0.12f, 0.025f, 0.12f), UpraBlue, true, default, shadows);
            Art.Part(PrimitiveType.Sphere, root, new Vector3(0f, 0.63f, 0f), new Vector3(0.2f, 0.07f, 0.11f), tie, true, default, shadows);
            Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.08f, 0f), new Vector3(0.405f, 0.07f, 0.245f), UpraBlue, false, default, shadows);
            Decal(root, UpraMaterial, new Vector3(0f, 0.29f, 0.125f), 0.3f, 0.3f, Vector3.forward, Color.white);
            Decal(root, UpraMaterial, new Vector3(0f, 0.29f, -0.125f), 0.3f, 0.3f, Vector3.back, Color.white);
            SetLayer(root, root.gameObject.layer);
            return root;
        }

        /// <summary>Tigela UPRA (base em y = 0, diâmetro ≈ 0,42 × size). Devolve a ração (para animar o enchimento).</summary>
        public static Transform BuildBowl(Transform parent, float size, out Transform food)
        {
            var root = new GameObject("UpraBowl").transform;
            root.gameObject.layer = parent != null ? parent.gameObject.layer : 0;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * size;
            Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.05f, 0f), new Vector3(0.42f, 0.05f, 0.42f), UpraBlue, true);
            Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.095f, 0f), new Vector3(0.48f, 0.012f, 0.48f), Art.Lighten(UpraBlue, 0.25f), true);
            Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.1f, 0f), new Vector3(0.36f, 0.006f, 0.36f), Art.Darken(UpraBlue, 0.45f), false);
            food = Art.Part(PrimitiveType.Sphere, root, new Vector3(0f, 0.1f, 0f), new Vector3(0.34f, 0.08f, 0.34f), GrainColor, false);
            // Logótipo deitado ao lado da tigela, como um tapete da marca
            Decal(root, UpraMaterial, new Vector3(0f, 0.012f, -0.4f), 0.32f, 0.32f, Vector3.up, Color.white);
            SetLayer(root, root.gameObject.layer);
            return root;
        }

        /// <summary>Lata de spray SITA na cor escolhida (eixo +Y, bico a apontar para +Z).</summary>
        public static Transform BuildSprayCan(Transform parent, Color color, ShadowCastingMode shadows = ShadowCastingMode.Off)
        {
            var root = new GameObject("SitaCan").transform;
            root.gameObject.layer = parent != null ? parent.gameObject.layer : 0;
            root.SetParent(parent, false);
            Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.11f, 0f), new Vector3(0.075f, 0.11f, 0.075f), Color.white, true, default, shadows);
            Art.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.17f, 0f), new Vector3(0.077f, 0.04f, 0.077f), color, false, default, shadows);
            Art.Part(PrimitiveType.Sphere, root, new Vector3(0f, 0.23f, 0f), new Vector3(0.06f, 0.04f, 0.06f), color, true, default, shadows);
            Art.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.25f, 0.012f), new Vector3(0.022f, 0.022f, 0.03f), Color.white, false, default, shadows);
            Decal(root, SitaMaterial, new Vector3(0f, 0.09f, -0.039f), 0.085f, 0.085f, Vector3.back, Color.white);
            Decal(root, SitaMaterial, new Vector3(0f, 0.09f, 0.039f), 0.085f, 0.085f, Vector3.forward, Color.white);
            SetLayer(root, root.gameObject.layer);
            return root;
        }

        public static void SetLayer(Transform t, int layer)
        {
            if (t == null) return;
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        public static void SetShadows(Transform t, ShadowCastingMode mode)
        {
            if (t == null) return;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = mode;
        }
    }
}
