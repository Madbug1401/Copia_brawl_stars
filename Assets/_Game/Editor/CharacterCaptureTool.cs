using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Ferramenta de verificação (só no Editor): fotografa os personagens com modelo (Rig_*_model) durante o Play
    /// e grava as imagens em Temp/captures (fora do Git). Útil para afinar poses, armas e animações.
    /// </summary>
    public static class CharacterCaptureTool
    {
        static readonly string Dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "captures");

        [MenuItem("FPS-LAN/Debug: fotografar personagens com modelo", priority = 60)]
        static void CaptureAll()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Captura] Entra em Play primeiro."); return; }
            Directory.CreateDirectory(Dir);
            var report = new StringBuilder();
            int i = 0;
            foreach (var anim in Rigs())
            {
                var root = anim.transform.parent;   // Rig_<id>_model
                foreach (var view in new[] { "front", "side", "back34" })
                    Shoot(root, view, Path.Combine(Dir, i + "_" + view + ".png"));
                report.AppendLine(Describe(i, anim));
                i++;
            }
            File.WriteAllText(Path.Combine(Dir, "report.txt"), report.ToString());
            Debug.Log("[Captura] " + i + " personagem(ns) → " + Dir + "\n" + report);
        }

        [MenuItem("FPS-LAN/Debug: filmar o personagem mais rápido (8 fotos)", priority = 61)]
        static void CaptureBurst()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Captura] Entra em Play primeiro."); return; }
            Directory.CreateDirectory(Dir);
            Animator target = null;
            float best = -1f;
            foreach (var a in Rigs())
            {
                float s = HasParam(a, "Speed") ? a.GetFloat("Speed") : 0f;
                if (s > best) { best = s; target = a; }
            }
            if (target == null) { Debug.LogWarning("[Captura] Não há personagens com modelo na cena."); return; }
            int n = 0;
            double next = EditorApplication.timeSinceStartup;
            var sb = new StringBuilder();
            void Tick()
            {
                if (target == null || !Application.isPlaying) { EditorApplication.update -= Tick; return; }
                if (EditorApplication.timeSinceStartup < next) return;
                next = EditorApplication.timeSinceStartup + 0.09;
                Shoot(target.transform.parent, "side", Path.Combine(Dir, "burst_" + n + ".png"));
                sb.AppendLine(Describe(n, target));
                if (++n >= 8)
                {
                    EditorApplication.update -= Tick;
                    File.WriteAllText(Path.Combine(Dir, "burst.txt"), sb.ToString());
                    Debug.Log("[Captura] 8 fotos de " + target.transform.parent.name + " (Speed " + best.ToString("0.0") + ")");
                }
            }
            EditorApplication.update += Tick;
        }

        // ------------------------------------------------------------------ sessão de fotos (rig de teste controlado)

        struct Shot { public string state; public float at; public Vector3 vel; public bool grounded, dead; public int evt; }

        [MenuItem("FPS-LAN/Debug: sessão de fotos do Lince (todas as animações)", priority = 62)]
        static void PhotoSession() => PhotoSessionCore(false);

        [MenuItem("FPS-LAN/Debug: sessão de fotos do Lince (só salto)", priority = 63)]
        static void PhotoSessionJump() => PhotoSessionCore(true);

        static void PhotoSessionCore(bool jumpOnly)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Captura] Entra em Play primeiro."); return; }
            BrawlerDefinition def = null;
            foreach (var b in BrawlerRegistry.All) if (b != null && b.brawlerId == "sniper") def = b;
            if (def == null) { Debug.LogWarning("[Captura] Brawler 'sniper' não encontrado."); return; }
            Directory.CreateDirectory(Dir);

            var holder = new GameObject("[CaptureLince]").transform;
            var start = new Vector3(4f, 30f, -6f);
            holder.position = Physics.Raycast(start, Vector3.down, out var hit, 60f) ? hit.point : Vector3.zero;
            var rig = CharacterRig.Build(def, holder, UnityEngine.Rendering.ShadowCastingMode.On, 0);

            // (estado, instante, velocidade, no chão, morto, evento: 1 ataque, 2 dano)
            var plan = new List<Shot>();
            void Add(string st, float t0, int count, float step, Vector3 v, bool g = true, bool d = false, int evt = 0)
            { for (int k = 0; k < count; k++) plan.Add(new Shot { state = st, at = t0 + k * step, vel = v, grounded = g, dead = d, evt = k == 0 ? evt : 0 }); }
            var fwd = holder.forward;
            if (jumpOnly)
            {
                Add("idle", 1.0f, 1, 0.5f, Vector3.zero);
                Add("jump", 1.5f, 6, 0.15f, fwd * 4f, false);
                Add("land", 3.0f, 2, 0.3f, Vector3.zero);
            }
            else {
            Add("idle", 1.0f, 2, 0.6f, Vector3.zero);
            Add("walk", 3.0f, 4, 0.11f, fwd * 2.5f);
            Add("run", 5.0f, 4, 0.08f, fwd * 6.5f);
            Add("sprint", 7.0f, 4, 0.07f, fwd * 9.5f);
            Add("jump", 9.0f, 2, 0.25f, fwd * 4f, false);
            Add("attack", 10.5f, 3, 0.07f, Vector3.zero, true, false, 1);
            Add("hurt", 12.0f, 3, 0.1f, Vector3.zero, true, false, 2);
            Add("death", 12.6f, 4, 0.35f, Vector3.zero, true, true);
            }

            double t0 = EditorApplication.timeSinceStartup, last = t0;
            int idx = 0, firedEvt = -1;
            bool deadSet = false;
            var sb = new StringBuilder();
            void Tick()
            {
                if (!Application.isPlaying || holder == null) { EditorApplication.update -= Tick; return; }
                double now = EditorApplication.timeSinceStartup;
                float dt = Mathf.Clamp((float)(now - last), 0f, 0.1f);
                last = now;
                float t = (float)(now - t0);
                var cur = plan[Mathf.Min(idx, plan.Count - 1)];
                // o estado do próximo disparo começa ~1 s antes, para a animação estabilizar
                if (cur.dead && !deadSet) { rig.SetDead(true, false); deadSet = true; }
                if (cur.evt != 0 && firedEvt != idx && t >= cur.at - 0.05f)
                {
                    firedEvt = idx;
                    if (cur.evt == 1) rig.OnAttack(false, 0.3f); else rig.OnHurt();
                }
                rig.Tick(dt, new RigFrame { worldVelocity = cur.vel, grounded = cur.grounded, pitch = 0f, superReady = false });
                if (t >= cur.at)
                {
                    var name = "seq_" + idx.ToString("00") + "_" + cur.state + ".png";
                    Shoot(rig.Root, "side", Path.Combine(Dir, name));
                    if (idx % 4 == 0) Shoot(rig.Root, "front", Path.Combine(Dir, "seq_" + idx.ToString("00") + "_" + cur.state + "_front.png"));
                    var a = rig.Root.GetComponentInChildren<Animator>();
                    sb.AppendLine(name + "  " + (a != null ? Describe(idx, a) : "-"));
                    idx++;
                    if (idx >= plan.Count)
                    {
                        EditorApplication.update -= Tick;
                        File.WriteAllText(Path.Combine(Dir, "seq.txt"), sb.ToString());
                        rig.Destroy();
                        Object.Destroy(holder.gameObject);
                        Debug.Log("[Captura] Sessão de fotos concluída: " + plan.Count + " fotos em " + Dir);
                    }
                }
            }
            EditorApplication.update += Tick;
        }

        static IEnumerable<Animator> Rigs()
        {
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (a.transform.parent != null && a.transform.parent.name.StartsWith("Rig_") && a.transform.parent.name.EndsWith("_model"))
                    yield return a;
        }

        static bool HasParam(Animator a, string name)
        {
            foreach (var p in a.parameters) if (p.name == name) return true;
            return false;
        }

        static string Describe(int i, Animator a)
        {
            var root = a.transform.parent;
            var owner = root.parent != null ? root.parent.root.name : "-";
            var sb = new StringBuilder();
            sb.Append(i).Append(": ").Append(owner).Append(" / ").Append(root.name);
            if (HasParam(a, "Speed")) sb.Append(" speed=").Append(a.GetFloat("Speed").ToString("0.0"));
            var st = a.GetCurrentAnimatorStateInfo(0);
            sb.Append(" state=").Append(st.shortNameHash).Append(" t=").Append(st.normalizedTime.ToString("0.00"));
            if (HasParam(a, "Grounded")) sb.Append(" grounded=").Append(a.GetBool("Grounded"));
            if (a.IsInTransition(0)) sb.Append(" ->").Append(a.GetNextAnimatorStateInfo(0).shortNameHash);
            sb.Append(" jumpHash=").Append(Animator.StringToHash("Jump"));
            if (a.isHuman)
            {
                var hr = a.GetBoneTransform(HumanBodyBones.RightHand);
                var hl = a.GetBoneTransform(HumanBodyBones.LeftHand);
                var wr = root.Find("WeaponR");
                if (wr != null && hr != null) sb.Append(" |mãoD-arma|=").Append(Vector3.Distance(hr.position, wr.position).ToString("0.00"));
                if (wr != null && hl != null) sb.Append(" mãoE(local arma)=").Append(wr.InverseTransformPoint(hl.position).ToString("F2"));
                var hips = a.GetBoneTransform(HumanBodyBones.Hips);
                var foot = a.GetBoneTransform(HumanBodyBones.LeftFoot);
                if (hips != null && foot != null)
                    sb.Append(" anca.y=").Append((hips.position.y - root.position.y).ToString("0.00"))
                      .Append(" pé.y=").Append((foot.position.y - root.position.y).ToString("0.00"));
            }
            return sb.ToString();
        }

        static void Shoot(Transform target, string view, string path)
        {
            var go = new GameObject("CaptureCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var fwd = target.forward; fwd.y = 0f; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            Vector3 dir = view == "front" ? fwd : view == "side" ? right : (-fwd + right * 0.8f).normalized;
            var center = target.position + Vector3.up * 0.95f;
            go.transform.position = center + dir * 4.4f + Vector3.up * 0.25f;
            go.transform.LookAt(center);
            var rt = new RenderTexture(640, 640, 24);
            cam.targetTexture = rt;
            // o jogador local só projeta sombra (está na 1.ª pessoa): mostra-o durante a foto
            var rends = target.GetComponentsInChildren<Renderer>(true);
            var modes = new UnityEngine.Rendering.ShadowCastingMode[rends.Length];
            var enabled = new bool[rends.Length];
            for (int k = 0; k < rends.Length; k++)
            {
                modes[k] = rends[k].shadowCastingMode; enabled[k] = rends[k].enabled;
                if (modes[k] == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) rends[k].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            cam.Render();
            for (int k = 0; k < rends.Length; k++) { rends[k].shadowCastingMode = modes[k]; rends[k].enabled = enabled[k]; }
            RenderTexture.active = rt;
            var tex = new Texture2D(640, 640, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }
    }
}
