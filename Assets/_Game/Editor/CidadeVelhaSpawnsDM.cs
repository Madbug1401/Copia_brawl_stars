using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Cidade Velha (Pelourinho):
    /// 1) tira as faixas azul e vermelha do chão (GRD_Spawn_Blue / GRD_Spawn_Red) — os spawns das equipas do Pique-Gemas
    ///    continuam nos mesmos sítios, só deixam de estar pintados;
    /// 2) cria spawns "só Deathmatch" dentro das 10 casas (rés-do-chão e 1.º andar dos sobrados). No Deathmatch o respawn
    ///    passa a ser aleatório entre eles; nos outros modos são ignorados.
    /// Cada ponto é validado com a física do prefab: chão do piso por baixo, espaço livre para o jogador e teto por cima.
    /// Pode correr-se várias vezes (substitui o grupo "Spawns_Deathmatch").
    /// </summary>
    public static class CidadeVelhaSpawnsDM
    {
        const string PrefabPath = ProjectSetup.Root + "/Prefabs/Environment/PF_CidadeVelha.prefab";
        const string GroupName = "Spawns_Deathmatch";
        const int PerFloor = 2;            // spawns por piso de cada casa
        const float MinSeparation = 2.5f;  // entre spawns da mesma casa
        const float PlayerRadius = 0.42f;

        [MenuItem("FPS-LAN/Mapas/Cidade Velha: spawns do Deathmatch nas casas (sem faixas azul e vermelha)", priority = 46)]
        static void Menu()
        {
            var r = Apply();
            Debug.Log("[Cidade Velha] " + r);
            EditorUtility.DisplayDialog("Cidade Velha", r, "OK");
        }

        public static string Apply()
        {
            var log = new StringBuilder();
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var cen = FindDeep(root.transform, "Cenario") ?? root.transform;

                // 1) faixas pintadas dos spawns das equipas
                int removed = 0;
                foreach (var n in new[] { "GRD_Spawn_Blue", "GRD_Spawn_Red" })
                {
                    var t = FindDeep(root.transform, n);
                    if (t != null) { Object.DestroyImmediate(t.gameObject); removed++; }
                }
                log.AppendLine(removed > 0 ? $"Faixas azul/vermelha removidas ({removed})." : "Faixas azul/vermelha: já não existiam.");

                // 2) spawns do Deathmatch nos interiores
                var old = FindDeep(root.transform, GroupName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var group = new GameObject(GroupName).transform;
                group.SetParent(root.transform, false);

                Physics.SyncTransforms();
                var phys = root.scene.GetPhysicsScene();
                var byFloor = new Dictionary<string, List<(Vector3 p, Vector3 toDoor)>>();
                int tested = 0, valid = 0;
                foreach (var c in CidadeVelhaInteriores.CasaSpawnCandidates(cen))
                {
                    tested++;
                    if (!Validate(phys, c.point, c.floorY, out var ground)) continue;
                    valid++;
                    string key = c.house + "#" + c.floor;
                    if (!byFloor.TryGetValue(key, out var l)) byFloor[key] = l = new List<(Vector3, Vector3)>();
                    l.Add((ground, c.toDoor));
                }

                int made = 0;
                var houses = new HashSet<string>();
                foreach (var kv in byFloor)
                {
                    var chosen = Spread(kv.Value, PerFloor, MinSeparation);
                    string house = kv.Key.Substring(0, kv.Key.IndexOf('#'));
                    string floor = kv.Key.EndsWith("#0") ? "R" : "1";
                    for (int i = 0; i < chosen.Count; i++)
                    {
                        var go = new GameObject($"SPAWN_DM_{house}_{floor}{i}");
                        go.transform.SetParent(group, false);
                        go.transform.position = chosen[i].p + Vector3.up * 0.04f;
                        var dir = chosen[i].toDoor.sqrMagnitude > 0.01f ? chosen[i].toDoor.normalized : Vector3.forward;
                        go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                        var sp = go.AddComponent<SpawnPoint>();
                        sp.team = -1;
                        sp.deathmatchOnly = true;
                        made++;
                        houses.Add(house);
                    }
                }
                log.AppendLine($"Spawns do Deathmatch: {made} em {houses.Count} casas ({valid} de {tested} pontos da grelha válidos).");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return log.ToString().TrimEnd();
        }

        /// <summary>Chão do piso por baixo (horizontal), espaço livre para o jogador em pé e teto por cima (é interior).</summary>
        static bool Validate(PhysicsScene phys, Vector3 p, float floorY, out Vector3 ground)
        {
            ground = p;
            if (!phys.Raycast(new Vector3(p.x, floorY + 1.0f, p.z), Vector3.down, out var hit, 1.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.normal.y < 0.9f || Mathf.Abs(hit.point.y - floorY) > 0.3f) return false;
            ground = hit.point;
            var a = ground + Vector3.up * (PlayerRadius + 0.06f);
            var b = ground + Vector3.up * 1.75f;
            var cols = new Collider[4];
            if (phys.OverlapCapsule(a, b, PlayerRadius, cols, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) > 0) return false;
            // dentro de casa: há teto até 6 m acima
            if (!phys.Raycast(ground + Vector3.up * 1.8f, Vector3.up, out _, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            return true;
        }

        /// <summary>Até 'count' pontos espalhados: o mais perto do centro do piso e depois sempre o mais afastado dos já escolhidos.</summary>
        static List<(Vector3 p, Vector3 toDoor)> Spread(List<(Vector3 p, Vector3 toDoor)> pts, int count, float minSep)
        {
            var res = new List<(Vector3 p, Vector3 toDoor)>();
            if (pts.Count == 0) return res;
            var center = Vector3.zero;
            foreach (var q in pts) center += q.p;
            center /= pts.Count;
            int first = 0;
            for (int i = 1; i < pts.Count; i++)
                if ((pts[i].p - center).sqrMagnitude < (pts[first].p - center).sqrMagnitude) first = i;
            res.Add(pts[first]);
            while (res.Count < count)
            {
                int best = -1;
                float bestD = minSep;
                for (int i = 0; i < pts.Count; i++)
                {
                    float d = float.MaxValue;
                    foreach (var r in res) d = Mathf.Min(d, Vector3.Distance(r.p, pts[i].p));
                    if (d > bestD) { bestD = d; best = i; }
                }
                if (best < 0) break;
                res.Add(pts[best]);
            }
            return res;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
