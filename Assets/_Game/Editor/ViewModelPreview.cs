using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Ajuda para afinar a vista em 1.ª pessoa (em Play): monta o viewmodel de um personagem longe do mapa, com uma
    /// câmara FPS ("VM_Cam") e uma câmara lateral ("VM_Side") para ver como a mão encaixa na arma.
    /// </summary>
    public static class ViewModelPreview
    {
        public static ViewModelRig Rig;

        public static string Show(string brawlerId, float sideYaw = 90f, float sideDist = 0.9f, string focus = null)
        {
            var root = GameObject.Find("VM_Preview");
            if (root == null)
            {
                root = new GameObject("VM_Preview");
                root.transform.position = new Vector3(0f, 500f, 0f);
                var c = new GameObject("VM_Cam").AddComponent<Camera>();
                c.transform.SetParent(root.transform, false);
                c.nearClipPlane = 0.01f; c.fieldOfView = 70f; c.depth = 100f;
                c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.55f, 0.7f, 0.85f);
                var sc = new GameObject("VM_Side").AddComponent<Camera>();
                sc.transform.SetParent(root.transform, false);
                sc.nearClipPlane = 0.01f; sc.fieldOfView = 30f; sc.enabled = false;
                sc.clearFlags = CameraClearFlags.SolidColor; sc.backgroundColor = new Color(0.6f, 0.6f, 0.65f);
                            }
            var cam = root.transform.Find("VM_Cam");
            if (Rig != null) Rig.Destroy();
            for (int i = cam.childCount - 1; i >= 0; i--) Object.Destroy(cam.GetChild(i).gameObject);
            Rig = ViewModelRig.Build(BrawlerRegistry.FindById(brawlerId), cam, 0);
            Rig.Tick(0.016f, default);
            Transform target = null;
            if (!string.IsNullOrEmpty(focus))
                foreach (var t in Rig.Root.GetComponentsInChildren<Transform>(true)) if (t.name == focus) { target = t; break; }
            var center = target != null ? target.position : cam.position + cam.forward * 0.4f + cam.up * -0.2f;
            var side = root.transform.Find("VM_Side");
            side.position = center + Quaternion.Euler(0f, sideYaw, 0f) * (Vector3.back * sideDist) + Vector3.up * 0.1f;
            side.LookAt(center);
            return Rig.Style + " focus=" + (target != null ? target.name : "cam");
        }

        /// <summary>Avança a animação (ex.: depois de OnAttack) para ver a pose a meio.</summary>
        public static void Step(float seconds)
        {
            if (Rig == null) return;
            for (float t = 0f; t < seconds; t += 0.016f) Rig.Tick(0.016f, default);
        }
    }
}
