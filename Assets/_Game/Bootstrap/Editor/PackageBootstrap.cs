using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace FPSLAN.Bootstrap
{
    /// <summary>
    /// Instala automaticamente os packages de que o jogo precisa na primeira abertura do projeto.
    /// O código do jogo (FPSLAN.Game) só compila depois de o Netcode estar instalado,
    /// por isso o projeto nunca abre com erros de compilação.
    /// </summary>
    [InitializeOnLoad]
    public static class PackageBootstrap
    {
        static readonly string[] Required =
        {
            "com.unity.netcode.gameobjects",
            "com.unity.multiplayer.playmode", // testar 2–4 jogadores no mesmo PC
            // Input System não é necessário: o jogo usa o Input Manager se for esse o ativo.
        };

        static Queue<string> s_Queue;
        static AddRequest s_Current;
        static string s_CurrentName;

        static PackageBootstrap()
        {
            EditorApplication.delayCall += Begin;
        }

        static void Begin()
        {
            // Nunca instalar durante o Play mode ou a meio de uma compilação: volta a tentar depois.
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Begin;
                return;
            }
            if (s_Current != null && !s_Current.IsCompleted) return;

            var installed = new HashSet<string>();
            foreach (var p in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                installed.Add(p.name);

            s_Queue = new Queue<string>();
            foreach (var name in Required)
            {
                if (installed.Contains(name)) continue;
                // Conta tentativas por sessão do Editor, para não ficar em loop se um package falhar.
                var key = "FPSLAN_Bootstrap_Tries_" + name;
                if (SessionState.GetInt(key, 0) >= 2) continue;
                s_Queue.Enqueue(name);
            }

            if (s_Queue.Count == 0) return;

            Debug.Log("[FPS-LAN] A instalar packages necessários: " + string.Join(", ", s_Queue.ToArray()));
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Next();
        }

        static void Next()
        {
            if (s_Queue == null || s_Queue.Count == 0)
            {
                EditorApplication.update -= Tick;
                s_Current = null;
                Debug.Log("[FPS-LAN] Packages instalados. O projeto vai compilar e gerar a cena Arena automaticamente.");
                return;
            }

            s_CurrentName = s_Queue.Dequeue();
            var key = "FPSLAN_Bootstrap_Tries_" + s_CurrentName;
            SessionState.SetInt(key, SessionState.GetInt(key, 0) + 1);
            s_Current = Client.Add(s_CurrentName);
        }

        static void Tick()
        {
            if (s_Current == null || !s_Current.IsCompleted) return;

            if (s_Current.Status == StatusCode.Success)
                Debug.Log($"[FPS-LAN] Instalado: {s_Current.Result.packageId}");
            else
                Debug.LogWarning($"[FPS-LAN] Não foi possível instalar {s_CurrentName}: {s_Current.Error?.message}");

            Next();
        }
    }
}
