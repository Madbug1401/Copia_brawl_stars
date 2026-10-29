using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Verificações automáticas da cena Fogo (no editor, sem entrar em Play): percursos a pé com um
    /// CharacterController igual ao do jogador (raio 0,4 · altura 1,8 · rampa 46° · degrau 0,35), saída da lava a andar,
    /// pontos altos dentro da zona final, linhas de tiro pelas entradas e por cima da lava, fecho da tempestade e
    /// números de desempenho (triângulos, renderers, materiais, luzes). Não altera a cena.
    /// </summary>
    public static class FogoValidation
    {
        const float Dt = 0.02f, Speed = 5f, Gravity = 24f;

        [MenuItem("FPS-LAN/Mapas/Validar Fogo", priority = 46)]
        static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var r = Run();
            Debug.Log("[Fogo] Validação\n" + r);
            EditorUtility.DisplayDialog("Fogo — validação", r, "OK");
        }

        public static string Run()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != FogoBuilder.ScenePath) EditorSceneManager.OpenScene(FogoBuilder.ScenePath);
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            var storm = Object.FindAnyObjectByType<StormManager>();
            var lavaSrc = Object.FindAnyObjectByType<LavaArea>();
            if (storm == null || lavaSrc == null) return "Cena Fogo sem StormManager/LavaArea.";

            var lava = Object.Instantiate(lavaSrc.gameObject).GetComponent<LavaArea>();
            foreach (var c in lava.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is LavaArea)) Object.DestroyImmediate(c);
            typeof(LavaArea).GetMethod("Decode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(lava, null);

            var walker = new GameObject("tmpWalker");
            walker.hideFlags = HideFlags.HideAndDontSave;
            var cc = walker.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.4f; cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 46f; cc.stepOffset = 0.35f; cc.skinWidth = 0.05f;
            try
            {
                Entrances(sb, storm, cc);
                Connectivity(sb, storm, lava);
                Perches(sb, storm, lava);
                Shots(sb, storm, lava);
                Storm(sb, storm);
                Performance(sb);
            }
            finally
            {
                Object.DestroyImmediate(walker);
                Object.DestroyImmediate(lava.gameObject);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ andar

        /// <summary>Anda em linha reta até cada ponto; devolve null se chegou, senão onde ficou preso.</summary>
        static string Walk(CharacterController cc, Vector3 start, IList<Vector3> path, out float seconds, out float maxY)
        {
            cc.transform.position = start + Vector3.up * 0.3f;
            Physics.SyncTransforms();
            float vy = 0f;
            int steps = 0;
            maxY = -999f;
            for (int w = 0; w < path.Count; w++)
            {
                float best = 1e9f;
                int stall = 0;
                for (int it = 0; it < 6000; it++)
                {
                    var p = cc.transform.position;
                    var d = path[w] - p;
                    d.y = 0f;
                    if (d.magnitude < 0.7f) break;
                    vy = cc.isGrounded ? -2f : vy - Gravity * Dt;
                    cc.Move(d.normalized * Speed * Dt + Vector3.up * vy * Dt);
                    Physics.SyncTransforms();
                    steps++;
                    maxY = Mathf.Max(maxY, cc.transform.position.y);
                    if (cc.transform.position.y < -12f) { seconds = steps * Dt; return $"caiu abaixo do mapa antes do ponto {w}"; }
                    if (d.magnitude < best - 0.05f) { best = d.magnitude; stall = 0; }
                    else if (++stall > 150) { seconds = steps * Dt; return $"preso antes do ponto {w} em {Fmt(cc.transform.position)}"; }
                }
            }
            seconds = steps * Dt;
            return null;
        }

        static void Entrances(StringBuilder sb, StormManager storm, CharacterController cc)
        {
            sb.AppendLine("ENTRADAS (fora → boca → faixa → interior → centro da zona final):");
            int ok = 0, total = 0;
            for (int k = 0; k < storm.entrances.Length; k++)
                for (int lane = 0; lane < 2; lane++)
                {
                    var e = storm.entrances[k];
                    var path = new[] { e.mouth, lane == 0 ? e.laneA : e.laneB, e.inner, storm.center };
                    var err = Walk(cc, e.outer, path, out float t, out float maxY);
                    total++;
                    if (err == null) ok++;
                    sb.AppendLine($"  entrada {k + 1}{(lane == 0 ? "A" : "B")}: {(err ?? "OK")} · {t:0.0} s · altura máx. {maxY:0.0} m");
                }
            sb.AppendLine($"  → {ok}/{total} percursos chegam ao centro.");
        }

        // ------------------------------------------------------------------ ligação de todo o chão

        /// <summary>
        /// Grelha de 0,4 m: célula "de pé" se o chão tem inclinação ≤ 46° e cabe lá uma cápsula de jogador (sem
        /// coberturas); duas vizinhas ligam-se se o desnível for ≤ 0,4 m (degrau 0,35 + rampa). Procura a partir
        /// do centro da zona final e conta o chão que não se liga (becos sem saída, ilhas, poços).
        /// </summary>
        static void Connectivity(StringBuilder sb, StormManager storm, LavaArea lava)
        {
            const float step = 0.4f, hx = 30.0f, hz = 31.4f;
            int nx = Mathf.FloorToInt(2 * hx / step), nz = Mathf.FloorToInt(2 * hz / step);
            var h = new float[nx, nz];
            var ok = new bool[nx, nz];
            var isLava = new bool[nx, nz];
            int standable = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var p = new Vector3(-hx + (i + 0.5f) * step, 0f, -hz + (j + 0.5f) * step);
                    RaycastHit ground = default;
                    bool found = false;
                    foreach (var hit in Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        if (hit.collider is MeshCollider && (!found || hit.point.y > ground.point.y)) { ground = hit; found = true; }
                    if (!found || ground.normal.y < 0.69f) continue;
                    var g = ground.point;
                    bool blocked = false;
                    foreach (var c in Physics.OverlapCapsule(g + Vector3.up * 0.5f, g + Vector3.up * 1.4f, 0.36f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        if (!(c is MeshCollider)) { blocked = true; break; }
                    if (blocked) continue;
                    h[i, j] = g.y;
                    ok[i, j] = true;
                    isLava[i, j] = lava.IsLava(g, out _);
                    standable++;
                }
            int ci = Mathf.Clamp(Mathf.FloorToInt((storm.center.x + hx) / step), 0, nx - 1);
            int cj = Mathf.Clamp(Mathf.FloorToInt((storm.center.z + hz) / step), 0, nz - 1);
            var seen = new bool[nx, nz];
            var q = new Queue<Vector2Int>();
            if (ok[ci, cj]) { seen[ci, cj] = true; q.Enqueue(new Vector2Int(ci, cj)); }
            int reached = 0;
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                reached++;
                foreach (var d in dirs)
                {
                    int a = c.x + d.x, b = c.y + d.y;
                    if (a < 0 || b < 0 || a >= nx || b >= nz || seen[a, b] || !ok[a, b]) continue;
                    if (Mathf.Abs(h[a, b] - h[c.x, c.y]) > 0.4f) continue;
                    seen[a, b] = true;
                    q.Enqueue(new Vector2Int(a, b));
                }
            }
            // ilhas não ligadas (agrupadas)
            var islands = new List<string>();
            int lostDry = 0, lostLava = 0;
            var mark = new bool[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (!ok[i, j] || seen[i, j] || mark[i, j]) continue;
                    int size = 0, lavaCells = 0;
                    float sx = 0, sz = 0, sy = 0;
                    var st = new Stack<Vector2Int>();
                    st.Push(new Vector2Int(i, j));
                    mark[i, j] = true;
                    while (st.Count > 0)
                    {
                        var c = st.Pop();
                        size++;
                        if (isLava[c.x, c.y]) lavaCells++;
                        sx += -hx + (c.x + 0.5f) * step; sz += -hz + (c.y + 0.5f) * step; sy += h[c.x, c.y];
                        foreach (var d in dirs)
                        {
                            int a = c.x + d.x, b = c.y + d.y;
                            if (a < 0 || b < 0 || a >= nx || b >= nz || mark[a, b] || seen[a, b] || !ok[a, b]) continue;
                            if (Mathf.Abs(h[a, b] - h[c.x, c.y]) > 0.4f) continue;
                            mark[a, b] = true;
                            st.Push(new Vector2Int(a, b));
                        }
                    }
                    lostLava += lavaCells;
                    lostDry += size - lavaCells;
                    if (size >= 4) islands.Add($"{size} células ({size * step * step:0.0} m², {lavaCells} de lava) perto de ({sx / size:0.0}, {sy / size:0.0}, {sz / size:0.0})");
                }
            var extra = new StringBuilder();
            int spawnOk = 0, spawnN = 0;
            foreach (var sp in Object.FindObjectsByType<SpawnPoint>())
            {
                spawnN++;
                var p = sp.transform.position;
                int a = Mathf.Clamp(Mathf.FloorToInt((p.x + hx) / step), 0, nx - 1), b = Mathf.Clamp(Mathf.FloorToInt((p.z + hz) / step), 0, nz - 1);
                bool any = false;
                for (int u = -1; u <= 1; u++) for (int v = -1; v <= 1; v++)
                    { int x = a + u, z = b + v; if (x >= 0 && z >= 0 && x < nx && z < nz && seen[x, z]) any = true; }
                if (any) spawnOk++;
                else extra.AppendLine($"  spawn sem ligação: {sp.name} {Fmt(p)}");
            }
            // lava: distância a pé (pela grelha) até à rocha seca ligada ao centro
            var dist = new int[nx, nz];
            var lq = new Queue<Vector2Int>();
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    dist[i, j] = -1;
                    if (seen[i, j] && !isLava[i, j]) { dist[i, j] = 0; lq.Enqueue(new Vector2Int(i, j)); }
                }
            while (lq.Count > 0)
            {
                var c = lq.Dequeue();
                foreach (var d in dirs)
                {
                    int a = c.x + d.x, b = c.y + d.y;
                    if (a < 0 || b < 0 || a >= nx || b >= nz || dist[a, b] >= 0 || !ok[a, b] || !isLava[a, b]) continue;
                    if (Mathf.Abs(h[a, b] - h[c.x, c.y]) > 0.4f) continue;
                    dist[a, b] = dist[c.x, c.y] + 1;
                    lq.Enqueue(new Vector2Int(a, b));
                }
            }
            int lavaTotal = 0, lavaStuck = 0, worstD = 0;
            var stuckAt = new List<string>();
            Vector3 worstAt = Vector3.zero;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (!ok[i, j] || !isLava[i, j]) continue;
                    lavaTotal++;
                    if (dist[i, j] < 0) { lavaStuck++; if (lavaStuck <= 4) stuckAt.Add(Fmt(new Vector3(-hx + (i + 0.5f) * step, h[i, j], -hz + (j + 0.5f) * step))); }
                    else if (dist[i, j] > worstD) { worstD = dist[i, j]; worstAt = new Vector3(-hx + (i + 0.5f) * step, h[i, j], -hz + (j + 0.5f) * step); }
                }
            extra.AppendLine($"  LAVA: {lavaTotal} células de pé em lava; sem saída a andar: {lavaStuck} {string.Join(" ", stuckAt.ToArray())}; o ponto mais longe da margem fica a {worstD * step:0.0} m ({worstD * step / Speed:0.0} s a correr) em {Fmt(worstAt)}.");
            sb.AppendLine($"LIGAÇÃO DO CHÃO (grelha 0,4 m, desnível ≤ 0,4 m, rampa ≤ 46°, cápsula do jogador): {standable} células de pé, {reached} ligadas ao centro da zona final ({100f * reached / Mathf.Max(1, standable):0.0}%).");
            sb.AppendLine($"  não ligadas: {lostDry} secas, {lostLava} em lava; spawns ligados ao centro: {spawnOk}/{spawnN}.");
            islands.Sort((x, y) => int.Parse(y.Split(' ')[0]).CompareTo(int.Parse(x.Split(' ')[0])));
            for (int k = 0; k < islands.Count && k < 10; k++) sb.AppendLine("  ilha: " + islands[k]);
            if (islands.Count > 10) sb.AppendLine($"  … e mais {islands.Count - 10} ilhas pequenas.");
            sb.Append(extra);
        }

        // ------------------------------------------------------------------ zona final

        static void Perches(StringBuilder sb, StormManager storm, LavaArea lava)
        {
            float lastR = storm.phases[storm.phases.Length - 1].radius;
            int samples = 0, high = 0, lavaIn = 0;
            float maxH = -99f;
            for (float x = -storm.ceilingFromRadius; x <= storm.ceilingFromRadius; x += 0.5f)
                for (float z = -storm.ceilingFromRadius; z <= storm.ceilingFromRadius; z += 0.5f)
                {
                    var p = storm.center + new Vector3(x, 0f, z);
                    if (Flat(p - storm.center).magnitude > storm.ceilingFromRadius) continue;
                    foreach (var h in Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 60f))
                    {
                        if (h.normal.y < 0.72f) continue;          // só onde se consegue estar de pé
                        samples++;
                        float rel = h.point.y - storm.center.y;
                        maxH = Mathf.Max(maxH, rel);
                        if (rel > storm.ceiling) high++;
                    }
                    if (Flat(p - storm.center).magnitude <= lastR && lava.IsLava(p, out _)) lavaIn++;
                }
            sb.AppendLine($"ZONA FINAL: raio {lastR} m em {Fmt(storm.center)}; teto de {storm.ceiling} m a partir de raio {storm.ceilingFromRadius} m.");
            sb.AppendLine($"  superfícies de pé dentro de {storm.ceilingFromRadius} m: {samples}; mais alta a {maxH:0.0} m do chão da arena; acima do teto: {high}.");
            sb.AppendLine($"  lava dentro do círculo final: {(lavaIn == 0 ? "nenhuma" : lavaIn + " amostras")}.");
        }

        // ------------------------------------------------------------------ tiros

        static void Shots(StringBuilder sb, StormManager storm, LavaArea lava)
        {
            int ok = 0, n = 0;
            // pelas duas faixas de cada entrada (o penedo do meio é cobertura de propósito)
            foreach (var e in storm.entrances)
                foreach (var lanePt in new[] { e.laneA, e.laneB })
                {
                    var side = Flat(lanePt - Vector3.Lerp(e.laneA, e.laneB, 0.5f));
                    var a = e.mouth + side + Vector3.up * 1.3f;
                    var b = e.inner + side * 0.6f + Vector3.up * 1.3f;
                    if (Physics.Raycast(a + Vector3.up * 3f, Vector3.down, out var ha, 8f)) a.y = ha.point.y + 1.3f;
                    if (Physics.Raycast(b + Vector3.up * 3f, Vector3.down, out var hb, 8f)) b.y = hb.point.y + 1.3f;
                    n++;
                    if (!Physics.Linecast(a, b, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) ok++;
                    else if (n <= 8) sb.AppendLine($"  linha bloqueada por {hit.collider.name} em {Fmt(hit.point)}");
                }
            sb.AppendLine($"TIROS: boca → interior pelas faixas laterais, a 1,3 m do chão: {ok}/{n} livres.");
            var cells = lava.LavaCells;
            int across = 0, free = 0;
            for (int i = 0; i < cells.Count; i += 23)
            {
                var c = cells[i];
                for (int d = 0; d < 2; d++)
                {
                    var dir = d == 0 ? Vector3.right : Vector3.forward;
                    var p0 = c - dir * 3f;
                    var p1 = c + dir * 3f;
                    if (lava.IsLava(p0, out _) || lava.IsLava(p1, out _)) continue;
                    if (!Physics.Raycast(p0 + Vector3.up * 3f, Vector3.down, out var h0, 6f)) continue;
                    if (!Physics.Raycast(p1 + Vector3.up * 3f, Vector3.down, out var h1, 6f)) continue;
                    across++;
                    if (!Physics.Linecast(h0.point + Vector3.up * 1.3f, h1.point + Vector3.up * 1.3f, out _, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) free++;
                }
            }
            sb.AppendLine($"  por cima da lava (margem a margem, 6 m): {free}/{across} linhas livres (as restantes cruzam rochas/muros de cobertura).");
        }

        // ------------------------------------------------------------------ tempestade

        static void Storm(StringBuilder sb, StormManager storm)
        {
            float total = 0f;
            foreach (var ph in storm.phases) total += ph.wait + ph.shrink;
            storm.Evaluate(storm.StartTime.Value + total + 1, out int phase, out bool shrinking, out float r, out _, out _, out int dps);
            sb.AppendLine($"TEMPESTADE: {storm.phases.Length} fases, {storm.startRadius} → {r} m em {total / 60f:0.0} min; depois fica a {dps} vida/s fora da zona.");
            float prev = storm.startRadius;
            for (int i = 0; i < storm.phases.Length; i++)
            {
                var ph = storm.phases[i];
                sb.AppendLine($"  fase {i + 1}: espera {ph.wait} s, fecha {prev}→{ph.radius} m em {ph.shrink} s, {ph.damagePerSecond}/s");
                prev = ph.radius;
            }
            int mapCorners = 0;
            var info = Object.FindAnyObjectByType<MapInfo>();
            if (info != null)
                foreach (var cx in new[] { -1f, 1f })
                    foreach (var cz in new[] { -1f, 1f })
                        if (!storm.IsSafe(new Vector3(cx * info.halfSize, 0f, cz * info.halfSize), r)) mapCorners++;
            sb.AppendLine($"  no fim, cantos do mapa fora da zona: {mapCorners}/4 (sem refúgios fora do círculo: a parede da tempestade é um cilindro sem altura máxima).");
        }

        // ------------------------------------------------------------------ desempenho

        static void Performance(StringBuilder sb)
        {
            long tris = 0, bushTris = 0;
            int renderers = 0, lights = 0;
            var mats = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                renderers++;
                long t = 0;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) t += mf.sharedMesh.GetIndexCount(s) / 3;
                tris += t;
                if (r.GetComponentInParent<Bush>() != null) bushTris += t;
                foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
            }
            foreach (var l in Object.FindObjectsByType<Light>()) if (l.enabled) lights++;
            int bushes = Object.FindObjectsByType<Bush>().Length;
            int bushCols = 0;
            foreach (var b in Object.FindObjectsByType<Bush>()) bushCols += b.GetComponentsInChildren<Collider>(true).Length;
            sb.AppendLine($"DESEMPENHO (cena em repouso): {tris:N0} triângulos em {renderers} renderers, {mats.Count} materiais, {lights} luzes na cena.");
            sb.AppendLine($"  arbustos: {bushes} zonas, {bushTris:N0} triângulos, {bushCols} colisores (devem ser 0: atravessam-se).");
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        static string Fmt(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";
    }
}
