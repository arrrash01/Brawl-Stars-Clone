using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Multiplayer;
using UnityEngine;
#if UNITY_SERVER || ENABLE_UCS_SERVER
using Unity.Services.Authentication.Server;
#endif

namespace RiftRumble
{
    /// <summary>Owns connection lifetime. All game traffic uses NGO and UnityTransport.</summary>
    [DefaultExecutionOrder(50)]
    public sealed class ArenaConnection : MonoBehaviour
    {
        public static ArenaConnection Instance { get; private set; }

        [Header("Dedicated server matchmaking")]
        [SerializeField] string servicesEnvironment = "production";
        [SerializeField] string gemRushQueue = "rift-gem-rush";
        [SerializeField] string showdownQueue = "rift-solo-showdown";
        [SerializeField] ushort port = 7777;
        [SerializeField, Min(5)] float connectionTimeout = 20f;
        [SerializeField] ArenaMatch matchPrefab;

        public BrawlerId SelectedBrawler { get; set; } = BrawlerId.Bront;
        public GameMode SelectedMode { get; set; } = GameMode.GemRush;
        public string Username { get; private set; } = string.Empty;
        public string PreferredServerAddress { get; private set; } = string.Empty;
        public bool HasLogin => !string.IsNullOrEmpty(Username);
        public string Status { get; private set; } = "Enter a username to continue.";
        public bool IsBusy => busy || leaving || matchmakingCancellation != null;
        public bool IsPractice { get; private set; }
        public bool IsDedicatedServer { get; private set; }
        public ArenaServerMatches ServerMatches { get; private set; }
        internal ArenaNetworkStats NetworkStats { get; private set; }
        public event Action Changed;

        NetworkManager manager;
        UnityTransport transport;
        ArenaMatch sceneMatch;
        ISession session;
        CancellationTokenSource matchmakingCancellation;
        Task matchmakingTask;
        bool busy;
        bool leaving;
        bool connecting;
        bool destroying;
        bool suppressDisconnect;
        int connectionGeneration;
        float connectStarted;
        string authenticationProfile;
        bool serveAllModes;
        int serverPingMs;
        int serverJitterMs;

