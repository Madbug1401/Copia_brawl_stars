using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Host / Join na LAN, sobre Unity Transport (UDP). Os servidores aparecem sozinhos via LanDiscovery.
    /// Online: Unity Relay — o host cria uma sala e partilha o código; não é preciso abrir portas nem IPs.
    /// </summary>
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

        /// <summary>A sessão atual (ou a que está a ser criada) passa pelo Unity Relay.</summary>
        public bool IsOnline { get; private set; }
        /// <summary>Código da sala online (vazio na LAN). O host partilha-o; os outros escrevem-no em PLAY ONLINE.</summary>
        public string RoomCode { get; private set; } = "";

        float m_ConnectStartedAt;
        string m_ConnectTarget = "";
        bool m_RelayPending;   // a pedir sala/entrada ao Relay (antes de o Netcode arrancar)

        static NetworkManager NM => NetworkManager.Singleton;

        static bool s_HostAfterLoad;
        static GameMode s_HostMode;
        static ushort s_HostPort;
        static bool s_HostOnline;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPendingHost() { s_HostAfterLoad = false; s_HostOnline = false; s_SignIn = null; s_CommandLineJoinDone = false; }

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

        public static bool SupportsGemGrab(string mapKey) => mapKey == "santiago" || mapKey == "sal" || mapKey == "fogo";

        public bool HostMap(string mapKey, ushort port = DefaultPort, bool online = false)
        {
            if (NM == null || NM.IsListening || IsConnecting) return false;
            if (MatchManager.MenuSelectedMode == GameMode.ItTakesTwo) mapKey = "santiago";
            string scene;
            switch (mapKey)
            {
                case "pradaria": scene = "Arena"; break;
                case "santiago": scene = "CidadeVelha"; break;
                case "sal": scene = "Buracona"; break;
                case "fogo": scene = "Fogo"; break;
                default: Status = "This map is not available yet."; return false;
            }
            if (MatchManager.MenuSelectedMode == GameMode.GemGrab && !SupportsGemGrab(mapKey))
            {
                Status = "GEM RUSH is available in Santiago, Sal and Fogo.";
                return false;
            }
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == scene)
                return Host(port, online);
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Status = "Scene unavailable: " + scene;
                return false;
            }
            s_HostMode = MatchManager.MenuSelectedMode;
            s_HostPort = port;
            s_HostOnline = online;
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
            bool online = s_HostOnline;
            s_HostOnline = false;
            if (!Host(s_HostPort, online)) GameUI.ClearPendingBots();
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
            else if (!s_CommandLineJoinDone) StartCoroutine(JoinFromCommandLine());
        }

        static bool s_CommandLineJoinDone;

        /// <summary>Testes: ".exe -joinroom CODIGO" entra logo na sala online, sem passar pelo menu.</summary>
        IEnumerator JoinFromCommandLine()
        {
            s_CommandLineJoinDone = true;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-joinroom")
                {
                    yield return null;
                    yield return null;
                    JoinOnline(args[i + 1]);
                    yield break;
                }
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
            if (IsConnecting && !m_RelayPending && NM != null && !NM.IsServer && !NM.IsConnectedClient && Time.unscaledTime - m_ConnectStartedAt > ConnectTimeout)
            {
                // Nenhuma resposta: o pedido não chegou ao host (IP errado, rede diferente ou firewall do host).
                Status = IsOnline
                    ? "No reply from " + m_ConnectTarget + ". Check the code and that the host is still in the match."
                    : "No reply from " + m_ConnectTarget + ". On the host PC press FIX FIREWALL, " +
                      "and check both PCs are on the same Wi-Fi.";
                IsConnecting = false;
                NM.Shutdown();
            }
        }

        public bool Host(ushort port = DefaultPort, bool online = false)
        {
            if (NM == null || NM.IsListening || m_RelayPending) return false;
            if (MatchManager.MenuSelectedMode == GameMode.ItTakesTwo &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "CidadeVelha")
                return HostMap("santiago", port, online);
            if (MatchManager.MenuSelectedMode == GameMode.GemGrab && GemGrabManager.Instance == null)
            {
                Status = "GEM RUSH requires a map with a gem emitter (Santiago, Sal or Fogo).";
                return false;
            }
            if (online)
            {
                HostRelayAsync();
                return true;
            }
            IsOnline = false;
            RoomCode = "";

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
            if (NM == null || NM.IsListening || m_RelayPending) return false;
            IsOnline = false;
            RoomCode = "";

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
            m_RelayPending = false;
            IsOnline = false;
            RoomCode = "";
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
            RoomCode = "";

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

        // ------------------------------------------------------------------ online (Unity Relay)

        /// <summary>Entra numa sala online com o código que o host partilhou.</summary>
        public bool JoinOnline(string code)
        {
            if (NM == null || NM.IsListening || m_RelayPending) return false;
            code = NormalizeRoomCode(code);
            if (!IsValidRoomCode(code))
            {
                Status = "Invalid room code. Use the code shown on the host's screen.";
                return false;
            }
            JoinRelayAsync(code);
            return true;
        }

        /// <summary>Tira espaços e passa a maiúsculas: os códigos do Relay são letras e números.</summary>
        public static string NormalizeRoomCode(string code)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in (code ?? "").Trim())
                if (!char.IsWhiteSpace(c) && c != '-') sb.Append(char.ToUpperInvariant(c));
            return sb.ToString();
        }

        public static bool IsValidRoomCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length < 6 || code.Length > 12) return false;
            foreach (var c in code) if (!char.IsLetterOrDigit(c) || c > 127) return false;
            return true;
        }

        async void HostRelayAsync()
        {
            m_RelayPending = true;
            IsConnecting = true;
            IsOnline = true;
            RoomCode = "";
            Status = "Creating online room...";
            try
            {
                await EnsureSignedInAsync();
                if (!StillPending()) return;
                // Lugares para os outros humanos (os bots correm no host e não contam).
                var allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
                var code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                if (!StillPending()) return;

                NM.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
                m_RelayPending = false;
                IsConnecting = false;
                if (!NM.StartHost())
                {
                    FailOnline("Could not start the online room. Try again.");
                    return;
                }
                RoomCode = code;
                Status = "";
                GUIUtility.systemCopyBuffer = code;   // pronto a colar no chat dos amigos
                Debug.Log("[Online] Room created: " + code + " (region " + allocation.Region + ")");
            }
            catch (System.Exception e)
            {
                if (StillPending()) FailOnline(FriendlyOnlineError(e, false));
                Debug.LogWarning("[Online] Create room failed: " + e);
            }
        }

        async void JoinRelayAsync(string code)
        {
            m_RelayPending = true;
            IsConnecting = true;
            IsOnline = true;
            RoomCode = "";
            m_ConnectTarget = "room " + code;
            Status = "Joining room " + code + "...";
            try
            {
                await EnsureSignedInAsync();
                if (!StillPending()) return;
                var allocation = await RelayService.Instance.JoinAllocationAsync(code);
                if (!StillPending()) return;

                NM.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
                m_RelayPending = false;
                m_ConnectStartedAt = Time.unscaledTime;
                if (!NM.StartClient())
                {
                    FailOnline("Could not connect to the room. Try again.");
                    return;
                }
                RoomCode = code;
            }
            catch (System.Exception e)
            {
                if (StillPending()) FailOnline(FriendlyOnlineError(e, true));
                Debug.LogWarning("[Online] Join room failed: " + e);
            }
        }

        /// <summary>Falso se o pedido foi cancelado (Leave/CANCEL) ou a cena mudou enquanto se esperava.</summary>
        bool StillPending() => this != null && m_RelayPending && NM != null;

        void FailOnline(string message)
        {
            m_RelayPending = false;
            IsConnecting = false;
            IsOnline = false;
            RoomCode = "";
            Status = message;
            GameUI.ClearPendingBots();
            if (NM != null && NM.IsListening) NM.Shutdown();
            MenuCamera.SetActive(true);
        }

        static string FriendlyOnlineError(System.Exception e, bool joining)
        {
            var msg = (e.Message ?? "").ToLowerInvariant();
            if (joining && (msg.Contains("join code") || msg.Contains("not found") || msg.Contains("invalid")))
                return "Room not found. Check the code (the room closes when the host leaves).";
            if (joining && (msg.Contains("full") || msg.Contains("capacity")))
                return "The room is full.";
            if (msg.Contains("network") || msg.Contains("connect") || msg.Contains("timeout") || msg.Contains("resolve") || msg.Contains("unreachable"))
                return "No internet connection (or Unity services unreachable). Check your connection and try again.";
            return (joining ? "Could not join the room: " : "Could not create the room: ") + e.Message;
        }

        static Task s_SignIn;

        /// <summary>Inicializa os serviços Unity e faz login anónimo (uma vez por execução).</summary>
        static Task EnsureSignedInAsync()
        {
            if (s_SignIn == null || s_SignIn.IsFaulted || s_SignIn.IsCanceled) s_SignIn = SignInAsync();
            return s_SignIn;
        }

        static async Task SignInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // Perfil por processo: duas cópias do jogo no mesmo PC (ou Multiplayer Play Mode)
                // ficam com jogadores diferentes; senão o Relay via o mesmo jogador dos dois lados.
                var options = new InitializationOptions();
                options.SetProfile("p" + System.Diagnostics.Process.GetCurrentProcess().Id);
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
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
