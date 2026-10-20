using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Debug (só no Editor): monta a vista em 1.ª pessoa do Lince longe do mapa, fotografa-a (vista do jogador e de lado)
    /// e mede a distância das mãos à arma, para várias afinações de uma vez (Temp/captures/fp_params.txt).
    /// Também aceita ordens por ficheiro (Temp/captures/fp_cmd.txt: refresh, play, run, stop), para afinar sem mexer no ecrã.
    /// </summary>
    [InitializeOnLoad]
    public static class FirstPersonCaptureTool
    {
        static readonly string Dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "captures");
        static string CmdFile => Path.Combine(Dir, "fp_cmd.txt");
        static string StatusFile => Path.Combine(Dir, "fp_status.txt");
        static double s_NextPoll;
        static bool s_Running;

        static FirstPersonCaptureTool()
        {
            EditorApplication.update += Poll;
            CompilationPipeline.assemblyCompilationFinished += (asm, msgs) =>
            {
                var sb = new StringBuilder();
                foreach (var m in msgs) if (m.type == CompilerMessageType.Error) sb.AppendLine(m.message);
                if (sb.Length > 0) { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "fp_compile_errors.txt"), asm + "\n" + sb); }
            };
            Status("pronto (reload " + DateTime.Now.ToString("HH:mm:ss") + ")");
        }

        static void Status(string s)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(StatusFile, s + "\nplaying=" + Application.isPlaying + "\n"); } catch { }
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < s_NextPoll) return;
            s_NextPoll = EditorApplication.timeSinceStartup + 0.5;
            if (!File.Exists(CmdFile)) return;
            string cmd;
            try { cmd = File.ReadAllText(CmdFile).Trim(); File.Delete(CmdFile); } catch { return; }
            switch (cmd)
            {
                case "refresh": Status("a compilar"); AssetDatabase.Refresh(); break;
                case "play": if (!Application.isPlaying) EditorApplication.isPlaying = true; Status("play"); break;
                case "stop": if (Application.isPlaying) EditorApplication.isPlaying = false; Status("stop"); break;
                case "run": Run(); break;
                default: Status("ordem desconhecida: " + cmd); break;
            }
        }

        // ------------------------------------------------------------------ variantes

        class Variant
        {
            public string name = "atual";
            public Vector3 head, hand, grip, reul, leul, gunpos, guneul, elbr, elbl, wrist;
            public bool fixedPose = true;
            public float scale = 0.8f, speed;
            public bool sprint, shoot;
        }

        static Vector3 V(string s)
        {
            var p = s.Split(',');
            return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));
        }

        static List<Variant> LoadVariants()
        {
            var list = new List<Variant>();
            var baseV = new Variant
            {
                head = ViewModelRig.ModelHeadOffset, hand = FirstPersonArms.AnimatedHandOffset, grip = ModelWeaponHolder.GripFromWrist,
                reul = ModelWeaponHolder.RightHandEuler, leul = ModelWeaponHolder.LeftHandEuler,
                gunpos = FirstPersonArms.GunPos, guneul = FirstPersonArms.GunEuler, elbr = FirstPersonArms.ElbowHintR, elbl = FirstPersonArms.ElbowHintL, wrist = FirstPersonArms.WristFromGrip,
                fixedPose = FirstPersonArms.FixedGunPose,
            };
            var file = Path.Combine(Dir, "fp_params.txt");
            if (!File.Exists(file)) { list.Add(baseV); return list; }
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var v = new Variant { head = baseV.head, hand = baseV.hand, grip = baseV.grip, reul = baseV.reul, leul = baseV.leul,
                    gunpos = baseV.gunpos, guneul = baseV.guneul, elbr = baseV.elbr, elbl = baseV.elbl, wrist = baseV.wrist, fixedPose = baseV.fixedPose };
                var parts = line.Split('|');
                v.name = parts[0].Trim();
                for (int i = 1; i < parts.Length; i++)
                {
                    var kv = parts[i].Split('=');
                    if (kv.Length != 2) continue;
                    string k = kv[0].Trim(), val = kv[1].Trim();
                    switch (k)
                    {
                        case "head": v.head = V(val); break;
                        case "hand": v.hand = V(val); break;
                        case "grip": v.grip = V(val); break;
                        case "reul": v.reul = V(val); break;
                        case "leul": v.leul = V(val); break;
                        case "scale": v.scale = float.Parse(val, CultureInfo.InvariantCulture); break;
                        case "speed": v.speed = float.Parse(val, CultureInfo.InvariantCulture); break;
                        case "sprint": v.sprint = val == "1"; break;
                        case "shoot": v.shoot = val == "1"; break;
                        case "gunpos": v.gunpos = V(val); break;
                        case "guneul": v.guneul = V(val); break;
                        case "elbr": v.elbr = V(val); break;
                        case "elbl": v.elbl = V(val); break;
                        case "wrist": v.wrist = V(val); break;
                        case "fixed": v.fixedPose = val == "1"; break;
                    }
                }
                list.Add(v);
            }
            return list;
        }

        [MenuItem("FPS-LAN/Debug: fotos da 1.ª pessoa do Lince", priority = 64)]
        static void Run()
        {
            if (!Application.isPlaying) { Status("erro: não está em Play"); Debug.LogWarning("[FP] Entra em Play primeiro."); return; }
            if (s_Running) { Status("ocupado"); return; }
            BrawlerDefinition def = null;
            foreach (var b in BrawlerRegistry.All) if (b != null && b.brawlerId == "sniper") def = b;
            if (def == null) { Status("erro: sem sniper"); return; }
            Directory.CreateDirectory(Dir);
            foreach (var old in Directory.GetFiles(Dir, "fp_*.png")) File.Delete(old);
            var variants = LoadVariants();
            var keep = (ViewModelRig.ModelHeadOffset, FirstPersonArms.AnimatedHandOffset, ModelWeaponHolder.GripFromWrist,
                ModelWeaponHolder.RightHandEuler, ModelWeaponHolder.LeftHandEuler);
            var keep2 = (FirstPersonArms.GunPos, FirstPersonArms.GunEuler, FirstPersonArms.ElbowHintR, FirstPersonArms.ElbowHintL, FirstPersonArms.FixedGunPose, FirstPersonArms.WristFromGrip);

            var camGo = new GameObject("[FPCapture]");
            camGo.transform.position = new Vector3(0f, 400f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 85f;
            cam.nearClipPlane = 0.02f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.6f, 0.68f);
            cam.enabled = false;
            var shotGo = new GameObject("[FPShot]");
            var shot = shotGo.AddComponent<Camera>();
            shot.CopyFrom(cam);
            shot.enabled = false;
            var lightGo = new GameObject("[FPLight]");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            int vi = -1;
            ViewModelRig rig = null;
            double start = 0, last = EditorApplication.timeSinceStartup;
            bool shotFired = false;
            var sb = new StringBuilder();
            s_Running = true;

            void Next()
            {
                if (rig != null) { rig.Destroy(); rig = null; }
                vi++;
                if (vi >= variants.Count)
                {
                    EditorApplication.update -= Tick;
                    (ViewModelRig.ModelHeadOffset, FirstPersonArms.AnimatedHandOffset, ModelWeaponHolder.GripFromWrist,
                        ModelWeaponHolder.RightHandEuler, ModelWeaponHolder.LeftHandEuler) = keep;
                    (FirstPersonArms.GunPos, FirstPersonArms.GunEuler, FirstPersonArms.ElbowHintR, FirstPersonArms.ElbowHintL, FirstPersonArms.FixedGunPose, FirstPersonArms.WristFromGrip) = keep2;
                    Object.Destroy(camGo); Object.Destroy(shotGo); Object.Destroy(lightGo);
                    File.WriteAllText(Path.Combine(Dir, "fp_report.txt"), sb.ToString());
                    s_Running = false;
                    Status("feito: " + variants.Count + " variantes");
                    return;
                }
                var v = variants[vi];
                ViewModelRig.ModelHeadOffset = v.head;
                FirstPersonArms.AnimatedHandOffset = v.hand;
                ModelWeaponHolder.GripFromWrist = v.grip;
                ModelWeaponHolder.RightHandEuler = v.reul;
                ModelWeaponHolder.LeftHandEuler = v.leul;
                FirstPersonArms.GunPos = v.gunpos; FirstPersonArms.GunEuler = v.guneul;
                FirstPersonArms.ElbowHintR = v.elbr; FirstPersonArms.ElbowHintL = v.elbl;
                FirstPersonArms.FixedGunPose = v.fixedPose;
                FirstPersonArms.WristFromGrip = v.wrist;
                rig = ViewModelRig.Build(def, cam.transform, 0);
                foreach (var t in rig.Root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Sniper") { t.localScale = Vector3.one * v.scale; break; }
                start = EditorApplication.timeSinceStartup;
                shotFired = false;
                Status("a fotografar " + (vi + 1) + "/" + variants.Count + ": " + v.name);
            }

            void Tick()
            {
                if (!Application.isPlaying || camGo == null) { EditorApplication.update -= Tick; s_Running = false; Status("interrompido"); return; }
                double now = EditorApplication.timeSinceStartup;
                float dt = Mathf.Clamp((float)(now - last), 0f, 0.1f); last = now;
                if (rig == null) { Next(); return; }
                var v = variants[vi];
                float t = (float)(now - start);
                if (v.shoot && !shotFired && t >= 1.44f) { shotFired = true; rig.OnAttack(false); rig.OnShot(0); }
                rig.Tick(dt, new ViewFrame { speed = v.speed, grounded = true, sprinting = v.sprint });
                if (t < 1.5f) return;
                var c = cam.transform;
                string n = "fp_" + vi.ToString("00") + "_" + v.name;
                Shoot(shot, c.position, c.rotation, n + "_eye.png");
                var focus = c.position + c.forward * 0.3f - c.up * 0.2f;
                var sidePos = c.position + c.right * 0.9f + c.forward * 0.3f - c.up * 0.15f;
                Shoot(shot, sidePos, Quaternion.LookRotation(focus - sidePos, c.up), n + "_side.png");
                var leftPos = c.position - c.right * 0.9f + c.forward * 0.35f - c.up * 0.1f;
                Shoot(shot, leftPos, Quaternion.LookRotation(focus - leftPos, c.up), n + "_left.png");
                Transform gunT = null, supT = null;
                foreach (var tr in rig.Root.GetComponentsInChildren<Transform>(true))
                {
                    if (tr.name == "Sniper" && gunT == null) gunT = tr;
                    if (tr.name == "Support") supT = tr;
                }
                if (gunT != null)
                {
                    var p1 = gunT.position + c.right * 0.35f + c.up * 0.06f - c.forward * 0.05f;
                    Shoot(shot, p1, Quaternion.LookRotation(gunT.position - p1, c.up), n + "_maoD.png");
                }
                if (supT != null)
                {
                    var p2 = supT.position - c.right * 0.3f - c.up * 0.12f;
                    Shoot(shot, p2, Quaternion.LookRotation(supT.position - p2, c.up), n + "_maoE.png");
                }
                sb.AppendLine(v.name + ": " + Measure(rig, c));
                Next();
            }
            EditorApplication.update += Tick;
        }

        static string Measure(ViewModelRig rig, Transform cam)
        {
            var anim = rig.Root.GetComponentInChildren<Animator>();
            Transform gun = null, support = null, muzzle = null;
            foreach (var t in rig.Root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Sniper" && gun == null) gun = t;
                if (t.name == "Support") support = t;
                if (t.name == "Muzzle" && muzzle == null) muzzle = t;
            }
            var sb = new StringBuilder();
            if (gun != null)
            {
                sb.Append("punho(cam)=").Append(cam.InverseTransformPoint(gun.position).ToString("F3"));
                sb.Append(" ang(arma,mira)=").Append(Vector3.Angle(gun.forward, cam.forward).ToString("0.0"));
            }
            if (muzzle != null) sb.Append(" boca(cam)=").Append(cam.InverseTransformPoint(muzzle.position).ToString("F3"));
            if (support != null) sb.Append(" apoio(cam)=").Append(cam.InverseTransformPoint(support.position).ToString("F3"));
            if (anim != null && anim.isHuman)
            {
                var hr = anim.GetBoneTransform(HumanBodyBones.RightHand);
                var hl = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                if (hr != null) sb.Append(" pulsoD(cam)=").Append(cam.InverseTransformPoint(hr.position).ToString("F3"));
                if (hl != null) sb.Append(" pulsoE(cam)=").Append(cam.InverseTransformPoint(hl.position).ToString("F3"));
                if (hr != null && gun != null) sb.Append(" |pulsoD-punho|=").Append(Vector3.Distance(hr.position, gun.position).ToString("0.000"));
                if (hl != null && support != null) sb.Append(" |pulsoE-apoio|=").Append(Vector3.Distance(hl.position, support.position).ToString("0.000"));
                if (head != null) sb.Append(" cabeça(cam)=").Append(cam.InverseTransformPoint(head.position).ToString("F3"));
                foreach (var side in new[] { ("D", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
                                             ("E", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand) })
                {
                    var u = anim.GetBoneTransform(side.Item2); var l = anim.GetBoneTransform(side.Item3); var h = anim.GetBoneTransform(side.Item4);
                    if (u == null || l == null || h == null) continue;
                    sb.Append(" ombro" + side.Item1 + "(cam)=").Append(cam.InverseTransformPoint(u.position).ToString("F3"));
                    sb.Append(" braço" + side.Item1 + "=").Append((Vector3.Distance(u.position, l.position) + Vector3.Distance(l.position, h.position)).ToString("0.000"));
                    sb.Append(" ombro-pulso" + side.Item1 + "=").Append(Vector3.Distance(u.position, h.position).ToString("0.000"));
                }
            }
            return sb.ToString();
        }

        static void Shoot(Camera cam, Vector3 pos, Quaternion rot, string file)
        {
            cam.transform.SetPositionAndRotation(pos, rot);
            var rt = new RenderTexture(800, 450, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(800, 450, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 800, 450), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(Dir, file), tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