        public void ConfigureMatchPrefab(ArenaMatch prefab) => matchPrefab = prefab;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            manager = NetworkManager.Singleton;
            if (manager == null) manager = FindFirstObjectByType<NetworkManager>();
            if (manager == null)
            {
                SetStatus("The arena NetworkManager is missing. Open the Arena scene.");
                enabled = false;
                return;
            }
            transport = manager.GetComponent<UnityTransport>();
            NetworkStats = new ArenaNetworkStats(manager, transport);
            sceneMatch = FindFirstObjectByType<ArenaMatch>();
            manager.OnServerStarted += OnServerStarted;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            manager.OnTransportFailure += OnTransportFailure;
        }

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            var runServer = HasArgument(args, "-arenaServer");
#if (UNITY_SERVER || ENABLE_UCS_SERVER) && !UNITY_EDITOR
            runServer = true;
#endif
            var requestedMode = Argument(args, "-mode");
            serveAllModes = runServer && (string.IsNullOrEmpty(requestedMode)
                || requestedMode.Equals("all", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(requestedMode))
            {
                if (requestedMode.Equals("showdown", StringComparison.OrdinalIgnoreCase))
                    SelectedMode = GameMode.SoloShowdown;
                else if (!requestedMode.Equals("gems", StringComparison.OrdinalIgnoreCase) && !serveAllModes)
                {
                    SetStatus("Unknown -mode. Use gems or showdown (servers also accept all).");
                    return;
                }
            }
            var requestedPort = Argument(args, "-port");
            if (!string.IsNullOrEmpty(requestedPort))
            {
                if (!ushort.TryParse(requestedPort, out var parsedPort) || parsedPort == 0)
                {
                    SetStatus("The server port must be between 1 and 65535.");
                    return;
                }
                port = parsedPort;
            }
            var requestedCharacter = Argument(args, "-character");
            if (Enum.TryParse(requestedCharacter, true, out BrawlerId brawler)
                && Enum.IsDefined(typeof(BrawlerId), brawler)) SelectedBrawler = brawler;
            authenticationProfile = Argument(args, "-profile");

            if (HasArgument(args, "-ping") || HasArgument(args, "-jitter"))
            {
                if (!runServer)
                {
                    SetStatus("-ping and -jitter are dedicated server options. Use -arenaServer.");
                    Debug.LogError($"[Rift Rumble] {Status}");
                    return;
                }
                if (!ReadSimulationArgument(args, "-ping", out serverPingMs)
                    || !ReadSimulationArgument(args, "-jitter", out serverJitterMs)) return;
            }

            if (runServer)
            {
                var matchId = Argument(args, "-matchId");
                if (string.IsNullOrWhiteSpace(matchId)) StartDedicatedServer();
                else
                {
#if UNITY_SERVER || ENABLE_UCS_SERVER
                    _ = StartCloudServerAsync(matchId, Argument(args, "-publicIp"));
#else
                    SetStatus("Cloud allocations require a server build (UNITY_SERVER or ENABLE_UCS_SERVER).");
                    Debug.LogError($"[Rift Rumble] {Status}");
#endif
                }
            }
            else
            {
                var address = Argument(args, "-arenaConnect");
                var practice = HasArgument(args, "-arenaPractice");
                var hasUsername = HasArgument(args, "-username");
                // Explicit startup commands preserve unattended client/practice launches.
                // Normal visual startup waits for the login panel instead.
                if (hasUsername || practice || !string.IsNullOrEmpty(address) || Application.isBatchMode)
                {
                    if (!TryLogin(hasUsername ? Argument(args, "-username") : "Player", "", "", out var error))
                    {
                        SetStatus(error);
                        Debug.LogWarning($"[Rift Rumble] {error}");
                        return;
                    }
                }
                if (practice) StartPractice();
                else if (!string.IsNullOrEmpty(address)) ConnectDirect(address);
            }
        }

        public bool TryLogin(string username, string ip, string portText, out string error)
        {
            error = null;
            if (IsBusy || destroying || (manager != null && (manager.IsListening || manager.ShutdownInProgress)))
            {
                error = "Leave the current arena before changing login details.";
                return false;
            }
            if (!ArenaJoinData.TryNormalizeUsername(username, out var normalizedName, out error)) return false;

            var address = ip?.Trim() ?? string.Empty;
            var specifiedPort = portText?.Trim() ?? string.Empty;
            var endpoint = string.Empty;
            if (address.Length == 0)
            {
                if (specifiedPort.Length != 0)
                {
                    error = "Enter an IP address when specifying a server port.";
                    return false;
                }
            }
            else
            {
                // The IP field accepts numeric IPv4/IPv6; the port has its own field.
                if (address.StartsWith("[", StringComparison.Ordinal) && address.EndsWith("]", StringComparison.Ordinal))
                    address = address.Substring(1, address.Length - 2);
                if (!IPAddress.TryParse(address, out var parsedAddress))
                {
                    error = "Enter a valid numeric IPv4 or IPv6 address.";
                    return false;
                }
                var endpointPort = port;
                if (specifiedPort.Length != 0 && (!ushort.TryParse(specifiedPort, NumberStyles.None,
                    CultureInfo.InvariantCulture, out endpointPort) || endpointPort == 0))
                {
                    error = "The server port must be between 1 and 65535.";
                    return false;
                }
                endpoint = parsedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                    ? $"[{parsedAddress}]:{endpointPort}" : $"{parsedAddress}:{endpointPort}";
            }

            Username = normalizedName;
            PreferredServerAddress = endpoint;
            SetStatus("Choose your brawler and jump in.");
            return true;
        }

