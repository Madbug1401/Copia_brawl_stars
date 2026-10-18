using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Host / Join por IP na LAN, sobre Unity Transport (UDP).</summary>
    [DefaultExecutionOrder(-1000)]
    public class ConnectionManager : MonoBehaviour
    {
        public const ushort DefaultPort = 7777;
        public const int MaxPlayers = 4;

        public static ConnectionManager Instance { get; private set; }

        public string Status { get; private set; } = "";
        public bool IsConnecting { get; private set; }

        float m_ConnectStartedAt;

        static NetworkManager NM => NetworkManager.Singleton;

        static bool s_HostAfterLoad;
        static GameMode s_HostMode;
        static ushort s_HostPort;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPendingHost() => s_HostAfterLoad = false;

        void Awake()
        {
            Instance = this;
            // Netcode persists across scenes. Keep its transport and client connection.
            if (NM != null)
                foreach (var manager in FindObjectsByType<NetworkManager>())
                    if (manager != NM)
                    {
                        manager.gameObject.SetActive(false);
                        Destroy(manager.gameObject);
                    }
        }

        public static bool SupportsGemGrab(string mapKey) => mapKey == "santiago" || mapKey == "sal";

        public bool HostMap(string mapKey, ushort port = DefaultPort)
        {
            if (NM == null || NM.IsListening || IsConnecting) return false;
            if (MatchManager.MenuSelectedMode == GameMode.ItTakesTwo) mapKey = "santiago";
            string scene;
            switch (mapKey)
            {
                case "pradaria": scene = "Arena"; break;
                case "santiago": scene = "CidadeVelha"; break;
                case "sal": scene = "Buracona"; break;
                default: Status = "This map is not available yet."; return false;
            }
            if (MatchManager.MenuSelectedMode == GameMode.GemGrab && !SupportsGemGrab(mapKey))
            {
                Status = "GEM RUSH is available in Santiago and Sal.";
                return false;
            }
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == scene)
                return Host(port);
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Status = "Scene unavailable: " + scene;
                return false;
            }
            s_HostMode = MatchManager.MenuSelectedMode;
            s_HostPort = port;
            s_HostAfterLoad = true;
            IsConnecting = true;
            UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(scene);
            return true;
        }

        IEnumerator HostAfterSceneLoad()
        {
            yield return null; // UI and scene systems must finish initialization first.
            if (!s_HostAfterLoad) yield break;
            s_HostAfterLoad = false;
            MatchManager.MenuSelectedMode = s_HostMode;
            IsConnecting = false;
            if (!Host(s_HostPort)) GameUI.ClearPendingBots();
        }

        void Start()
        {
            if (NM == null)
            {
                Status = "NetworkManager not found in this scene.";
                return;
            }

            NM.OnClientConnectedCallback += OnClientConnected;
            NM.OnClientDisconnectCallback += OnClientDisconnected;
            NM.ConnectionApprovalCallback = ApproveConnection;
            NM.NetworkConfig.ConnectionApproval = true;
            if (s_HostAfterLoad) StartCoroutine(HostAfterSceneLoad());
        }

        void OnDestroy()
        {
            if (NM != null)
            {
                NM.OnClientConnectedCallback -= OnClientConnected;
                NM.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (IsConnecting && NM != null && !NM.IsServer && !NM.IsConnectedClient && Time.time - m_ConnectStartedAt > 10f)
            {
                Status = "Connection failed. Check the IP, host and firewall.";
                IsConnecting = false;
                NM.Shutdown();
            }
        }

        public bool Host(ushort port = DefaultPort)
        {
            if (NM == null || NM.IsListening) return false;
            if (MatchManager.MenuSelectedMode == GameMode.ItTakesTwo &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "CidadeVelha")
                return HostMap("santiago", port);
            if (MatchManager.MenuSelectedMode == GameMode.GemGrab && GemGrabManager.Instance == null)
            {
                Status = "GEM RUSH requires a map with a gem emitter (Santiago or Sal).";
                return false;
            }

            var utp = NM.GetComponent<UnityTransport>();
            utp.SetConnectionData("127.0.0.1", port, "0.0.0.0");

            bool ok = NM.StartHost();
            Status = ok ? "" : "Could not create server (UDP port " + port + " is already in use?).";
            return ok;
        }

        public bool Join(string ip, ushort port = DefaultPort)
        {
            if (NM == null || NM.IsListening) return false;

            ip = string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim();
            var utp = NM.GetComponent<UnityTransport>();
            utp.SetConnectionData(ip, port);

            bool ok = NM.StartClient();
            IsConnecting = ok;
            m_ConnectStartedAt = Time.time;
            Status = ok ? "Connecting to " + ip + "..." : "Invalid address.";
            return ok;
        }

        public void Leave()
        {
            GameUI.ClearPendingBots();
            if (NM == null) return;
            IsConnecting = false;
            if (CombatSystem.Instance != null) CombatSystem.Instance.ClearAll();
            NM.Shutdown();
            Status = "";
            MenuCamera.SetActive(true);
        }

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            int limit = MatchManager.IsCoopActive || MatchManager.MenuSelectedMode == GameMode.ItTakesTwo ? 2 : MaxPlayers;
            bool full = NM.ConnectedClientsIds.Count >= limit;
            // Arena cheia de bots? Um humano tem sempre prioridade sobre um bot.
            if (!full && PlayerNetwork.All.Count >= BotManager.MaxTotalPlayers) BotManager.RemoveBot();
            response.Approved = !full;
            response.CreatePlayerObject = true;
            response.Reason = full ? "Server full (max. " + limit + " players)." : "";
            response.Pending = false;
        }

        void OnClientConnected(ulong clientId)
        {
            if (clientId == NM.LocalClientId)
            {
                IsConnecting = false;
                Status = "";
            }
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (NM.IsServer) return;

            // Este cliente perdeu a ligação ao host
            IsConnecting = false;
            var reason = NM.DisconnectReason;
            Status = string.IsNullOrEmpty(reason) ? "Disconnected from host." : reason;
            if (CombatSystem.Instance != null) CombatSystem.Instance.ClearAll();
            MenuCamera.SetActive(true);
        }

        /// <summary>IPs locais IPv4 desta máquina (para mostrar no ecrã do host).</summary>
        public static List<string> GetLocalIPv4()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var s = ua.Address.ToString();
                        if (s.StartsWith("169.254.")) continue;
                        if (!list.Contains(s)) list.Add(s);
                    }
                }
            }
            catch
            {
                // Algumas plataformas não suportam: ignora
            }
            return list;
        }
    }
}
