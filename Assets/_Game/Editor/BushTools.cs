using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Arbustos de folhas (<see cref="LeafBush"/>): material partilhado e comandos para converter os arbustos
    /// da cena aberta sem regenerar a Arena inteira.
    /// </summary>
    public static class BushTools
    {
        public const string LeafMaterialPath = "Assets/_Game/Materials/M_BushLeaves.mat";

        /// <summary>Material cartoon com cor por vértice (cada folha traz a sua cor na malha).</summary>
        public static Material LeafMaterial()
        {
            var shader = Shader.Find("FPSLAN/ToonOutline");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) shader = Shader.Find("Standard");
            var m = AssetDatabase.LoadAssetAtPath<Material>(LeafMaterialPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, LeafMaterialPath);
            }
            else if (m.shader != shader) m.shader = shader;

            if (m.HasProperty("_Color")) m.color = Color.white;
            Set(m, "_VertexColor", 1f);
            Set(m, "_OutlineWidth", 0.0012f);
            Set(m, "_OutlineColor", new Color(0.05f, 0.2f, 0.07f));
            Set(m, "_RimColor", new Color(0.85f, 1f, 0.6f, 0.3f));
            Set(m, "_ShadeColor", new Color(0.6f, 0.72f, 0.82f));
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Set(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
        static void Set(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }

        /// <summary>Cria o visual de folhas como filho do arbusto (o <see cref="Bush"/> continua a tratar do esconder/fade).</summary>
        public static LeafBush AddLeaves(Bush bush, int seed, Material mat = null)
        {
            var go = new GameObject("Leaves", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(bush.transform, false);
            go.isStatic = false; // malha gerada em runtime: fora do static batching
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat != null ? mat : LeafMaterial();
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            var lb = go.AddComponent<LeafBush>();
            lb.size = new Vector3(bush.halfExtents.x * 2f, Mathf.Max(0.5f, bush.height - 0.2f), bush.halfExtents.y * 2f);
            lb.seed = seed;
            lb.Rebuild();
            return lb;
        }

        /// <summary>Seed estável a partir da posição: o mesmo arbusto mantém o aspeto entre regenerações.</summary>
        public static int SeedFor(Vector3 pos)
        {
            unchecked
            {
                int h = Mathf.RoundToInt(pos.x * 10f) * 73856093 ^ Mathf.RoundToInt(pos.z * 10f) * 19349663;
                return (h & 0x7fffffff) % 100000 + 1;
            }
        }

        [MenuItem("FPS-LAN/Arbustos: aplicar folhas na cena aberta", priority = 4)]
        static void ApplyMenu() => Apply(false);

        [MenuItem("FPS-LAN/Arbustos: sortear novas variações", priority = 5)]
        static void RerollMenu() => Apply(true);

        static void Apply(bool reroll)
        {
            var bushes = new System.Collections.Generic.List<Bush>();
            foreach (var b in Resources.FindObjectsOfTypeAll<Bush>())
                if (!EditorUtility.IsPersistent(b) && b.gameObject.scene.IsValid() && b.hideFlags == HideFlags.None) bushes.Add(b);
            if (bushes.Count == 0)
            {
                EditorUtility.DisplayDialog("FPS-LAN", "Não há arbustos (componente Bush) na cena aberta.", "OK");
                return;
            }
            var mat = LeafMaterial();
            int group = Undo.GetCurrentGroup();
            foreach (var b in bushes)
            {
                // Remove o visual antigo (esferas "Leaves"/"Top") ou as folhas anteriores
                for (int i = b.transform.childCount - 1; i >= 0; i--)
                {
                    var child = b.transform.GetChild(i);
                    if (child.name == "Leaves" || child.name == "Top" || child.GetComponent<LeafBush>() != null)
                        Undo.DestroyObjectImmediate(child.gameObject);
                }
                int seed = reroll ? Random.Range(1, 100000) : SeedFor(b.transform.position);
                var lb = AddLeaves(b, seed, mat);
                Undo.RegisterCreatedObjectUndo(lb.gameObject, "Arbustos de folhas");
            }
            Undo.CollapseUndoOperations(group);
            foreach (var b in bushes) EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[FPS-LAN] " + bushes.Count + " arbustos convertidos em folhas. Grava a cena (Ctrl+S).");
        }
    }
}
