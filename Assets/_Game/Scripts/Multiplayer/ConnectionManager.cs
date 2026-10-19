using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Host / Join na LAN, sobre Unity Transport (UDP). Os servidores aparecem sozinhos via LanDiscovery.</summary>
    [DefaultExecutionOrder(-1000)]
    public class ConnectionManager : MonoBehaviour
    {
        public const ushort DefaultPort = 7777;
        public const int MaxPlayers = 4;
        const float ConnectTimeout = 12f;

        public static ConnectionManager Instance { get; private set; }

        public string Status { get; private set; } = "";
        public bool IsConnecting { get; private set; }
        /// <summary>Porta em que este PC está a servir (anunciada aos clientes pela LanDiscovery).</summary>
        public ushort HostPort { get; private set; } = DefaultPort;

        float m_ConnectStartedAt;
        string m_ConnectTarget = "";

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
            if (IsConnecting && NM != null && !NM.IsServer && !NM.IsConnectedClient && Time.unscaledTime - m_ConnectStartedAt > ConnectTimeout)
            {
                // Nenhuma resposta: o pedido não chegou ao host (IP errado, rede diferente ou firewall do host).
                Status = "No reply from " + m_ConnectTarget + ". On the host PC press FIX FIREWALL, " +
                         "and check both PCs are on the same Wi-Fi.";
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
            if (ok) HostPort = port;
            Status = ok ? "" : "Could not create the server (is UDP port " + port + " in use by another copy of the game?).";
            return ok;
        }

        /// <summary>Liga a um servidor. Aceita "IP" ou "IP:porta".</summary>
        public bool Join(string code, ushort port = DefaultPort)
        {
            if (NM == null || NM.IsListening) return false;

            if (!TryParseCode(code, out var ip, out var parsedPort))
            {
                Status = "Invalid address.";
                return false;
            }
            if (parsedPort != 0) port = parsedPort;

            var utp = NM.GetComponent<UnityTransport>();
            utp.SetConnectionData(ip, port);

            bool ok = NM.StartClient();
            IsConnecting = ok;
            m_ConnectStartedAt = Time.unscaledTime;
            m_ConnectTarget = port == DefaultPort ? ip : ip + ":" + port;
            Status = ok ? "Connecting to " + m_ConnectTarget + "..." : "Invalid address.";
            return ok;
        }

        /// <summary>
        /// Lê um código de servidor: "192.168.1.20", "192.168.1.20:7778" ou "localhost".
        /// port = 0 quando o código não indica porta.
        /// </summary>
        public static bool TryParseCode(string code, out string ip, out ushort port)
        {
            ip = null;
            port = 0;
            code = (code ?? "").Trim();
            if (code.Length == 0 || code.Equals("localhost", System.StringComparison.OrdinalIgnoreCase))
            {
                ip = "127.0.0.1";
                return true;
            }
            int colon = code.LastIndexOf(':');
            if (colon > 0 && code.IndexOf(':') == colon)
            {
                if (!ushort.TryParse(code.Substring(colon + 1), out port) || port == 0) return false;
                code = code.Substring(0, colon).Trim();
                if (code.Equals("localhost", System.StringComparison.OrdinalIgnoreCase)) { ip = "127.0.0.1"; return true; }
            }
            // Exige os 4 números: o .NET aceita "192.168.1" como 192.168.0.1, o que esconderia gralhas.
            if (code.Split('.').Length != 4) return false;
            if (!System.Net.IPAddress.TryParse(code, out var addr) || addr.AddressFamily != AddressFamily.InterNetwork) return false;
            ip = addr.ToString();
            return true;
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

            // Este cliente perdeu a ligação ao host (ou o host recusou a entrada).
            bool duringConnect = IsConnecting;
            IsConnecting = false;
            var reason = NM.DisconnectReason;
            if (!string.IsNullOrEmpty(reason)) Status = reason;
            else if (duringConnect)
                // O Netcode recusa builds diferentes sem enviar motivo.
                Status = "The host refused the connection. Both PCs must run the same build of the game.";
            else Status = "Disconnected from host.";
            if (CombatSystem.Instance != null) CombatSystem.Instance.ClearAll();
            MenuCamera.SetActive(true);
        }

        // ------------------------------------------------------------------ IPs e firewall

        /// <summary>
        /// IPs locais IPv4 desta máquina (para mostrar no ecrã do host), do mais provável para o menos provável:
        /// adaptadores com gateway (Wi-Fi/Ethernet reais) primeiro; adaptadores virtuais (Hyper-V, WSL,
        /// VirtualBox, VMware, VPN) no fim.
        /// </summary>
        public static List<string> GetLocalIPv4()
        {
            var ranked = new List<KeyValuePair<int, string>>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();

                    int score = 0;
                    foreach (var gw in props.GatewayAddresses)
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork && !gw.Address.Equals(System.Net.IPAddress.Any)) { score += 100; break; }
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 20;
                    if (IsVirtualAdapter(ni.Name + " " + ni.Description)) score -= 200;

                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var s = ua.Address.ToString();
                        if (s.StartsWith("169.254.")) continue;
                        int ipScore = score;
                        if (s.StartsWith("192.168.") || s.StartsWith("10.")) ipScore += 10;
                        ranked.Add(new KeyValuePair<int, string>(ipScore, s));
                    }
                }
            }
            catch
            {
                // Algumas plataformas não suportam: ignora
            }

            ranked.Sort((a, b) => b.Key.CompareTo(a.Key));
            var list = new List<string>();
            foreach (var kv in ranked) if (!list.Contains(kv.Value)) list.Add(kv.Value);
            return list;
        }

        static bool IsVirtualAdapter(string text)
        {
            text = text.ToLowerInvariant();
            string[] virt = { "vethernet", "hyper-v", "wsl", "virtualbox", "vmware", "docker", "loopback", "tap-", "tunnel", "vpn", "hamachi", "radmin", "zerotier", "tailscale", "bluetooth" };
            foreach (var v in virt) if (text.Contains(v)) return true;
            return false;
        }

        public static bool CanFixFirewall => Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>Resultado do último "FIX FIREWALL" (vazio enquanto não houver).</summary>
        public static string FirewallMessage { get; private set; } = "";
        public static bool FixingFirewall { get; private set; }

        /// <summary>
        /// Abre o pedido de administrador do Windows e permite este executável na firewall (todas as redes,
        /// incluindo "Pública"). Remove primeiro as regras de bloqueio que o Windows cria quando alguém
        /// carrega em "Cancelar" no aviso da firewall — essas regras ganham sempre às de permissão.
        /// Corre numa thread para o jogo não congelar enquanto o aviso do Windows está aberto.
        /// </summary>
        public static void FixFirewall()
        {
            if (FixingFirewall) return;
            if (!CanFixFirewall) { FirewallMessage = "Only needed on Windows."; return; }
            string exe;
            try { exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName; }
            catch (System.Exception e) { FirewallMessage = "Firewall not changed (" + e.Message + ")."; return; }

            FixingFirewall = true;
            FirewallMessage = "Accept the Windows prompt to allow the game...";
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    const string rule = "Chiefs and Gems - LAN";
                    string args = "/c netsh advfirewall firewall delete rule name=all program=\"" + exe + "\" & " +
                                  "netsh advfirewall firewall add rule name=\"" + rule + "\" dir=in action=allow program=\"" + exe + "\" enable=yes profile=any & " +
                                  "netsh advfirewall firewall add rule name=\"" + rule + "\" dir=out action=allow program=\"" + exe + "\" enable=yes profile=any";
                    var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", args)
                    {
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    };
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        if (p == null) FirewallMessage = "Firewall not changed.";
                        else
                        {
                            p.WaitForExit(30000);
                            FirewallMessage = p.HasExited && p.ExitCode == 0
                                ? "Firewall fixed: other PCs can now join this one."
                                : "Firewall: the change did not finish. Try again.";
                        }
                    }
                }
                catch (System.Exception)
                {
                    // Inclui o caso em que a pessoa recusa o pedido de administrador.
                    FirewallMessage = "Firewall not changed (the Windows prompt was refused).";
                }
                finally { FixingFirewall = false; }
            }) { IsBackground = true, Name = "FirewallFix" };
            thread.Start();
        }
    }
}
