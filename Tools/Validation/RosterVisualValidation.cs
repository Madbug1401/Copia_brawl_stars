using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using FPSLAN;

public static class RosterVisualValidation
{
    public static string Main()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Entrar em Play primeiro.");
        string[] ids = { "gunslinger", "artillery", "sprayer", "trapper", "shotgunner" };
        RigStyle[] styles = { RigStyle.DualSMG, RigStyle.DualExplosives, RigStyle.DualSprayCans, RigStyle.Bow, RigStyle.Shotgun };
        string dir = Path.GetFullPath("Temp/roster-validation");
        Directory.CreateDirectory(dir);
        var report = new StringBuilder();
        var sheet = new Texture2D(2000, 500, TextureFormat.RGB24, false);
        var stage = new GameObject("[RosterValidation]");
        stage.transform.position = new Vector3(300, 100, 300);
        var cameraGo = new GameObject("RosterValidationCamera");
        cameraGo.transform.SetParent(stage.transform, false);
        var cam = cameraGo.AddComponent<Camera>();
        cam.enabled = false; cam.nearClipPlane = 0.01f; cam.farClipPlane = 20;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.14f, 0.18f, 0.23f);
        var lightGo = new GameObject("RosterValidationLight");
        lightGo.transform.SetParent(stage.transform, false);
        lightGo.transform.rotation = Quaternion.Euler(35, -35, 0);
        var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 0.8f;
        try
        {
            for (int n = 0; n < ids.Length; n++)
            {
                string id = ids[n];
                var def = BrawlerRegistry.All.First(b => b.brawlerId == id);
                if (def.firstPerson.rig != styles[n] || def.thirdPerson.rig != styles[n]) throw new Exception(id + ": estilo errado.");
                var rig = CharacterRig.Build(def, stage.transform, ShadowCastingMode.On, 0);
                var anim = rig.Root.GetComponentInChildren<Animator>();
                if (anim == null || !anim.isHuman || !anim.avatar.isValid || rig.Style != styles[n]) throw new Exception(id + ": não usou o modelo humanoide correto.");
                if (WeaponModels.IsDual(styles[n]) && (rig.Root.Find("WeaponR") == null || rig.Root.Find("WeaponL") == null)) throw new Exception(id + ": falta uma das armas duplas.");
                var body = rig.Root.GetComponentInChildren<SkinnedMeshRenderer>();
                if (body.sharedMaterial.mainTexture == null) throw new Exception(id + ": sem textura.");
                foreach (string state in new[] { "idle", "walk", "run", "jump", "attack", "hurt", "death", "respawn" })
                {
                    if (state == "attack") { rig.OnAttack(false, 0.3f); rig.OnShot(0); rig.OnShot(1); }
                    if (state == "hurt") rig.OnHurt();
                    if (state == "death") rig.SetDead(true, false);
                    if (state == "respawn") rig.SetDead(false, true);
                    var frame = new RigFrame { grounded = state != "jump", worldVelocity = Vector3.forward * (state == "walk" ? 2.2f : state == "run" ? 9.5f : 0) };
                    for (int k = 0; k < (state == "attack" || state == "hurt" ? 5 : 50); k++) { rig.Tick(1f / 60, frame); anim.Update(1f / 60); }
                    if (state == "idle")
                    {
                        float height = body.bounds.size.y;
                        float skeletonHeight = Mathf.Abs(anim.GetBoneTransform(HumanBodyBones.Head).position.y - anim.GetBoneTransform(HumanBodyBones.LeftFoot).position.y);
                        report.AppendLine(def.displayName + " / " + id + " / " + rig.Style + ": height=" + height.ToString("F2") + ", material=" + body.sharedMaterial.name + ", avatar=" + anim.avatar.isValid);
                        if (height < 1.3f || height > 2.8f || skeletonHeight < 0.9f || skeletonHeight > 1.9f) throw new Exception(id + ": altura inválida " + height);
                    }
                    if (state == "idle" || state == "run" || state == "attack" || state == "death")
                    {
                        cam.fieldOfView = 32;
                        var center = stage.transform.position + Vector3.up * 0.95f;
                        cam.transform.position = center + new Vector3(2.5f, 0.15f, 4.0f);
                        cam.transform.LookAt(center);
                        var tex = Render(cam, 400, 500);
                        File.WriteAllBytes(Path.Combine(dir, id + "_" + state + ".png"), tex.EncodeToPNG());
                        if (state == "idle") sheet.SetPixels(n * 400, 0, 400, 500, tex.GetPixels());
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                }
                UnityEngine.Object.DestroyImmediate(rig.Root.gameObject);
                cam.transform.localPosition = Vector3.zero; cam.transform.localRotation = Quaternion.identity; cam.fieldOfView = 85;
                var vm = ViewModelRig.Build(def, cam.transform, 0);
                foreach (var t in vm.Root.GetComponentsInChildren<Transform>())
                    if (t.name == "Hand") t.gameObject.SetActive(false); // Remoção adiada pelo jogo; ocultar já na captura síncrona.
                var arms = vm.Root.GetComponentInChildren<FirstPersonArms>();
                var fpAnim = vm.Root.GetComponentInChildren<Animator>();
                if (WeaponModels.IsDual(styles[n]) && (arms == null || arms.rightTarget == null || arms.leftTarget == null || arms.rightTarget == arms.leftTarget)) throw new Exception(id + ": alvos das duas mãos incorretos.");
                if (arms == null || fpAnim == null || !fpAnim.isHuman) throw new Exception(id + ": faltam braços próprios.");
                foreach (string state in new[] { "fp_idle", "fp_fire", "fp_sprint" })
                {
                    if (state == "fp_fire") { vm.OnAttack(false); vm.OnShot(0); }
                    var frame = new ViewFrame { grounded = true, sprinting = state == "fp_sprint", speed = state == "fp_sprint" ? 9.5f : 0 };
                    for (int k = 0; k < (state == "fp_fire" ? 5 : 45); k++) { vm.Tick(1f / 60, frame); fpAnim.Update(1f / 60); }
                    var tex = Render(cam, 1280, 720);
                    File.WriteAllBytes(Path.Combine(dir, id + "_" + state + ".png"), tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                }
                report.AppendLine("  idle/walk/run/jump/attack/hurt/death/respawn + FP idle/fire/sprint: OK");
                UnityEngine.Object.DestroyImmediate(vm.Root.gameObject);
            }
            sheet.Apply(); File.WriteAllBytes(Path.Combine(dir, "elenco.png"), sheet.EncodeToPNG());
            File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
            return report.ToString();
        }
        finally { UnityEngine.Object.DestroyImmediate(sheet); UnityEngine.Object.DestroyImmediate(stage); }
    }

    static Texture2D Render(Camera cam, int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 24);
        var old = RenderTexture.active;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        try { cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, width, height), 0, 0); tex.Apply(); return tex; }
        finally { cam.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); }
    }
}
