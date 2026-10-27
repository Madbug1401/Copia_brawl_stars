using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Descoberta automática de servidores na rede local (sem escrever IPs).
    ///
    /// Host: escuta pedidos na porta UDP 47777 e responde; também se anuncia por broadcast a cada segundo.
    /// Cliente (no menu): envia pedidos por broadcast a partir de uma porta efémera — a resposta do host é
    /// tráfego de resposta, que a firewall do cliente deixa passar mesmo sem regra própria — e escuta
    /// anúncios na 47777 quando consegue.
    ///
    /// O pacote leva uma impressão digital da build: se o host e o cliente tiverem versões diferentes do jogo,
    /// o menu avisa antes de ligar (o Netcode rejeita versões diferentes sem dar motivo).
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public class LanDiscovery : MonoBehaviour
    {
        public const int DiscoveryPort = 47777;
        const string Magic = "CGLAN1";
        const string Probe = Magic + "?";
        const float AnnounceInterval = 1f;
        const float ProbeInterval = 1.5f;
        const float ServerTimeout = 4.5f;

        public struct ServerInfo
        {
            public string Address;
            public ushort Port;
            public string HostName;
            public string Map;
            public string Mode;
            public int Players;
            public int MaxPlayers;
            public bool SameVersion;
            public float LastSeen;

            /// <summary>Código para o campo de ligação: "IP" na porta padrão, "IP:porta" nas outras.</summary>
            public string Code => Port == ConnectionManager.DefaultPort ? Address : Address + ":" + Port;
            public bool IsFull => MaxPlayers > 0 && Players >= MaxPlayers;
        }

        public static LanDiscovery Instance { get; private set; }

        static readonly List<ServerInfo> s_Servers = new List<ServerInfo>();
        /// <summary>Servidores encontrados recentemente (ordenados: mesma versão primeiro).</summary>
        public static IReadOnlyList<ServerInfo> Servers => s_Servers;
        /// <summary>Verdadeiro enquanto o cliente está a procurar (menu aberto e sem ligação).</summary>
        public static bool Searching { get; private set; }

        Socket m_Port;      // ligado à 47777: host recebe pedidos / cliente recebe anúncios
        Socket m_Probe;     // cliente: porta efémera para pedidos e respostas
        bool m_HostMode;
        float m_NextSend;
        float m_NextRetry;
        readonly byte[] m_Buffer = new byte[1024];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null || Application.isBatchMode) return;
            var go = new GameObject("LanDiscovery");
            DontDestroyOnLoad(go);
            go.AddComponent<LanDiscovery>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            CloseAll();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit() => CloseAll();

        // ------------------------------------------------------------------ ciclo

        void Update()
        {
            var nm = NetworkManager.Singleton;
            // Salas online (Relay) não se anunciam na LAN: só se entra nelas com o código.
            bool host = nm != null && nm.IsServer && nm.IsListening && !(ConnectionManager.Instance != null && ConnectionManager.Instance.IsOnline);
            bool browse = nm == null || !nm.IsListening;   // no menu, sem ligação nem servidor

            if (host != m_HostMode) { CloseAll(); m_HostMode = host; }
            Searching = browse;

            if (!host && !browse)
            {
                // A jogar como cliente: não precisa de sockets.
                CloseAll();
                return;
            }

            EnsureSockets(host);
            Receive(m_Port, host);
            if (!host) Receive(m_Probe, false);

            if (Time.unscaledTime >= m_NextSend)
            {
                if (host)
                {
                    m_NextSend = Time.unscaledTime + AnnounceInterval;
                    Broadcast(m_Port, BuildAnnouncement());
                }
                else
                {
                    m_NextSend = Time.unscaledTime + ProbeInterval;
                    Broadcast(m_Probe, Encoding.UTF8.GetBytes(Probe));
                }
            }

            if (!host) Expire();
            else if (s_Servers.Count > 0) s_Servers.Clear();
        }

        // ------------------------------------------------------------------ sockets

        void EnsureSockets(bool host)
        {
            if (Time.unscaledTime < m_NextRetry) return;
            bool missing = m_Port == null || (!host && m_Probe == null);
            if (!missing) return;
            m_NextRetry = Time.unscaledTime + 3f;

            if (m_Port == null) m_Port = Open(DiscoveryPort);
            if (!host && m_Probe == null) m_Probe = Open(0);
        }

        static Socket Open(int port)
        {
            Socket s = null;
            try
            {
                s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.ExclusiveAddressUse = false;
                s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                s.EnableBroadcast = true;
                s.Blocking = false;
                s.Bind(new IPEndPoint(IPAddress.Any, port));
                return s;
            }
            catch (Exception e)
            {
                if (port != 0) Debug.LogWarning("[LAN] Não foi possível escutar a porta UDP " + port + ": " + e.Message);
                try { s?.Close(); } catch { }
                return null;
            }
        }

        void CloseAll()
        {
            try { m_Port?.Close(); } catch { }
            try { m_Probe?.Close(); } catch { }
            m_Port = null;
            m_Probe = null;
            m_NextSend = 0f;
            m_NextRetry = 0f;
        }

        /// <summary>Envia para 255.255.255.255, para o broadcast de cada rede e para este PC.</summary>
        static void Broadcast(Socket s, byte[] data)
        {
            if (s == null) return;
            foreach (var target in CachedBroadcastTargets())
            {
                try { s.SendTo(data, new IPEndPoint(target, DiscoveryPort)); }
                catch { /* interface sem rota: ignora */ }
            }
        }

        // Enumerar as interfaces de rede no Windows demora dezenas de ms. Feito na thread principal a cada anúncio,
        // o host engasgava uma vez por segundo durante a partida. A lista é calculada numa thread de fundo e
        // reutilizada; até chegar a primeira, usa-se o broadcast geral e o loopback.
        const float TargetsRefreshInterval = 15f;
        static volatile List<IPAddress> s_Targets;
        static float s_TargetsNextRefresh;
        static int s_TargetsRefreshing;

        static List<IPAddress> CachedBroadcastTargets()
        {
            if (Time.unscaledTime >= s_TargetsNextRefresh && Interlocked.CompareExchange(ref s_TargetsRefreshing, 1, 0) == 0)
            {
                s_TargetsNextRefresh = Time.unscaledTime + TargetsRefreshInterval;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { s_Targets = BroadcastTargets(); }
                    finally { Interlocked.Exchange(ref s_TargetsRefreshing, 0); }
                });
            }
            return s_Targets ?? s_DefaultTargets;
        }

        static readonly List<IPAddress> s_DefaultTargets = new List<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };

        static List<IPAddress> BroadcastTargets()
        {
            var list = new List<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        var ip = ua.Address.GetAddressBytes();
                        var mask = ua.IPv4Mask.GetAddressBytes();
                        if (mask.Length != 4 || (mask[0] == 0 && mask[1] == 0 && mask[2] == 0 && mask[3] == 0)) continue;
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                        var addr = new IPAddress(b);
                        if (!list.Contains(addr)) list.Add(addr);
                    }
                }
            }
            catch { }
            return list;
        }

        void Receive(Socket s, bool host)
        {
            if (s == null) return;
            for (int guard = 0; guard < 32; guard++)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int n;
                try
                {
                    if (s.Available <= 0) return;
                    n = s.ReceiveFrom(m_Buffer, ref from);
                }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                if (n <= 0) return;

                var text = Encoding.UTF8.GetString(m_Buffer, 0, n);
                var sender = (IPEndPoint)from;
                if (host)
                {
                    if (text == Probe)
                    {
                        try { s.SendTo(BuildAnnouncement(), sender); } catch { }
                    }
                }
                else if (text.StartsWith(Magic + "|", StringComparison.Ordinal))
                {
                    Register(text, sender.Address);
                }
            }
        }

        // ------------------------------------------------------------------ mensagem

        static string Clean(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("|", "/").Replace("\n", " ").Replace("\r", " ").Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        static string s_CodeId;   // o código compilado não muda durante a execução

        /// <summary>Identifica a build: versão, configuração do Netcode e código compilado.</summary>
        public static string Fingerprint
        {
            get
            {
                var nm = NetworkManager.Singleton;
                ulong config = nm != null && nm.NetworkConfig != null ? nm.NetworkConfig.GetConfig(false) : 0UL;
                if (s_CodeId == null)
                {
                    try { s_CodeId = typeof(LanDiscovery).Assembly.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 8); }
                    catch { s_CodeId = "?"; }
                }
                return Clean(Application.version, 16) + "-" + config.ToString("x") + "-" + s_CodeId;
            }
        }

        static string MapLabel(string scene)
        {
            switch (scene)
            {
                case "CidadeVelha": return "Santiago";
                case "Buracona": return "Sal";
                case "Arena": return "Pradaria";
                default: return scene;
            }
        }

        static string ModeLabel(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.GemGrab: return "Gem Rush";
                case GameMode.ItTakesTwo: return "It Takes Two";
                default: return mode.ToString();
            }
        }

        static byte[] BuildAnnouncement()
        {
            var nm = NetworkManager.Singleton;
            int players = nm != null && nm.IsServer ? nm.ConnectedClientsIds.Count : 0;
            int max = MatchManager.IsCoopActive || MatchManager.MenuSelectedMode == GameMode.ItTakesTwo ? 2 : ConnectionManager.MaxPlayers;
            ushort port = ConnectionManager.Instance != null ? ConnectionManager.Instance.HostPort : ConnectionManager.DefaultPort;
            string name = Clean(GameSession.LocalName, 16);
            if (name.Length == 0) name = Clean(Environment.MachineName, 16);
            var msg = string.Join("|",
                Magic,
                Fingerprint,
                port.ToString(),
                name,
                MapLabel(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name),
                ModeLabel(MatchManager.MenuSelectedMode),
                players.ToString(),
                max.ToString());
            return Encoding.UTF8.GetBytes(msg);
        }

        static void Register(string text, IPAddress from)
        {
            var p = text.Split('|');
            if (p.Length < 8) return;
            if (!ushort.TryParse(p[2], out var port)) return;
            int.TryParse(p[6], out var players);
            int.TryParse(p[7], out var max);

            var info = new ServerInfo
            {
                Address = from.ToString(),
                Port = port,
                HostName = p[3],
                Map = p[4],
                Mode = p[5],
                Players = players,
                MaxPlayers = max,
                SameVersion = p[1] == Fingerprint,
                LastSeen = Time.unscaledTime,
            };

            for (int i = 0; i < s_Servers.Count; i++)
            {
                var s = s_Servers[i];
                bool sameHost = s.HostName == info.HostName && s.Port == info.Port;
                if (s.Address == info.Address && s.Port == info.Port || sameHost && IsLoopback(s.Address) != IsLoopback(info.Address))
                {
                    // Um host neste mesmo PC responde por 127.0.0.1 e pelo IP da rede: fica o IP da rede.
                    if (IsLoopback(info.Address) && !IsLoopback(s.Address)) info.Address = s.Address;
                    s_Servers[i] = info;
                    return;
                }
            }
            s_Servers.Add(info);
            s_Servers.Sort((a, b) => a.SameVersion == b.SameVersion ? string.CompareOrdinal(a.HostName, b.HostName) : (a.SameVersion ? -1 : 1));
        }

        static bool IsLoopback(string ip) => ip.StartsWith("127.", StringComparison.Ordinal);

        static void Expire()
        {
            float now = Time.unscaledTime;
            for (int i = s_Servers.Count - 1; i >= 0; i--)
                if (now - s_Servers[i].LastSeen > ServerTimeout) s_Servers.RemoveAt(i);
        }
    }
}
