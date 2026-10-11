using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RiftRumble
{
    public struct ArenaPickup : INetworkSerializable, IEquatable<ArenaPickup>
    {
        public int Id;
        public Vector3 Position;
        public bool Power;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id); serializer.SerializeValue(ref Position); serializer.SerializeValue(ref Power);
        }
        public bool Equals(ArenaPickup other) => Id == other.Id && Position == other.Position && Power == other.Power;
    }

    public sealed class ArenaMatch : NetworkBehaviour
    {
        private const double BotFillWaitSeconds = 15;
        public static ArenaMatch Instance { get; private set; }
        [SerializeField] private NetworkBrawler brawlerPrefab;
        [SerializeField] private NetworkProjectile projectilePrefab;
        [SerializeField] private ArenaView arenaView;

        private readonly NetworkVariable<int> mode = new NetworkVariable<int>();
        private readonly NetworkVariable<uint> roomId = new NetworkVariable<uint>();
        private readonly NetworkVariable<int> phase = new NetworkVariable<int>();
        private readonly NetworkVariable<double> phaseEnds = new NetworkVariable<double>();
        private readonly NetworkVariable<double> roundEnds = new NetworkVariable<double>();
        private readonly NetworkVariable<float> stormRadius = new NetworkVariable<float>(30);
        private readonly NetworkVariable<int> scoreBlue = new NetworkVariable<int>();
        private readonly NetworkVariable<int> scoreRed = new NetworkVariable<int>();
        private readonly NetworkVariable<int> winningTeam = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<int> holdingTeam = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<double> holdEnds = new NetworkVariable<double>();
        private NetworkList<ArenaPickup> pickups;
        private readonly List<NetworkBrawler> players = new List<NetworkBrawler>(10);
        private readonly Dictionary<ulong, ArenaJoinData> approved = new Dictionary<ulong, ArenaJoinData>();
        private readonly Dictionary<NetworkBrawler, double> respawns = new Dictionary<NetworkBrawler, double>();
        private readonly Dictionary<int, GameObject> pickupViews = new Dictionary<int, GameObject>();
        private readonly List<NetworkBrawler> respawnBuffer = new List<NetworkBrawler>();
        private readonly List<int> pickupRemoveBuffer = new List<int>();
        private bool practice, initialized;
        private double nextGem, nextStormDamage, botFillAt;
        private float botTimer;
        private int pickupId;
        private LineRenderer stormLine;
        private Material stormMaterial;
        private GameObject gemViewPrefab, powerViewPrefab;
        private bool managedRoom;
        private uint configuredRoomId;
        private GameMode configuredMode;

        public uint RoomId => roomId.Value;
        public int ReservedPlayers => approved.Count;
        public GameMode Mode => (GameMode)mode.Value;
        public MatchPhase Phase => (MatchPhase)phase.Value;
        public bool AcceptingPlayers => Phase == MatchPhase.Waiting && (botFillAt == 0 || ServerNow < botFillAt);
        public bool IsPlaying => IsSpawned && Phase == MatchPhase.Playing;
        public int Capacity => GameRules.Participants(Mode);
        public IReadOnlyList<NetworkBrawler> Players => players;
        public NetworkProjectile ProjectilePrefab => projectilePrefab;
        public int BlueScore => scoreBlue.Value;
        public int RedScore => scoreRed.Value;
        public int WinningTeam => winningTeam.Value;
        public int HoldingTeam => holdingTeam.Value;
        public float StormRadius => stormRadius.Value;
        public float Remaining => (float)Math.Max(0, (Phase == MatchPhase.Playing ? roundEnds.Value : phaseEnds.Value) - ServerNow);
        public float HoldRemaining => (float)Math.Max(0, holdEnds.Value - ServerNow);
        public int AliveCount { get { int n = 0; foreach (var p in players) if (p != null && p.Alive.Value) n++; return n; } }
        private int HumanPlayers { get { int n = 0; foreach (var p in players) if (p != null && !p.IsBot) n++; return n; } }
        private double ServerNow => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : 0;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            pickups = new NetworkList<ArenaPickup>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && managedRoom)
            {
                roomId.Value = configuredRoomId;
                StartServer(configuredMode, false);
            }
            // On clients, only their assigned room is visible. The scene match remains
            // available for practice and the existing single-allocation cloud path.
            if (IsClient) Instance = this;
            mode.OnValueChanged += OnModeChanged;
            if (IsClient && !Application.isBatchMode)
            {
                if (arenaView == null) arenaView = FindFirstObjectByType<ArenaView>();
                arenaView.Build(Mode);
            }
            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += OnClientJoined;
                NetworkManager.OnClientDisconnectCallback += OnClientLeft;
            }
        }

        public override void OnNetworkDespawn()
        {
            mode.OnValueChanged -= OnModeChanged;
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientJoined;
                NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
            }
            foreach (var pair in pickupViews) if (pair.Value != null) Destroy(pair.Value);
            pickupViews.Clear();
            if (stormLine != null) Destroy(stormLine.gameObject);
            players.Clear(); respawns.Clear(); approved.Clear(); initialized = false; botFillAt = 0;
        }

        private void OnModeChanged(int oldMode, int newMode)
        {
            if (IsClient && !Application.isBatchMode) arenaView.Build((GameMode)newMode);
        }

        public void ConfigureRoom(uint id, GameMode gameMode)
        {
            managedRoom = true;
            configuredRoomId = id;
            configuredMode = gameMode;
            ConfigureVisibility(NetworkObject);
        }

        public void ReserveClient(ulong clientId, ArenaJoinData selection) => approved[clientId] = selection;
        public void ReleaseClient(ulong clientId) => approved.Remove(clientId);

        public void ConfigureVisibility(NetworkObject networkObject)
        {
            if (managedRoom) networkObject.CheckObjectVisibility = clientId => clientId == Unity.Netcode.NetworkManager.ServerClientId
                || approved.ContainsKey(clientId);
        }

        public void ConfigureAssets(NetworkBrawler brawler, NetworkProjectile projectile, ArenaView arena)
        {
            brawlerPrefab = brawler; projectilePrefab = projectile; arenaView = arena;
        }

        public void StartServer(GameMode gameMode, bool isPractice)
        {
            if (!IsServer) return;
            mode.Value = (int)gameMode; practice = isPractice; initialized = true;
            botFillAt = 0; phaseEnds.Value = 0;
            phase.Value = (int)MatchPhase.Waiting; scoreBlue.Value = scoreRed.Value = 0;
            winningTeam.Value = holdingTeam.Value = -1; pickups.Clear(); respawns.Clear(); stormRadius.Value = 30f;
            foreach (ulong client in NetworkManager.ConnectedClientsIds) OnClientJoined(client);
            if (practice)
            {
                for (int slot = players.Count; slot < Capacity; slot++) SpawnBrawler((BrawlerId)(slot % 3), slot, true, 0);
            }
            CheckReady();
        }

        public void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            if (ArenaConnection.Instance != null && ArenaConnection.Instance.ServerMatches != null)
            {
                ArenaConnection.Instance.ServerMatches.Approve(request, response);
                return;
            }
            GameMode requestedMode = ArenaConnection.Instance != null ? ArenaConnection.Instance.SelectedMode : Mode;
            string reason = null;
            if (!ArenaJoinData.TryDecode(request.Payload, out var data))
                reason = "Incompatible game version or invalid character / username.";
            else if (data.Mode != requestedMode) reason = "This server is running a different game mode.";
            else if (initialized && !AcceptingPlayers) reason = "This match has already started.";
            else if (approved.Count >= GameRules.Participants(requestedMode)) reason = "This match is full.";
            response.Approved = reason == null;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = reason ?? string.Empty;
            if (response.Approved) approved[request.ClientNetworkId] = data;
        }

        private void OnClientJoined(ulong clientId)
        {
            if (!IsServer || !initialized) return;
            if (managedRoom && !approved.ContainsKey(clientId)) return;
            foreach (var p in players) if (!p.IsBot && p.OwnerClientId == clientId) return;
            bool hasApproval = approved.TryGetValue(clientId, out var selection);
            BrawlerId id = hasApproval ? selection.Brawler :
                (ArenaConnection.Instance != null ? ArenaConnection.Instance.SelectedBrawler : BrawlerId.Bront);
            string username = hasApproval ? selection.Username :
                (ArenaConnection.Instance != null && ArenaConnection.Instance.HasLogin ? ArenaConnection.Instance.Username : "Player");
            SpawnBrawler(id, FindAvailableSlot(), false, clientId, username);
            if (!practice && botFillAt == 0)
            {
                botFillAt = ServerNow + BotFillWaitSeconds;
                phaseEnds.Value = botFillAt;
            }
            CheckReady();
        }

        private int FindAvailableSlot()
        {
            for (int slot = 0; slot < Capacity; slot++)
            {
                bool occupied = false;
                foreach (var p in players) if (p.Team.Value == (Mode == GameMode.SoloShowdown ? slot : slot % 2)
                    && (p.ServerPosition - ArenaLayout.Spawn(Mode, slot)).sqrMagnitude < 1) occupied = true;
                if (!occupied) return slot;
            }
            return Capacity - 1;
        }

        private void OnClientLeft(ulong clientId)
        {
            approved.Remove(clientId);
        }

        private void SpawnBrawler(BrawlerId id, int slot, bool bot, ulong owner, string username = null)
        {
            NetworkBrawler brawler = Instantiate(brawlerPrefab, ArenaLayout.Spawn(Mode, slot), Quaternion.identity);
            int team = Mode == GameMode.GemRush ? slot % 2 : slot;
            brawler.ConfigureServer(this, id, team, bot, bot ? GameRules.Stats(id).Name + " BOT" : username, ArenaLayout.Spawn(Mode, slot));
            ConfigureVisibility(brawler.NetworkObject);
            if (bot) brawler.NetworkObject.Spawn();
            // NGO automatically shares PlayerObjects with every peer. Room brawlers
            // retain client ownership without that global player-observer registration.
            else if (managedRoom) brawler.NetworkObject.SpawnWithOwnership(owner);
            else brawler.NetworkObject.SpawnAsPlayerObject(owner);
        }

        public void Register(NetworkBrawler player)
        {
            if (!players.Contains(player)) players.Add(player);
        }

        public void Unregister(NetworkBrawler player)
        {
            if (IsServer && initialized)
            {
                if (IsPlaying && player.Alive.Value) DropLoot(player);
                if (!player.IsBot) approved.Remove(player.OwnerClientId);
                respawns.Remove(player);
            }
            players.Remove(player);
            if (IsServer && initialized)
            {
                if (Phase == MatchPhase.Countdown && players.Count < Capacity)
                {
                    phase.Value = (int)MatchPhase.Waiting;
                    phaseEnds.Value = botFillAt;
                }
                if (IsPlaying && Mode == GameMode.SoloShowdown) CheckSoloWinner();
            }
        }

        private void CheckReady()
        {
            if (Phase != MatchPhase.Waiting || players.Count < Capacity) return;
            phase.Value = (int)MatchPhase.Countdown; phaseEnds.Value = ServerNow + 4;
        }

        private void FillWaitingSlots()
        {
            if (practice || Phase != MatchPhase.Waiting || botFillAt == 0 || ServerNow < botFillAt) return;
            int humans = HumanPlayers;
            if (humans == 0) return;
            // Approved clients may still be synchronizing. Keep their seats open;
            // the room stops accepting new reservations when its deadline expires.
            int pendingPlayers = Mathf.Max(0, approved.Count - humans);
            int missing = Capacity - players.Count - pendingPlayers;
            for (int i = 0; i < missing; i++)
            {
                int slot = FindAvailableSlot();
                SpawnBrawler((BrawlerId)(slot % 3), slot, true, 0);
            }
            CheckReady();
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsClient && !Application.isBatchMode) UpdatePickupViews();
            if (!IsServer || !initialized) return;
            if (!practice && approved.Count == 0 && HumanPlayers == 0)
            {
                botFillAt = 0;
                // Bots are server-owned and survive client disconnects. Remove them
                // so an empty managed room can be released by ArenaServerMatches.
                for (int i = players.Count - 1; i >= 0; i--)
                    if (players[i].IsBot) players[i].NetworkObject.Despawn();
                if (Phase == MatchPhase.Waiting) phaseEnds.Value = 0;
            }
            // Local dedicated servers can accept a fresh round after everyone leaves the results.
            // Cloud allocations may reclaim the process on MATCH_COMPLETE instead.
            if (!managedRoom && Phase == MatchPhase.Finished && NetworkManager.ConnectedClientsIds.Count == 0 && ServerNow >= phaseEnds.Value)
            {
                StartServer(Mode, false);
                return;
            }
            FillWaitingSlots();
            if (Phase == MatchPhase.Countdown && ServerNow >= phaseEnds.Value) BeginRound();
            if (!IsPlaying) return;
            botTimer -= Time.deltaTime;
            if (botTimer <= 0) { botTimer = .15f; UpdateBots(); }
            CollectPickups();
            if (Mode == GameMode.GemRush) UpdateGemRush(); else UpdateShowdown();
        }

        private void BeginRound()
        {
            phase.Value = (int)MatchPhase.Playing; roundEnds.Value = ServerNow + GameRules.RoundSeconds;
            nextGem = ServerNow + 1; nextStormDamage = ServerNow + 1;
            if (Mode == GameMode.SoloShowdown)
            {
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * Mathf.PI / 6;
                    AddPickup(new Vector3(Mathf.Sin(angle) * 10, 0, Mathf.Cos(angle) * 14), true);
                }
            }
        }

        private void UpdateGemRush()
        {
            if (ServerNow >= nextGem)
            {
                nextGem += 4.5;
                if (pickups.Count < 35) AddPickup(new Vector3(Mathf.Sin(pickupId * 2.4f), 0, Mathf.Cos(pickupId * 2.4f)) * 1.5f, false);
            }
            respawnBuffer.Clear();
            foreach (var respawn in respawns) if (ServerNow >= respawn.Value) respawnBuffer.Add(respawn.Key);
            foreach (var p in respawnBuffer)
            {
                respawns.Remove(p);
                if (p != null && p.IsSpawned) p.RespawnServer(new Vector3(UnityEngine.Random.Range(-4f, 4f), 0, p.Team.Value == 0 ? -17f : 17f));
            }
            int blue = 0, red = 0;
            foreach (var p in players)
            {
                if (p.Team.Value == 0) blue += p.Gems.Value; else red += p.Gems.Value;
            }
            scoreBlue.Value = blue; scoreRed.Value = red;
            int leader = blue >= GameRules.GemTarget && blue > red ? 0 : red >= GameRules.GemTarget && red > blue ? 1 : -1;
            if (leader != holdingTeam.Value)
            {
                holdingTeam.Value = leader; holdEnds.Value = leader >= 0 ? ServerNow + GameRules.GemHoldSeconds : 0;
            }
            if (leader >= 0 && ServerNow >= holdEnds.Value) Finish(leader);
            else if (ServerNow >= roundEnds.Value) Finish(blue == red ? -1 : blue > red ? 0 : 1);
        }

        private void UpdateShowdown()
        {
            float elapsed = GameRules.RoundSeconds - Remaining;
            stormRadius.Value = Mathf.Lerp(28, .5f, Mathf.Clamp01((elapsed - 15f) / 145f));
            if (ServerNow >= nextStormDamage)
            {
                nextStormDamage = ServerNow + 1;
                // Iterate backwards: elimination never removes a spawned participant.
                for (int i = players.Count - 1; i >= 0; i--)
                {
                    var p = players[i];
                    if (p.Alive.Value && new Vector2(p.ServerPosition.x, p.ServerPosition.z).magnitude > stormRadius.Value)
                        p.DamageServer(800 + Mathf.FloorToInt(elapsed * 5), null);
                }
            }
            CheckSoloWinner();
            if (IsPlaying && ServerNow >= roundEnds.Value) Finish(-1);
        }

        private void CheckSoloWinner()
        {
            int alive = 0, lastTeam = -1;
            foreach (var p in players) if (p != null && p.Alive.Value) { alive++; lastTeam = p.Team.Value; }
            if (alive <= 1) Finish(lastTeam);
        }

        public void OnEliminated(NetworkBrawler victim, NetworkBrawler killer)
        {
            if (!IsServer || !IsPlaying) return;
            DropLoot(victim);
            if (Mode == GameMode.GemRush) respawns[victim] = ServerNow + 4;
            else CheckSoloWinner();
        }

        private void DropLoot(NetworkBrawler victim)
        {
            int count = Mode == GameMode.GemRush ? victim.Gems.Value : Mathf.Max(1, victim.Power.Value / 2);
            victim.Gems.Value = 0;
            for (int i = 0; i < count && pickups.Count < 70; i++)
            {
                float angle = i * 2.4f;
                AddPickup(victim.ServerPosition + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * (.5f + i * .06f), Mode == GameMode.SoloShowdown);
            }
        }

        private void AddPickup(Vector3 position, bool power)
        {
            position = ArenaLayout.ClampPosition(position);
            if (ArenaLayout.IsBlocked(position, Mode))
            {
                bool found = false;
                for (int i = 1; i <= 20; i++)
                {
                    Vector3 candidate = ArenaLayout.ClampPosition(position + new Vector3(Mathf.Sin(i * 2.4f), 0, Mathf.Cos(i * 2.4f)) * (i * .3f));
                    if (ArenaLayout.IsBlocked(candidate, Mode)) continue;
                    position = candidate; found = true; break;
                }
                if (!found) return;
            }
            pickups.Add(new ArenaPickup { Id = ++pickupId, Position = position, Power = power });
        }

        private void CollectPickups()
        {
            for (int i = pickups.Count - 1; i >= 0; i--)
                foreach (var p in players)
                {
                    if (!p.Alive.Value || (p.ServerPosition - pickups[i].Position).sqrMagnitude > 1.1f) continue;
                    if (pickups[i].Power) { p.Power.Value++; p.Health.Value = Mathf.Min(p.Health.Value + 400, p.MaxHealth); }
                    else p.Gems.Value++;
                    pickups.RemoveAt(i); break;
                }
        }

        private void Finish(int winner)
        {
            if (Phase == MatchPhase.Finished) return;
            winningTeam.Value = winner; phase.Value = (int)MatchPhase.Finished; phaseEnds.Value = ServerNow + 8;
            Debug.Log($"[Rift Rumble] MATCH_COMPLETE room={RoomId} mode={Mode} winner={winner}");
            foreach (var p in players) if (p.IsBot) p.SetBotInput(Vector2.zero, p.ServerPosition + Vector3.forward, false, false);
        }

        private void UpdateBots()
        {
            foreach (var bot in players)
            {
                if (!bot.IsBot || !bot.Alive.Value) continue;
                NetworkBrawler enemy = null; float nearest = float.MaxValue;
                foreach (var candidate in players)
                {
                    if (!candidate.Alive.Value || candidate == bot || candidate.Team.Value == bot.Team.Value) continue;
                    float distance = (candidate.ServerPosition - bot.ServerPosition).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; enemy = candidate; }
                }
                BrawlerStats stats = GameRules.Stats((BrawlerId)bot.Character.Value);
                Vector3 goal = enemy != null ? enemy.ServerPosition : Vector3.zero;
                float pickupDistance = 60;
                for (int i = 0; i < pickups.Count; i++)
                {
                    float distance = (pickups[i].Position - bot.ServerPosition).sqrMagnitude;
                    if (distance < pickupDistance) { pickupDistance = distance; goal = pickups[i].Position; }
                }
                if (Mode == GameMode.GemRush && bot.Gems.Value >= 5) goal = new Vector3(0, 0, bot.Team.Value == 0 ? -14 : 14);
                if (Mode == GameMode.SoloShowdown && new Vector2(bot.ServerPosition.x, bot.ServerPosition.z).magnitude > stormRadius.Value - 3) goal = Vector3.zero;
                Vector3 move = goal - bot.ServerPosition; move.y = 0;
                if (move.magnitude < .7f) move = Vector3.zero; else move.Normalize();
                if (enemy != null && nearest < stats.Range * stats.Range * .4f && (BrawlerId)bot.Character.Value != BrawlerId.Bront && pickupDistance > 9)
                    move = (bot.ServerPosition - enemy.ServerPosition).normalized;
                Vector2 input = new Vector2(move.x, move.z);
                Vector3 ahead = ArenaLayout.Move(bot.ServerPosition, input, stats.MoveSpeed, .2f, Mode);
                if (input.sqrMagnitude > .1f && (ahead - bot.ServerPosition).sqrMagnitude < .15f)
                {
                    float turn = Mathf.Sin(Time.time * .4f + bot.NetworkObjectId) > 0 ? 1 : -1;
                    input = new Vector2(-input.y, input.x) * turn;
                }
                bool attack = enemy != null && nearest < stats.Range * stats.Range &&
                    ((BrawlerId)bot.Character.Value == BrawlerId.Pip || ArenaLayout.HasLineOfSight(bot.ServerPosition, enemy.ServerPosition, Mode));
                bot.SetBotInput(input, enemy != null ? enemy.ServerPosition : bot.ServerPosition + move, attack, attack && bot.SuperCharge.Value >= 1);
            }
        }

        private void UpdatePickupViews()
        {
            if (gemViewPrefab == null)
            {
                gemViewPrefab = Resources.Load<GameObject>("RiftRumble/Environment/PickupGem");
                powerViewPrefab = Resources.Load<GameObject>("RiftRumble/Environment/PowerCell");
            }
            pickupRemoveBuffer.Clear();
            foreach (var pair in pickupViews)
            {
                bool exists = false;
                for (int i = 0; i < pickups.Count; i++) if (pickups[i].Id == pair.Key) { exists = true; break; }
                if (!exists) pickupRemoveBuffer.Add(pair.Key);
            }
            foreach (int id in pickupRemoveBuffer) { Destroy(pickupViews[id]); pickupViews.Remove(id); }
            for (int i = 0; i < pickups.Count; i++)
            {
                ArenaPickup pickup = pickups[i];
                if (!pickupViews.TryGetValue(pickup.Id, out GameObject view))
                {
                    view = Instantiate(pickup.Power ? powerViewPrefab : gemViewPrefab);
                    view.name = pickup.Power ? "Power cell" : "Gem";
                    pickupViews.Add(pickup.Id, view);
                }
                view.transform.position = pickup.Position + Vector3.up * (.65f + Mathf.Sin(Time.time * 3 + pickup.Id) * .12f);
                view.transform.rotation = Quaternion.Euler(0, Time.time * 60 + pickup.Id, 0);
            }
            if (Mode == GameMode.SoloShowdown && Phase >= MatchPhase.Playing)
            {
                if (stormLine == null)
                {
                    stormLine = new GameObject("Storm boundary").AddComponent<LineRenderer>();
                    stormMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(.75f, .2f, 1) };
                    stormLine.sharedMaterial = stormMaterial; stormLine.loop = true; stormLine.positionCount = 96;
                    stormLine.startWidth = stormLine.endWidth = .35f; stormLine.useWorldSpace = true;
                }
                for (int i = 0; i < 96; i++)
                {
                    float angle = i * Mathf.PI * 2 / 96;
                    stormLine.SetPosition(i, new Vector3(Mathf.Sin(angle) * stormRadius.Value, .12f, Mathf.Cos(angle) * stormRadius.Value));
                }
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (stormMaterial != null) Destroy(stormMaterial);
            base.OnDestroy();
        }
    }
}
