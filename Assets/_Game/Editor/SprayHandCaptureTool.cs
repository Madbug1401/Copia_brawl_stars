using System.IO;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Debug (só no Editor, em Play): fotografa a mão do Mr.S na lata de spray (vista do jogador e grandes planos)
    /// para Temp/captures/spray_*.png. Ordem por ficheiro: Temp/captures/spray_cmd.txt com "run".
    /// </summary>
    [InitializeOnLoad]
    public static class SprayHandCaptureTool
    {
        static readonly string Dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "captures");
        static double s_Next;

        static SprayHandCaptureTool() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < s_Next) return;
            s_Next = EditorApplication.timeSinceStartup + 0.5;
            var f = Path.Combine(Dir, "spray_cmd.txt");
            if (!File.Exists(f)) return;
            try { File.Delete(f); } catch { return; }
            Run();
        }

        [MenuItem("FPS-LAN/Debug: fotos da mão do Mr.S (spray)", priority = 65)]
        static void Run()
        {
            var status = Path.Combine(Dir, "spray_status.txt");
            Directory.CreateDirectory(Dir);
            if (!Application.isPlaying) { File.WriteAllText(status, "erro: não está em Play"); return; }
            BrawlerDefinition def = null;
            foreach (var b in BrawlerRegistry.All) if (b != null && b.brawlerId == "sprayer") def = b;
            if (def == null) { File.WriteAllText(status, "erro: sem sprayer"); return; }
            foreach (var old in Directory.GetFiles(Dir, "spray_*.png")) File.Delete(old);

            var camGo = new GameObject("[SprayCapture]");
            camGo.transform.position = new Vector3(0f, 400f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 85f; cam.nearClipPlane = 0.01f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.6f, 0.68f);
            cam.enabled = false;
            var lightGo = new GameObject("[SprayLight]");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            var rig = ViewModelRig.Build(def, cam.transform, 0);
            for (int i = 0; i < 30; i++) rig.Tick(0.016f, new ViewFrame { grounded = true });
            var c = cam.transform;
            Shoot(cam, c.position, c.rotation, 85f, "spray_0_eye.png");
            Transform canR = null;
            foreach (var t in rig.Root.GetComponentsInChildren<Transform>(true)) if (t.name == "CanR" || t.name == "Can") { canR = t; break; }
            if (canR != null)
            {
                var bounds = new Bounds(canR.position, Vector3.zero);
                foreach (var r in canR.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                var ctr = bounds.center;
                float dist = bounds.extents.magnitude * 3.2f;
                // volta à lata no plano da lata (8 ângulos, 0 = de frente, 180 = do lado do jogador), mais um de cima
                for (int i = 0; i < 8; i++)
                {
                    var d = Quaternion.AngleAxis(i * 45f, canR.up) * canR.forward + canR.up * 0.25f;
                    var p = ctr + d.normalized * dist;
                    Shoot(cam, p, Quaternion.LookRotation(ctr - p, canR.up), 35f, "spray_a" + (i * 45).ToString("000") + ".png");
                }
                var pt = ctr + (canR.up - canR.forward * 0.35f).normalized * dist;
                Shoot(cam, pt, Quaternion.LookRotation(ctr - pt, canR.forward), 35f, "spray_top.png");
                var p2 = c.position + (ctr - c.position) * 0.5f;
                Shoot(cam, p2, Quaternion.LookRotation(ctr - p2, c.up), 45f, "spray_near.png");
            }
            rig.Destroy();
            Object.Destroy(camGo); Object.Destroy(lightGo);
            File.WriteAllText(status, "feito " + System.DateTime.Now.ToString("HH:mm:ss"));
        }

        static void Shoot(Camera cam, Vector3 pos, Quaternion rot, float fov, string file)
        {
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
            var rt = new RenderTexture(900, 600, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(900, 600, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 900, 600), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(Dir, file), tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
    }
}
