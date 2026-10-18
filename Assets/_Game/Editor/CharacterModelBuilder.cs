using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Monta um personagem com modelo + animações para substituir o boneco de primitivas de um brawler.
    ///
    /// Como usar: pôr numa pasta UM FBX com skin e rig humanoide (Mixamo, Meshy…), os FBX das animações
    /// (só esqueleto; o nome diz o papel: Idle, Walk, Run, Jump, Death, Hurt, Attack — ex.: "Lince@Run.fbx")
    /// e a textura base (*diffuse*.png). As armas são as de primitivas, seguradas por IK (ModelWeaponHolder).
    /// O menu configura a importação Humanoid, cria o Animator Controller (locomoção, salto, ataque/dano por cima
    /// do tronco, morte), o material cartoon e grava o prefab em Resources/Characters/&lt;brawlerId&gt;.prefab,
    /// que o CharacterRig passa a usar automaticamente.
    /// </summary>
    public static class CharacterModelBuilder
    {
        public const string MsFolder = "Assets/_Game/Art/Characters/MrS";
        public const string MsBrawlerId = "sprayer";   // MR.S joga como o brawler "sprayer" (ver s_Roster no menu)

        public const string LinceFolder = "Assets/_Game/Art/Characters/Lince";
        public const string LinceBrawlerId = "sniper";

        [MenuItem("FPS-LAN/Personagens: montar MR.S (modelo + animações)", priority = 30)]
        static void BuildMsMenu() => RosterModelBuilder.BuildMrS();

        [MenuItem("FPS-LAN/Personagens: montar Lince (modelo + animações)", priority = 31)]
        static void BuildLinceMenu() => Report(Build(LinceBrawlerId, LinceFolder, "Lince"));

        [MenuItem("FPS-LAN/Personagens: voltar ao boneco de primitivas (Lince)", priority = 40)]
        static void RemoveLinceMenu()
        {
            var path = "Assets/_Game/Resources/Characters/" + LinceBrawlerId + ".prefab";
            Report(AssetDatabase.DeleteAsset(path) ? "Removido " + path + ": o Lince volta ao boneco de primitivas." : "Não havia " + path);
        }

        [MenuItem("FPS-LAN/Personagens: gerar braços da 1.ª pessoa (Lince)", priority = 32)]
        static void BuildLinceArmsMenu() => Report(BuildFirstPersonArms(LinceBrawlerId, LinceFolder, "Lince"));

        /// <summary>
        /// Braços para a vista em 1.ª pessoa: o prefab do modelo com uma malha que só tem os braços e as mãos
        /// (triângulos cujos vértices pesam sobretudo nos ossos do braço, antebraço, mão e dedos).
        /// Grava Resources/Characters/&lt;brawlerId&gt;_fparms.prefab, que o ViewModelRig usa automaticamente.
        /// </summary>
        public static string BuildFirstPersonArms(string brawlerId, string folder, string prefix)
        {
            const string res = "Assets/_Game/Resources/Characters";
            var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(res + "/" + brawlerId + ".prefab");
            if (srcPrefab == null) return "Primeiro monta o personagem (falta " + res + "/" + brawlerId + ".prefab).";
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(srcPrefab);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = prefix + "_FPArms";
            var anim = inst.GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman) { Object.DestroyImmediate(inst); return "O modelo não tem Animator humanoide."; }

            // ossos dos braços: braço, antebraço, mão e todos os dedos
            var arm = new HashSet<Transform>();
            foreach (var hb in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
            {
                var t = anim.GetBoneTransform(hb);
                if (t != null) foreach (var c in t.GetComponentsInChildren<Transform>(true)) arm.Add(c);
            }

            int kept = 0, total = 0;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var src = smr.sharedMesh;
                if (src == null) continue;
                var bones = smr.bones;
                var isArm = new bool[bones.Length];
                for (int i = 0; i < bones.Length; i++) isArm[i] = bones[i] != null && arm.Contains(bones[i]);
                var bw = src.boneWeights;
                var armW = new float[bw.Length];
                for (int v = 0; v < bw.Length; v++)
                {
                    var w = bw[v];
                    float a = 0f;
                    if (isArm[w.boneIndex0]) a += w.weight0;
                    if (isArm[w.boneIndex1]) a += w.weight1;
                    if (isArm[w.boneIndex2]) a += w.weight2;
                    if (isArm[w.boneIndex3]) a += w.weight3;
                    armW[v] = a;
                }
                var mesh = Object.Instantiate(src);
                mesh.name = prefix + "_FPArms";
                for (int sm = 0; sm < src.subMeshCount; sm++)
                {
                    var tris = src.GetTriangles(sm);
                    var keep = new List<int>(tris.Length / 4);
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        total++;
                        if (armW[tris[t]] >= 0.5f && armW[tris[t + 1]] >= 0.5f && armW[tris[t + 2]] >= 0.5f)
                        {
                            keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]);
                            kept++;
                        }
                    }
                    mesh.SetTriangles(keep, sm);
                }
                var meshPath = folder + "/" + prefix + "_FPArms.asset";
                AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath);
                smr.sharedMesh = mesh;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.updateWhenOffscreen = true;
            }

            var path = res + "/" + brawlerId + ViewModelRig.FirstPersonArmsSuffix + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(inst, path);
            Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
            return "Braços da 1.ª pessoa: " + path + "\nTriângulos: " + kept + " de " + total;
        }

        static void Report(string report)
        {
            Debug.Log("[Personagens] " + report);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("FPS-LAN", report, "OK");
        }

        public static string Build(string brawlerId, string folder, string prefix,
            IReadOnlyDictionary<string, AnimationClip> fallbackClips = null, string texturePath = null)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return "Pasta não encontrada: " + folder;
            var fbxs = Directory.GetFiles(folder, "*.fbx").Select(p => p.Replace('\\', '/')).ToList();
            if (fbxs.Count == 0) return "Não há FBX em " + folder;

            // ---------------- 1. modelo com skin (cria o Avatar)
            string modelPath = null;
            foreach (var p in fbxs)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                var smr = go != null ? go.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
                if (smr != null && smr.sharedMesh != null && smr.sharedMesh.vertexCount > 100) { modelPath = p; break; }
            }
            if (modelPath == null) return "Nenhum FBX com skin (descarrega uma animação 'With Skin' no Mixamo).";

            var mi = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            // o FBX do modelo só traz animação se o nome disser qual (ex.: "Breathing Idle.fbx" do Mixamo)
            mi.importAnimation = RoleFor(Path.GetFileNameWithoutExtension(modelPath)) != null;
            ConfigureClips(mi, Path.GetFileNameWithoutExtension(modelPath));
            mi.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman) return "O Avatar Humanoid não foi criado (o rig do Mixamo não foi reconhecido).";

            // ---------------- 2. animações (copiam o Avatar do modelo)
            // nome do osso da anca do modelo: animações com o mesmo esqueleto copiam o Avatar do modelo;
            // as de outro esqueleto (ex.: Mixamo) criam o seu e o Unity converte entre humanoides.
            string hipsName = null;
            foreach (var hb in avatar.humanDescription.human) if (hb.humanName == "Hips") hipsName = hb.boneName;
            foreach (var p in fbxs)
            {
                if (p == modelPath) continue;
                var ai = (ModelImporter)AssetImporter.GetAtPath(p);
                ai.animationType = ModelImporterAnimationType.Human;
                bool sameSkeleton = false;
                var animGo = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (animGo != null && hipsName != null)
                    foreach (var t in animGo.GetComponentsInChildren<Transform>(true)) if (t.name == hipsName) { sameSkeleton = true; break; }
                if (sameSkeleton)
                {
                    ai.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    ai.sourceAvatar = avatar;
                }
                else
                {
                    ai.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    ai.sourceAvatar = null;
                }
                ai.materialImportMode = ModelImporterMaterialImportMode.None;
                ConfigureClips(ai, Path.GetFileNameWithoutExtension(p));
                ai.SaveAndReimport();
            }

            // ---------------- 3. clips por papel (pelo nome do ficheiro)
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var p in fbxs)
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null) continue;
                var role = RoleFor(Path.GetFileNameWithoutExtension(p));
                if (role != null && !clips.ContainsKey(role)) clips[role] = clip;
            }
            if (fallbackClips != null)
                foreach (var entry in fallbackClips)
                    if (!clips.ContainsKey(entry.Key) && entry.Value != null) clips.Add(entry.Key, entry.Value);
            if (!clips.ContainsKey("idle"))
            {
                var diag = new System.Text.StringBuilder();
                foreach (var p in fbxs)
                {
                    var all = AssetDatabase.LoadAllAssetsAtPath(p);
                    diag.Append(Path.GetFileName(p)).Append(": role=").Append(RoleFor(Path.GetFileNameWithoutExtension(p)) ?? "-")
                        .Append(" clips=").Append(string.Join(",", all.OfType<AnimationClip>().Select(c => c.name)))
                        .Append(" assets=").Append(all.Length).Append("\n");
                }
                Debug.LogWarning("[Personagens] " + diag);
                return "Falta uma animação parada (nome com 'Idle').\n" + diag;
            }

            // ---------------- 4. Animator Controller
            var ctrlPath = folder + "/" + prefix + "_Animator.controller";
            AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Super", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
            // animações que já seguram uma espingarda (ex.: Mixamo "Rifle ..."): a arma passa a seguir as mãos
            if (clips.TryGetValue("idle", out var idleClip) && AssetDatabase.GetAssetPath(idleClip).ToLowerInvariant().Contains("rifle"))
                ctrl.AddParameter(ModelWeaponHolder.HoldsWeaponParam, AnimatorControllerParameterType.Bool);
            ctrl.parameters = ctrl.parameters.Select(p => { if (p.name == "Grounded") p.defaultBool = true; return p; }).ToArray();

            var baseSm = ctrl.layers[0].stateMachine;
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            // velocidades do jogo: andar 6,5 m/s, correr (sprint) 9,5 m/s
            tree.AddChild(clips["idle"], 0f);
            if (clips.TryGetValue("walk", out var walk)) tree.AddChild(walk, 2.2f);
            if (clips.TryGetValue("run", out var run))
            {
                tree.AddChild(run, 6.5f);
                tree.AddChild(run, 9.5f);
                var kids = tree.children;
                kids[kids.Length - 1].timeScale = 1.35f;   // sprint: o mesmo ciclo mais rápido
                tree.children = kids;
            }
            baseSm.defaultState = loco;
            loco.writeDefaultValues = false;
            loco.iKOnFeet = true;

            // IK na camada base: as mãos seguram a arma (ModelWeaponHolder)
            var baseLayers = ctrl.layers;
            baseLayers[0].iKPass = true;
            ctrl.layers = baseLayers;

            if (clips.TryGetValue("jump", out var jump))
            {
                var js = baseSm.AddState("Jump");
                js.motion = jump; js.writeDefaultValues = false;
                var toJump = loco.AddTransition(js);
                toJump.hasExitTime = false; toJump.duration = 0.12f;
                toJump.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
                var back = js.AddTransition(loco);
                back.hasExitTime = false; back.duration = 0.15f;
                back.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            }

            if (clips.TryGetValue("death", out var death))
            {
                var ds = baseSm.AddState("Dead");
                ds.motion = death; ds.writeDefaultValues = false;
                var toDead = baseSm.AddAnyStateTransition(ds);
                toDead.hasExitTime = false; toDead.duration = 0.1f; toDead.canTransitionToSelf = false;
                toDead.AddCondition(AnimatorConditionMode.If, 0, "Dead");
                var revive = ds.AddTransition(loco);
                revive.hasExitTime = false; revive.duration = 0.05f;
                revive.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            }

            // Animações com espingarda: o tronco e os braços ficam sempre na pose de mira (idle) e só as pernas
            // andam/correm — a arma aponta sempre para a frente, como nos jogos de tiro na 3.ª pessoa.
            bool holdsWeapon = false;   // desativado: torcia o tronco nas animações de locomoção
            if (holdsWeapon)
            {
                var aimMask = new AvatarMask();
                for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                {
                    var part = (AvatarMaskBodyPart)i;
                    aimMask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                        part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                        part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers);
                }
                var aimMaskPath = folder + "/" + prefix + "_AimUpper.mask";
                AssetDatabase.DeleteAsset(aimMaskPath);
                AssetDatabase.CreateAsset(aimMask, aimMaskPath);
                ctrl.AddLayer("AimUpper");
                var ls = ctrl.layers;
                var li = ls.Length - 1;
                ls[li].avatarMask = aimMask;
                ls[li].defaultWeight = 1f;
                ls[li].blendingMode = AnimatorLayerBlendingMode.Override;
                ctrl.layers = ls;
                var aimSm = ctrl.layers[li].stateMachine;
                var aimState = aimSm.AddState("Aim");
                aimState.motion = clips["idle"];
                aimState.writeDefaultValues = false;
                aimSm.defaultState = aimState;
                // morto: o tronco volta a seguir a animação de morte
                var free = aimSm.AddState("Free");
                free.writeDefaultValues = false;
                var toFree = aimState.AddTransition(free);
                toFree.hasExitTime = false; toFree.duration = 0.1f;
                toFree.AddCondition(AnimatorConditionMode.If, 0, "Dead");
                var back = free.AddTransition(aimState);
                back.hasExitTime = false; back.duration = 0.05f;
                back.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            }

            // camada do tronco: ataque/dano por cima da corrida
            if (clips.ContainsKey("attack") || clips.ContainsKey("hurt"))
            {
                var mask = new AvatarMask();
                for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                {
                    var part = (AvatarMaskBodyPart)i;
                    bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                                 part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                                 part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
                    mask.SetHumanoidBodyPartActive(part, upper);
                }
                var maskPath = folder + "/" + prefix + "_UpperBody.mask";
                AssetDatabase.DeleteAsset(maskPath);
                AssetDatabase.CreateAsset(mask, maskPath);

                ctrl.AddLayer("UpperBody");
                var layers = ctrl.layers;
                int ui = layers.Length - 1;
                layers[ui].avatarMask = mask;
                layers[ui].defaultWeight = 1f;
                layers[ui].blendingMode = AnimatorLayerBlendingMode.Override;
                ctrl.layers = layers;
                var up = ctrl.layers[ui].stateMachine;
                var empty = up.AddState("Empty");
                empty.writeDefaultValues = false;
                up.defaultState = empty;

                void Overlay(string stateName, AnimationClip clip, params string[] triggers)
                {
                    var st = up.AddState(stateName);
                    st.motion = clip; st.writeDefaultValues = false;
                    foreach (var trig in triggers)
                    {
                        var t = up.AddAnyStateTransition(st);
                        t.hasExitTime = false; t.duration = 0.06f; t.canTransitionToSelf = true;
                        t.AddCondition(AnimatorConditionMode.If, 0, trig);
                    }
                    var exit = st.AddTransition(empty);
                    exit.hasExitTime = true; exit.exitTime = 0.85f; exit.duration = 0.15f;
                }
                if (clips.TryGetValue("attack", out var attack)) Overlay("Attack", attack, "Attack", "Super");
                if (clips.TryGetValue("hurt", out var hurt)) Overlay("Hurt", hurt, "Hurt");
            }
            EditorUtility.SetDirty(ctrl);

            // ---------------- 5. material cartoon com a textura
            var tex = texturePath ?? Directory.GetFiles(folder, "*.png").Select(p => p.Replace('\\', '/'))
                .FirstOrDefault(p => p.ToLowerInvariant().Contains("diffuse") || p.ToLowerInvariant().Contains("albedo") || p.ToLowerInvariant().Contains("base"));
            var matPath = folder + "/" + prefix + "_Toon.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            var shader = Shader.Find("FPSLAN/ToonOutline") ?? Shader.Find("Standard");
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader;
            if (tex != null) mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            if (mat.HasProperty("_Color")) mat.color = Color.white;
            if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0.0022f);
            if (mat.HasProperty("_OutlineColor")) mat.SetColor("_OutlineColor", new Color(0.06f, 0.05f, 0.1f));
            if (mat.HasProperty("_RimColor")) mat.SetColor("_RimColor", new Color(1f, 1f, 1f, 0.25f));
            if (mat.HasProperty("_VertexColor")) mat.SetFloat("_VertexColor", 0f);
            EditorUtility.SetDirty(mat);

            // ---------------- 6. prefab em Resources/Characters/<brawlerId>
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = prefix + "_Model";
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();

            // altura igual à do personagem de primitivas
            var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var s in smrs)
            {
                var baked = new Mesh();
                s.BakeMesh(baked, true);
                foreach (var v in baked.vertices)
                {
                    float y = s.transform.TransformPoint(v).y;
                    top = Mathf.Max(top, y); bottom = Mathf.Min(bottom, y);
                }
                Object.DestroyImmediate(baked);
            }
            float height = Mathf.Max(0.01f, top - Mathf.Min(0f, bottom));
            inst.transform.localScale = Vector3.one * (CharacterRig.ModelTargetHeight / height);

            const string res = "Assets/_Game/Resources/Characters";
            if (!AssetDatabase.IsValidFolder(res)) AssetDatabase.CreateFolder("Assets/_Game/Resources", "Characters");
            var prefabPath = res + "/" + brawlerId + ".prefab";
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            // garantia: o prefab tem de ficar com o Animator Controller (o Unity às vezes perde a referência
            // a um controller acabado de recriar no mesmo caminho)
            using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                var pa = scope.prefabContentsRoot.GetComponentInChildren<Animator>();
                if (pa != null && pa.runtimeAnimatorController == null)
                    pa.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ctrlPath);
            }
            Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
            var armsReport = BuildFirstPersonArms(brawlerId, folder, prefix);

            return "Pronto: " + prefabPath + "\nModelo: " + Path.GetFileName(modelPath) +
                   "\nAnimações: " + string.Join(", ", clips.Select(kv => kv.Key + "=" + kv.Value.name + "(" + Path.GetFileName(AssetDatabase.GetAssetPath(kv.Value)) + ")")) +
                   "\nAltura original: " + height.ToString("0.00") + " m → " + CharacterRig.ModelTargetHeight + " m" +
                   "\n" + armsReport;
        }

        static void ConfigureClips(ModelImporter mi, string fileName)
        {
            var role = RoleFor(fileName);
            if (role == null) return;
            bool loop = role == "idle" || role == "run" || role == "walk";
            var defaults = mi.defaultClipAnimations;
            if (defaults == null || defaults.Length == 0) return;
            foreach (var c in defaults)
            {
                c.name = role;
                c.loopTime = loop;
                c.loopPose = loop;
                // tudo "baked into pose": o personagem não se desloca nem roda por causa da animação
                c.lockRootRotation = true;
                // locomoção: o corpo fica virado para a frente (as animações "Rifle" do Mixamo andam de lado);
                // parado/ataque mantêm a orientação original (pose de mira com o tronco rodado)
                c.keepOriginalOrientation = !(role == "walk" || role == "run" || role == "jump");
                c.lockRootHeightY = role != "jump";
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = defaults;
        }

        /// <summary>Papel da animação pelo nome do ficheiro (null = não é uma animação conhecida).</summary>
        static string RoleFor(string fileName)
        {
            var n = fileName.ToLowerInvariant();
            int at = n.IndexOf('@');
            if (at >= 0) n = n.Substring(at + 1);   // "Lince@Run" -> "run"
            if (n.Contains("idle")) return "idle";
            if (n.Contains("run") || n.Contains("sprint")) return "run";
            if (n.Contains("walk")) return "walk";
            if (n.Contains("jump")) return "jump";
            if (n.Contains("dying") || n.Contains("death") || n.Contains("die")) return "death";
            if (n.Contains("hurt") || n.Contains("hit") || n.Contains("reaction") || n.Contains("impact") || n.Contains("damage")) return "hurt";
            if (n.Contains("attack") || n.Contains("shoot") || n.Contains("fire") || n.Contains("throw") || n.Contains("punch")) return "attack";
            return null;
        }
    }
}
