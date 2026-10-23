using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Validação dos interiores da Cidade Velha (cena CidadeVelha aberta): percursos com um CharacterController igual
    /// ao do jogador (raio 0,4 · altura 1,8 · rampa 46° · degrau 0,35) desde o spawn até ao coro alto da igreja
    /// (pelas duas escadas) e até ao 1.º andar e à varanda do sobrado; linhas de tiro óculo/janelas ↔ janelas do sobrado.
    /// </summary>
    public static class CidadeVelhaInterioresValidation
    {
        const float Dt = 1f / 60f, Speed = 5f, Gravity = 20f;

        [MenuItem("FPS-LAN/Mapas/Validar interiores da Cidade Velha", priority = 44)]
        static void Menu()
        {
            var r = Run();
            Debug.Log("[Cidade Velha] Validação dos interiores\n" + r);
            EditorUtility.DisplayDialog("Interiores da Cidade Velha", r, "OK");
        }

        public static string Run()
        {
            var sb = new StringBuilder();
            if (!EditorSceneManager.GetActiveScene().path.EndsWith("CidadeVelha.unity"))
                EditorSceneManager.OpenScene(ProjectSetup.Root + "/Scenes/CidadeVelha.unity", OpenSceneMode.Single);
            Physics.SyncTransforms();

            var walker = new GameObject("tmpWalker");
            walker.hideFlags = HideFlags.HideAndDontSave;
            var cc = walker.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.4f; cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 46f; cc.stepOffset = 0.35f; cc.skinWidth = 0.05f;
            try
            {
                sb.AppendLine("— Percursos (CharacterController do jogador)");
                foreach (var side in new[] { -1f, 1f })
                {
                    float sx = side * 7.1f, st = side * 8.15f;
                    Path(sb, cc, $"Igreja: spawn → nave → escada {(side < 0 ? "esquerda" : "direita")} → coro → óculo",
                        new Vector3(0f, 0f, -22.4f),
                        new[]
                        {
                            new Vector3(0f, 0f, -24.2f), new Vector3(0f, 0f, -26.6f), new Vector3(sx * 0.5f, 0f, -28.6f), new Vector3(sx, 0f, -28.6f),
                            new Vector3(sx, 0f, -33.7f), new Vector3(st, 0f, -33.7f), new Vector3(st, 0f, -28.4f), new Vector3(st, 0f, -27.0f),
                            new Vector3(0f, 0f, -26.4f),
                        }, 3.3f);
                }
                Path(sb, cc, "Sobrado: spawn → porta → escada → 1.º andar → varanda",
                    new Vector3(0f, 0f, 22.4f),
                    new[]
                    {
                        new Vector3(-1.5f, 0f, 24.2f), new Vector3(-1.5f, 0f, 26.2f), new Vector3(-1.0f, 0f, 31.2f), new Vector3(-2.0f, 0f, 33.15f),
                        new Vector3(-7.4f, 0f, 33.15f), new Vector3(-8.15f, 0f, 33.15f), new Vector3(-8.15f, 0f, 31.6f), new Vector3(-1.5f, 0f, 26.0f),
                        new Vector3(-1.5f, 0f, 24.45f),
                    }, 3.55f);
                Path(sb, cc, "Sobrado: 1.º andar → janelas da frente",
                    new Vector3(-6f, 3.6f, 30f),
                    new[] { new Vector3(-4.5f, 0f, 25.9f), new Vector3(1.5f, 0f, 25.9f), new Vector3(4.5f, 0f, 25.9f), new Vector3(7.5f, 0f, 25.9f) }, 3.55f);
                Transform cen = null;
                foreach (var t in Object.FindObjectsByType<Transform>())
                    if (t.name == "Cenario") { cen = t; break; }
                if (cen != null)
                    foreach (var r in CidadeVelhaInteriores.CasaRoutes(cen))
                        Path(sb, cc, r.name, r.start, r.pts, r.expectY);
                else sb.AppendLine("FALHA não encontrei o Cenario para validar as casas");
                // não se sai pelas janelas: tenta atravessar cada janela de cima e do coro
                Blocked(sb, cc, "Janela do sobrado (x=4,5) não deixa sair", new Vector3(4.5f, 3.6f, 26.2f), new Vector3(4.5f, 0f, 23.0f));
                Blocked(sb, cc, "Óculo da igreja não deixa sair", new Vector3(0f, 3.35f, -26.2f), new Vector3(0f, 0f, -23.0f));
                Blocked(sb, cc, "Janela do coro (x=5,5) não deixa sair", new Vector3(5.5f, 3.35f, -26.2f), new Vector3(5.5f, 0f, -23.0f));

                cc.transform.position = new Vector3(0f, -200f, 0f);
                Physics.SyncTransforms();
                sb.AppendLine("— Linhas de tiro (olhos a 1,6 m do chão)");
                var oculus = new Vector3(0f, 3.3f + 1.6f, -25.9f);
                var coroL = new Vector3(-5.5f, 3.3f + 1.6f, -25.9f);
                var coroR = new Vector3(5.5f, 3.3f + 1.6f, -25.9f);
                foreach (var x in new[] { -7.5f, -4.5f, 1.5f, 4.5f, 7.5f })
                {
                    var win = new Vector3(x, 3.55f + 1.6f, 25.75f);
                    Shot(sb, $"óculo → janela x={x}", oculus, win);
                    Shot(sb, $"coro x=−5,5 → janela x={x}", coroL, win);
                    Shot(sb, $"coro x=+5,5 → janela x={x}", coroR, win);
                }
                var balc = new Vector3(-1.5f, 3.55f + 1.6f, 24.5f);
                Shot(sb, "óculo → varanda", oculus, balc);
                Shot(sb, "coro x=−5,5 → varanda", coroL, balc);
                Shot(sb, "coro x=+5,5 → varanda", coroR, balc);

                sb.AppendLine("— Desempenho");
                int tris = 0, rend = 0, lights = 0;
                foreach (var r in Object.FindObjectsByType<MeshRenderer>())
                {
                    if (!r.transform.IsChildOf(FindInteriores())) continue;
                    rend++;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                }
                foreach (var l in Object.FindObjectsByType<Light>())
                    if (l.transform.IsChildOf(FindInteriores())) lights++;
                sb.AppendLine($"interiores: {rend} malhas (1 por material), {tris} triângulos, {lights} luzes pontuais");
            }
            finally
            {
                Object.DestroyImmediate(walker);
            }
            return sb.ToString();
        }

        static Transform FindInteriores()
        {
            foreach (var t in Object.FindObjectsByType<Transform>())
                if (t.name == "Interiores") return t;
            return null;
        }

        static void Path(StringBuilder sb, CharacterController cc, string name, Vector3 start, Vector3[] pts, float expectY)
        {
            var err = Walk(cc, start, pts, out float secs);
            float y = cc.transform.position.y;
            bool ok = err == null && Mathf.Abs(y - expectY) < 0.2f;
            sb.AppendLine($"{(ok ? "OK " : "FALHA")} {name}: {(err ?? "chegou")} · y final {y:0.00} (esperado {expectY:0.00}) · {secs:0.0} s");
        }

        static void Blocked(StringBuilder sb, CharacterController cc, string name, Vector3 start, Vector3 target)
        {
            Walk(cc, start, new[] { target }, out _);
            var p = cc.transform.position;
            bool inside = Mathf.Abs(p.z) > 24.9f;
            sb.AppendLine($"{(inside ? "OK " : "FALHA")} {name}: parou em {p.x:0.0}, {p.y:0.00}, {p.z:0.0}");
        }

        static string Walk(CharacterController cc, Vector3 start, IList<Vector3> path, out float seconds)
        {
            cc.transform.position = start + Vector3.up * 0.3f;
            Physics.SyncTransforms();
            float vy = 0f;
            int steps = 0;
            for (int w = 0; w < path.Count; w++)
            {
                float best = 1e9f;
                int stall = 0;
                for (int it = 0; it < 4000; it++)
                {
                    var p = cc.transform.position;
                    var d = path[w] - p;
                    d.y = 0f;
                    if (d.magnitude < 0.25f) break;
                    vy = cc.isGrounded ? -2f : vy - Gravity * Dt;
                    var mv = d.magnitude < Speed * Dt ? d : d.normalized * Speed * Dt;
                    cc.Move(mv + Vector3.up * vy * Dt);
                    Physics.SyncTransforms();
                    steps++;
                    if (cc.transform.position.y < -5f) { seconds = steps * Dt; return $"caiu antes do ponto {w}"; }
                    if (d.magnitude < best - 0.03f) { best = d.magnitude; stall = 0; }
                    else if (++stall > 120) { seconds = steps * Dt; var q = cc.transform.position; return $"preso antes do ponto {w} em ({q.x:0.00}, {q.y:0.00}, {q.z:0.00})"; }
                }
            }
            // assenta no chão
            for (int i = 0; i < 30; i++) { cc.Move(Vector3.down * 0.1f); Physics.SyncTransforms(); }
            seconds = steps * Dt;
            return null;
        }

        static void Shot(StringBuilder sb, string name, Vector3 a, Vector3 b)
        {
            var d = b - a;
            float len = d.magnitude;
            var hits = Physics.RaycastAll(a, d / len, len, ~0, QueryTriggerInteraction.Ignore);
            float first = len;
            string what = null;
            foreach (var h in hits)
                if (h.distance < first) { first = h.distance; what = h.collider.name; }
            sb.AppendLine(what == null ? $"livre  {name} ({len:0.0} m)" : $"tapado {name}: {what} a {first:0.0} m de {len:0.0} m");
        }
    }
}