        void Update()
        {
            ServerMatches?.Update();
            NetworkStats?.Update();
            if (!connecting || manager == null || manager.IsConnectedClient) return;
            if (Time.realtimeSinceStartup - connectStarted < connectionTimeout) return;
            connecting = false;
            busy = false;
            suppressDisconnect = true;
            manager.Shutdown();
            SetStatus("Connection timed out. Check the server address, selected mode and UDP port.");
        }

        public Task FindMatchAsync()
        {
            if (!EnsureClientLogin() || !CanStart()) return Task.CompletedTask;
            matchmakingTask = MatchmakeAsync();
            return matchmakingTask;
        }

        async Task MatchmakeAsync()
        {
            busy = true;
            IsPractice = false;
            IsDedicatedServer = false;
            var cancellation = new CancellationTokenSource();
            matchmakingCancellation = cancellation;
            var token = cancellation.Token;
            ISession joined = null;
            try
            {
                if (string.IsNullOrWhiteSpace(Application.cloudProjectId)
                    || Application.cloudProjectId == Guid.Empty.ToString())
                    throw new InvalidOperationException(
                        "Online matchmaking needs a linked Unity cloud project and deployed dedicated servers. " +
                        "See Docs/Networking.md. Practice and direct connection are available now.");

                SetStatus("Connecting to online services...");
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var initialization = new InitializationOptions();
                    initialization.SetEnvironmentName(servicesEnvironment);
                    await UnityServices.InitializeAsync(initialization);
                }
                token.ThrowIfCancellationRequested();
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    if (!string.IsNullOrWhiteSpace(authenticationProfile))
                        AuthenticationService.Instance.SwitchProfile(authenticationProfile);
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
                token.ThrowIfCancellationRequested();

                SetConnectionPayload();
                var queue = SelectedMode == GameMode.GemRush ? gemRushQueue : showdownQueue;
                var options = new MatchmakerOptions
                {
                    QueueName = queue,
                    TicketAttributes = new Dictionary<string, object>
                    {
                        { "mode", SelectedMode == GameMode.GemRush ? "gems" : "showdown" },
                        { "protocol", GameRules.ProtocolVersion }
                    },
                    PlayerProperties = new Dictionary<string, PlayerProperty>
                    {
                        { "brawler", new PlayerProperty(((int)SelectedBrawler).ToString()) },
                        { "username", new PlayerProperty(Username) }
                    }
                };
                // Reject a misconfigured client-hosting pool instead of allowing the SDK to
                // turn an online player into the authority. The handler uses only NGO/UTP.
                var sessionOptions = new SessionOptions
                {
                    Type = "rift-arena",
                    MaxPlayers = GameRules.Participants(SelectedMode)
                }.WithDirectNetwork().WithNetworkHandler(new DedicatedClientNetwork(this, token));

                SetStatus($"Finding {GameRules.Participants(SelectedMode)} players...");
                joined = await MultiplayerService.Instance.MatchmakeSessionAsync(options, sessionOptions, token);
                token.ThrowIfCancellationRequested();
                session = joined;
                joined = null;
                if (!manager.IsConnectedClient)
                    throw new InvalidOperationException("The arena disconnected while the match was being joined.");
                SetStatus("Connected to the dedicated arena.");
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                suppressDisconnect = true;
                await ReleaseSessionAsync(joined);
                await ReleaseCurrentSessionAsync();
                if (manager != null && manager.IsListening) manager.Shutdown();
                SetStatus("Matchmaking cancelled.");
            }
            catch (Exception exception)
            {
                suppressDisconnect = true;
                await ReleaseSessionAsync(joined);
                await ReleaseCurrentSessionAsync();
                if (manager != null && manager.IsListening) manager.Shutdown();
                SetStatus($"Online match unavailable: {exception.Message}");
                Debug.LogWarning($"[Rift Rumble] Matchmaking: {exception.Message}");
            }
            finally
            {
                if (matchmakingCancellation == cancellation) matchmakingCancellation = null;
                cancellation.Dispose();
                connecting = false;
                busy = false;
                NotifyChanged();
            }
        }

        public void CancelMatchmaking()
        {
            if (matchmakingCancellation == null)
            {
                if (!connecting) return;
                connecting = false;
                busy = false;
                suppressDisconnect = true;
                manager.Shutdown();
                SetStatus("Connection cancelled.");
                return;
            }
            SetStatus("Cancelling matchmaking...");
            matchmakingCancellation.Cancel();
        }

        public void StartPractice()
        {
            if (!EnsureClientLogin() || !CanStart()) return;
            IsPractice = true;
            IsDedicatedServer = false;
            SetConnectionPayload();
            // Practice never exposes a game on external interfaces. An ephemeral port also
            // lets an independent local dedicated server use the configured server port.
            transport.SetConnectionData("127.0.0.1", 0, "127.0.0.1");
            busy = true;
            SetStatus("Starting practice arena...");
            using var statsDriver = NetworkStats.CaptureDriver();
            if (!manager.StartHost())
            {
                busy = false;
                IsPractice = false;
                SetStatus("Practice could not start. Check the Unity Console for network configuration errors.");
            }
        }

        public void ConnectDirect(string address)
        {
            if (!EnsureClientLogin() || !CanStart()) return;
            if (!TryEndpoint(address, port, out var host, out var endpointPort))
            {
                SetStatus("Enter a server IP address, optionally followed by :port (for example 127.0.0.1:7777).");
                return;
            }
            IsPractice = false;
            IsDedicatedServer = false;
            SetConnectionPayload();
            transport.SetConnectionData(host, endpointPort);
            busy = true;
            connecting = true;
            connectStarted = Time.realtimeSinceStartup;
            SetStatus($"Connecting to {host}:{endpointPort}...");
            using var statsDriver = NetworkStats.CaptureDriver();
            if (!manager.StartClient())
            {
                connecting = false;
                suppressDisconnect = true;
                busy = false;
                SetStatus("The network client could not start.");
            }
        }

        void StartDedicatedServer()
        {
            if (!CanStart()) return;
            if (matchPrefab == null || sceneMatch == null)
            {
                SetStatus("Multi-match server assets are missing. Run Rift Rumble/Configure Multi-Match Server in the Editor.");
                Debug.LogError($"[Rift Rumble] {Status}");
                return;
            }
            IsPractice = false;
            IsDedicatedServer = true;
            ServerMatches = new ArenaServerMatches(manager, matchPrefab, serveAllModes ? null : SelectedMode);
            // Direct clients synchronize only their assigned dynamic room, while the
            // original scene match continues to support practice and cloud allocations.
            sceneMatch.NetworkObject.CheckObjectVisibility = clientId => clientId == Unity.Netcode.NetworkManager.ServerClientId;
            Application.runInBackground = true;
            transport.MaxPacketQueueSize = Mathf.Max(transport.MaxPacketQueueSize, 1024);
            transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            using var simulation = CreateServerSimulation();
            using var statsDriver = NetworkStats.CaptureDriver();
            if (!manager.StartServer())
            {
                IsDedicatedServer = false;
                ServerMatches = null;
                SetStatus($"Dedicated server failed to bind UDP port {port}.");
                Debug.LogError($"[Rift Rumble] {Status}");
            }
        }

