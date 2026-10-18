using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using FPSLAN;

public static class LinceVisualValidation
{
    public static async System.Threading.Tasks.Task<string> Main()
    {
        if (!Application.isPlaying) throw new Exception("Enter Play mode first.");
        string dir = Path.GetFullPath("Temp/lince-validation");
        Directory.CreateDirectory(dir);
        var report = new StringBuilder();
        foreach (string role in new[] { "RifleIdle", "RifleWalk", "RifleRun", "RifleJump", "AttackRifle" })
        {
            string path = "Assets/_Game/Art/Characters/Lince/Lince@" + role + ".fbx";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            if (asset.GetComponentsInChildren<Renderer>(true).Length != 0) throw new Exception("Animation includes mesh: " + role);
            if (!clip.humanMotion) throw new Exception("Not humanoid: " + role);
            report.AppendLine(role + ": humanoid, no mesh, length=" + clip.length);
        }
        var def = BrawlerRegistry.All.First(b => b.brawlerId == "sniper");
        var stage = new GameObject("[LinceValidation]");
        stage.transform.position = new Vector3(300, 100, 300);
        var camGo = new GameObject("ValidationCamera");
        camGo.transform.SetParent(stage.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.14f, 0.18f, 0.23f);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 20;
        var lamp = new GameObject("ValidationLight");
        lamp.transform.SetParent(stage.transform, false);
        lamp.transform.rotation = Quaternion.Euler(35, -35, 0);
        var light = lamp.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f;
        try
        {
            var rig = CharacterRig.Build(def, stage.transform, ShadowCastingMode.On, 0);
            var anim = rig.Root.GetComponentInChildren<Animator>();
            var weapon = rig.Root.Find("WeaponR");
            var support = weapon.GetComponentsInChildren<Transform>().First(t => t.name == "Support");
            foreach (string state in new[] { "idle", "walk", "run", "sprint", "jump", "attack", "hurt", "pitchUp", "pitchDown", "death" })
            {
                float speed = state == "walk" ? 2.2f : state == "run" ? 6.5f : state == "sprint" ? 9.5f : 0;
                float pitch = state == "pitchUp" ? -50 : state == "pitchDown" ? 50 : 0;
                var frame = new RigFrame { grounded = state != "jump", worldVelocity = Vector3.forward * speed, pitch = pitch };
                for (int i = 0; i < 40; i++) { rig.Tick(1f / 60, frame); anim.Update(1f / 60); }
                if (state == "attack") rig.OnAttack(false, 0.3f);
                if (state == "hurt") rig.OnHurt();
                if (state == "death") rig.SetDead(true, false);
                float worstLeft = 0, worstRight = 0;
                for (int i = 0; i < 80; i++)
                {
                    rig.Tick(1f / 60, frame); anim.Update(1f / 60);
                    worstLeft = Mathf.Max(worstLeft, Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.LeftHand).position, support.position));
                    worstRight = Mathf.Max(worstRight, Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position, weapon.position));
                    if (i == 2 || i == 40)
                    {
                        cam.fieldOfView = 30;
                        var center = stage.transform.position + Vector3.up * 0.95f;
                        cam.transform.position = center + new Vector3(4.2f, 0.2f, 2.0f);
                        cam.transform.LookAt(center);
                        Save(cam, Path.Combine(dir, state + "_" + i + ".png"), 720, 720);
                    }
                }
                if (state != "death" && (worstLeft > 0.02f || worstRight > 0.09f || !weapon.gameObject.activeInHierarchy)) throw new Exception("Weapon grip failed in " + state);
                report.AppendLine(state + ": left-support=" + worstLeft.ToString("F3") + ", right-grip=" + worstRight.ToString("F3") + ", weapon=" + weapon.gameObject.activeInHierarchy + ", state=" + anim.GetCurrentAnimatorStateInfo(0).shortNameHash);
            }
            UnityEngine.Object.DestroyImmediate(rig.Root.gameObject);
            cam.transform.localPosition = Vector3.zero; cam.transform.localRotation = Quaternion.identity; cam.fieldOfView = 85;
            var vm = ViewModelRig.Build(def, cam.transform, 0);
            await System.Threading.Tasks.Task.Yield(); var arms = vm.Root.GetComponentInChildren<FirstPersonArms>();
            var fpAnim = vm.Root.GetComponentInChildren<Animator>();
            foreach (string state in new[] { "fp_idle", "fp_run", "fp_fire", "fp_melee" })
            {
                if (state == "fp_fire") vm.OnShot(0);
                if (state == "fp_melee") vm.OnMelee(0.3f);
                var frame = new ViewFrame { grounded = true, sprinting = state == "fp_run", speed = state == "fp_run" ? 9.5f : 0 };
                for (int i = 0; i < (state == "fp_idle" || state == "fp_run" ? 45 : 5); i++) { vm.Tick(1f / 60, frame); fpAnim.Update(1f / 60); }
                Save(cam, Path.Combine(dir, state + ".png"), 1280, 720);
                report.AppendLine(state + ": drivenWeapon=" + (arms.drivenWeapon != null) + ", left-support=" + Vector3.Distance(fpAnim.GetBoneTransform(HumanBodyBones.LeftHand).position, arms.drivenWeapon.TransformPoint(arms.supportLocal)).ToString("F3"));
            }

            File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
            return report.ToString();
        }
        finally { UnityEngine.Object.DestroyImmediate(stage); }
    }

    static void Save(Camera cam, string file, int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 24);
        var old = RenderTexture.active;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        try { cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, width, height), 0, 0); tex.Apply(); File.WriteAllBytes(file, tex.EncodeToPNG()); }
        finally { cam.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(tex); }
    }
}