#if UNITY_SERVER || ENABLE_UCS_SERVER
        async Task StartCloudServerAsync(string matchId, string publicIp)
        {
            if (!CanStart()) return;
            busy = true;
            IsPractice = false;
            IsDedicatedServer = true;
            Application.runInBackground = true;
            try
            {
                if (!IPAddress.TryParse(publicIp, out var published)
                    || published.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    throw new InvalidOperationException("A cloud allocation must pass its public IPv4 address with -publicIp.");
                var keyId = Environment.GetEnvironmentVariable("RIFT_SERVER_KEY_ID");
                var keySecret = Environment.GetEnvironmentVariable("RIFT_SERVER_KEY_SECRET");
                if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
                    throw new InvalidOperationException("Set RIFT_SERVER_KEY_ID and RIFT_SERVER_KEY_SECRET through the hosting provider's server secrets.");
                SetStatus("Registering the dedicated allocation...");
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var initialization = new InitializationOptions();
                    initialization.SetEnvironmentName(servicesEnvironment);
                    await UnityServices.InitializeAsync(initialization);
                }
                await ServerAuthenticationService.Instance.SignInWithServiceAccountAsync(keyId, keySecret);
                var options = new SessionOptions
                {
                    Type = "rift-arena",
                    Name = "Rift Rumble " + SelectedMode,
                    MaxPlayers = GameRules.Participants(SelectedMode),
                    IsPrivate = true
                }.WithDirectNetwork("0.0.0.0", publicIp, port)
                    .WithBackfillingConfiguration(false, false, false, 30, 1);
                // The server Session owns NGO startup on this path. Calling StartServer
                // separately would race the SDK's network module.
                using var simulation = CreateServerSimulation();
                using var statsDriver = NetworkStats.CaptureDriver();
                session = await MultiplayerServerService.Instance.CreateMatchSessionAsync(matchId, options);
                SetStatus($"Online {SelectedMode} arena is ready on UDP {port}.");
                Debug.Log($"[Rift Rumble] ONLINE_READY {matchId} {SelectedMode} UDP {port}");
            }
            catch (Exception exception)
            {
                suppressDisconnect = true;
                await ReleaseCurrentSessionAsync();
                if (manager != null && manager.IsListening) manager.Shutdown();
                IsDedicatedServer = false;
                SetStatus("Cloud server registration failed. Check the project, service-account permissions and allocation configuration.");
                // Never write credentials or full service responses to process logs.
                Debug.LogError($"[Rift Rumble] {Status} ({exception.GetType().Name})");
            }
            finally
            {
                busy = false;
                NotifyChanged();
            }
        }
#endif

        sealed class DedicatedClientNetwork : INetworkHandler
        {
            readonly ArenaConnection owner;
            readonly CancellationToken cancellation;
            readonly int generation;
            bool ownsNetwork;

            public DedicatedClientNetwork(ArenaConnection owner, CancellationToken cancellation)
            {
                this.owner = owner;
                this.cancellation = cancellation;
                generation = owner.connectionGeneration;
            }

            public async Task StartAsync(NetworkConfiguration configuration)
            {
                cancellation.ThrowIfCancellationRequested();
                if (owner.destroying || owner.connectionGeneration != generation)
                    throw new OperationCanceledException("This connection attempt is no longer active.");
                if (configuration.Type != NetworkType.Direct || configuration.Role != NetworkRole.Client)
                    throw new InvalidOperationException("The online queue must allocate a dedicated direct-network server.");
                owner.transport.SetConnectionData(configuration.DirectNetworkPublishAddress, configuration.DirectNetworkListenAddress);
                owner.connecting = true;
                owner.connectStarted = Time.realtimeSinceStartup;
                owner.SetStatus("Match found. Connecting to the dedicated arena...");
                using var statsDriver = owner.NetworkStats.CaptureDriver();
                if (!owner.manager.StartClient()) throw new InvalidOperationException("NGO could not start the client.");
                ownsNetwork = true;
                while (!owner.manager.IsConnectedClient)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!owner.manager.IsListening || owner.manager.ShutdownInProgress)
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(owner.manager.DisconnectReason)
                            ? "The dedicated server disconnected before the connection completed." : owner.manager.DisconnectReason);
                    if (Time.realtimeSinceStartup - owner.connectStarted > owner.connectionTimeout)
                        throw new TimeoutException("The dedicated server did not accept the connection in time.");
                    await Task.Yield();
                }
                owner.connecting = false;
            }

            public async Task StopAsync()
            {
                // A cancelled SDK join can finish after a newer local/direct game starts.
                // Only the handler that actually started this generation may stop NGO.
                if (!ownsNetwork || owner.connectionGeneration != generation) return;
                ownsNetwork = false;
                owner.connecting = false;
                owner.suppressDisconnect = true;
                if (owner.manager == null) return;
                owner.manager.Shutdown();
                while (owner.manager != null && owner.connectionGeneration == generation
                    && (owner.manager.IsListening || owner.manager.ShutdownInProgress))
                    await Task.Yield();
            }
        }

        public async Task LeaveAsync()
        {
            if (leaving) return;
            leaving = true;
            try
            {
                CancelMatchmaking();
                var pending = matchmakingTask;
                if (pending != null) await pending;
                connecting = false;
                await ReleaseCurrentSessionAsync();
                if (manager != null)
                {
                    manager.Shutdown();
                    // NGO shuts down on its next update. Do not allow a new connection to
                    // race the previous transport's deferred shutdown.
                    while (manager != null && (manager.IsListening || manager.ShutdownInProgress))
                        await Task.Yield();
                }
                IsPractice = false;
                IsDedicatedServer = false;
                ServerMatches = null;
                busy = false;
                if (sceneMatch != null) sceneMatch.NetworkObject.CheckObjectVisibility = null;
                SetStatus(HasLogin ? "Choose your brawler and jump in." : "Enter a username to continue.");
            }
            finally
            {
                leaving = false;
                NotifyChanged();
            }
        }

        bool EnsureClientLogin()
        {
            if (HasLogin) return true;
            if (Application.isBatchMode && TryLogin("Player", "", "", out _)) return true;
            SetStatus("Enter a username to continue.");
            return false;
        }

        bool CanStart()
        {
            if (IsBusy || destroying) return false;
            if (manager == null || transport == null)
            {
                SetStatus("NetworkManager and UnityTransport must be configured in the Arena scene.");
                return false;
            }
            if (manager.IsListening || manager.ShutdownInProgress)
            {
                SetStatus("Leave the current arena before joining another match.");
                return false;
            }
            connectionGeneration++;
            suppressDisconnect = false;
            return true;
        }

        void SetConnectionPayload()
        {
            manager.NetworkConfig.ConnectionData = new ArenaJoinData(SelectedMode, SelectedBrawler, Username).Encode();
        }

        void OnServerStarted()
        {
            NetworkStats.Start();
            if (ServerMatches != null)
            {
                busy = false;
                var modes = serveAllModes ? "all" : SelectedMode.ToString();
                SetStatus($"Dedicated multi-match server listening on UDP {port} (modes: {modes}).");
                Debug.Log($"[Rift Rumble] READY MULTI {modes} UDP {port}");
                return;
            }
            if (sceneMatch == null)
            {
                manager.Shutdown();
                busy = false;
                SetStatus("ArenaMatch is missing from the scene.");
                return;
            }
            sceneMatch.StartServer(SelectedMode, IsPractice);
            busy = false;
            if (IsDedicatedServer)
            {
                SetStatus($"Dedicated {SelectedMode} server listening on UDP {port} ({GameRules.Participants(SelectedMode)} players).");
                Debug.Log($"[Rift Rumble] READY {SelectedMode} UDP {port}");
            }
        }

        void OnClientConnected(ulong clientId)
        {
            ServerMatches?.ClientConnected(clientId);
            if (clientId != manager.LocalClientId || !manager.IsClient) return;
            if (!manager.IsServer) NetworkStats.Start();
            connecting = false;
            // Keep matchmaking busy until the Sessions operation also finishes.
            if (matchmakingCancellation == null) busy = false;
            SetStatus(IsPractice ? "Practice match • local bots" : "Connected to the dedicated arena.");
        }

        async void OnClientDisconnected(ulong clientId)
        {
            NetworkStats?.Disconnected(clientId);
            ServerMatches?.ClientDisconnected(clientId);
            if (destroying || leaving || suppressDisconnect || manager == null || manager.IsServer) return;
            if (clientId != manager.LocalClientId) return;
            connecting = false;
            if (matchmakingCancellation != null) return;
            busy = true;
            var generation = connectionGeneration;
            var reason = manager.DisconnectReason;
            await ReleaseCurrentSessionAsync();
            if (generation != connectionGeneration) return;
            busy = false;
            SetStatus(string.IsNullOrWhiteSpace(reason) ? "Disconnected from the arena." : $"Connection ended: {reason}");
        }

        async void OnTransportFailure()
        {
            connecting = false;
            busy = true;
            var generation = connectionGeneration;
            suppressDisconnect = true;
            if (matchmakingCancellation == null) await ReleaseCurrentSessionAsync();
            if (generation != connectionGeneration) return;
            busy = false;
            SetStatus("Network transport failed. Check the server and UDP port, then try again.");
        }

        async Task ReleaseCurrentSessionAsync()
        {
            var previous = session;
            session = null;
            await ReleaseSessionAsync(previous);
        }

        static async Task ReleaseSessionAsync(ISession previous)
        {
            if (previous == null) return;
            try { await previous.LeaveAsync(); }
            catch (Exception exception) { Debug.LogWarning($"[Rift Rumble] Session cleanup: {exception.Message}"); }
        }

        void SetStatus(string value)
        {
            Status = value;
            NotifyChanged();
        }

        void NotifyChanged()
        {
            if (!destroying) Changed?.Invoke();
        }

        ArenaServerNetworkSimulation CreateServerSimulation()
        {
            if (serverPingMs == 0 && serverJitterMs == 0) return null;
            return new ArenaServerNetworkSimulation(transport, serverPingMs, serverJitterMs);
        }

        bool ReadSimulationArgument(string[] args, string key, out int milliseconds)
        {
            milliseconds = 0;
            if (!HasArgument(args, key)) return true;
            if (int.TryParse(Argument(args, key), NumberStyles.None, CultureInfo.InvariantCulture,
                out milliseconds) && milliseconds <= 10000) return true;
            SetStatus($"{key} requires a whole number of milliseconds between 0 and 10000.");
            Debug.LogError($"[Rift Rumble] {Status}");
            return false;
        }

        static string Argument(string[] args, string key)
        {
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        static bool HasArgument(string[] args, string key)
        {
            foreach (var arg in args)
                if (arg.Equals(key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool TryEndpoint(string value, ushort defaultPort, out string host, out ushort parsedPort)
        {
            host = value?.Trim();
            parsedPort = defaultPort;
            if (string.IsNullOrWhiteSpace(host)) return false;
            // Brackets disambiguate an IPv6 address from its optional port.
            if (host.StartsWith("[", StringComparison.Ordinal))
            {
                var closing = host.IndexOf(']');
                if (closing < 0) return false;
                var suffix = host.Substring(closing + 1);
                if (suffix.Length > 0 && (!suffix.StartsWith(":", StringComparison.Ordinal)
                    || !ushort.TryParse(suffix.Substring(1), out parsedPort) || parsedPort == 0)) return false;
                host = host.Substring(1, closing - 1);
            }
            else if (host.IndexOf(':') == host.LastIndexOf(':') && host.Contains(":"))
            {
                var separator = host.LastIndexOf(':');
                if (!ushort.TryParse(host.Substring(separator + 1), out parsedPort) || parsedPort == 0) return false;
                host = host.Substring(0, separator);
            }
            return IPAddress.TryParse(host, out _);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            destroying = true;
            NetworkStats?.Stop();
            matchmakingCancellation?.Cancel();
            if (manager != null)
            {
                manager.OnServerStarted -= OnServerStarted;
                manager.OnClientConnectedCallback -= OnClientConnected;
                manager.OnClientDisconnectCallback -= OnClientDisconnected;
                manager.OnTransportFailure -= OnTransportFailure;
            }
            Instance = null;
        }
    }
}
