# Rift Rumble networking

All gameplay messages, input, authoritative snapshots, combat, spawns, pickups and match state use **Unity Netcode for GameObjects over UnityTransport**. The Unity Multiplayer Services SDK authenticates and matches players to registered dedicated server allocations. The client Session network handler only accepts a direct client role and starts the existing NGO client. There is no alternative gameplay transport, peer-hosted online fallback or simulated online queue.

## Available play paths

| Path | Authority | Cloud setup | Participants |
| --- | --- | --- | --- |
| Practice | Local NGO host | None | Player plus bots, 6 or 10 total |
| Direct connection | Shared NGO dedicated server, multiple isolated matches on one port | None | 6 participants per Gem Rush match; 10 per Solo Showdown match, with bot fill after 15 seconds |
| Find match | Allocated NGO dedicated server | Linked Unity project, deployed queues, hosting allocation | 6 or 10 participants in separate queues, with the same room bot-fill timeout |

Practice uses a loopback-only listener with an ephemeral UDP port and fills with bots immediately. A direct or matchmade dedicated arena starts a 15-second waiting timer when its first human brawler joins. If the room is still incomplete at that deadline, the server fills unreserved slots using the existing bots, then starts the normal four-second countdown. A full human room starts the countdown immediately. Gem Rush always starts with six participants, balanced three per team; Solo Showdown starts with ten distinct participants. For example, three connected Gem Rush players get three bots. Later arrivals do not reset the room's timer. Every character is unlocked.

Clients approved before the deadline retain their reserved seats while synchronizing; those seats are filled with bots only if their reservations are released. After the deadline, new direct clients are assigned to another waiting room. Bots use the existing server-owned brawler prefab, AI and room visibility rules, and are despawned when all humans and reservations leave so empty rooms still close independently.

This timeout begins after the first gameplay connection, not while waiting in the cloud matchmaking queue. The current cloud pool rules still request full human groups before allocating a server; changing cloud queue population rules is separate from filling an already connected room. Rebuild the server to apply the timeout; this feature does not change the protocol or replicated state layout.

The repository implements the client connection flow and dedicated server executable entry point. **Live cloud matchmaking cannot run until a Unity cloud project, queue configuration and hosting provider allocation are deployed.** Those account-bound resources and credentials are not included or provisioned. An unlinked project displays a setup message instead of pretending to search online.

## Local dedicated play

For Linux, use **Rift Rumble → Build Linux Headless Server**, which selects Unity's Dedicated Server subtarget and writes `Builds/LinuxServer/RiftRumbleServer.x86_64` and `StartServer.sh`. The runtime requires glibc 2.35 or newer. The [Linux server guide](LinuxServer.md) covers deployment, UDP firewall rules, client connections, background launch, and the packaged systemd service example.

Build the arena scene as a normal desktop player, or use Unity's Dedicated Server build target. Run the built player with the following game arguments. These commands launch your compiled game; they do not use Unity CLI.

The Editor menu **Rift Rumble → Build Windows Client** writes `Builds/Client/RiftRumble.exe`; **Build Windows Headless Server** writes `Builds/Server/RiftRumbleServer.exe` and `StartServer.cmd`. Copy the whole `Builds/Server` folder to the server machine, keeping its data and runtime folders together. The launcher passes `-batchmode -nographics -arenaServer`, serves both modes on UDP 7777 by default, and writes `server.log` alongside the executable. It accepts the same game arguments as the executable and works from any current directory. Headless processes retain the bootstrap's 30 FPS cap, matching the 30 Hz network tick.

From the project folder, run:

```powershell
.\Builds\Server\StartServer.cmd
# Or allow only Showdown matches on that same port:
.\Builds\Server\StartServer.cmd -mode showdown -port 7777
```

Clients on the same computer connect to `127.0.0.1:7777`; clients on another computer use the server machine's reachable IPv4 address. Enter that IP and port in the client login, select your desired game mode, then choose **Join Server**. The server assigns each new client to the first waiting match of that mode with an available reserved slot, creating another match when necessary. Gem Rush accepts up to six humans per match; Solo Showdown accepts up to ten. Each room fills its remaining unreserved seats with bots 15 seconds after its first human joins. A room whose waiting deadline has expired, or a match in countdown, playing, or results, is closed to new reservations. Local direct play does not require Unity cloud services.

Every room has its own `ArenaMatch` network object, player list, timers, pickups, scores, storm, and results. Brawlers replicate their owning match reference, and authoritative combat/projectiles use that match. NGO visibility checks limit match, brawler, projectile, network-variable, and gameplay RPC replication to that room's members. Rooms use the same mathematical arena coordinates; gameplay collision does not rely on shared Unity physics colliders. The scene match is hidden from direct clients and remains available for Practice/cloud allocations. Empty rooms are despawned independently after reservations and brawlers are released; leaving one room does not stop the shared listener or other rooms. Approval timeouts release seats for connections that never finish joining.

Room brawlers use `NetworkObject.SpawnWithOwnership(clientId)` rather than NGO's PlayerObject registration. In the installed NGO version, registering a PlayerObject adds other players as observers even when a visibility callback excludes them. Ordinary owned objects preserve owner-only input RPC permissions and disconnect cleanup without that global observer expansion. The game already identifies its local brawler through `NetworkBrawler.Local`; Practice and cloud allocations keep their existing PlayerObject behavior. Server startup raises the transport's packet queue to at least 1024 for concurrent room traffic.

The one server loop runs every room's existing authoritative simulation. Match timers share NGO's server clock but store separate deadlines; no room needs another process, port, NetworkManager, gameplay scene, or physics world. There is no configured room-count cap in this implementation; capacity depends on the server machine and network traffic. The 22-client verification below is a functional check, not a maximum-capacity benchmark.

Rebuild **both clients and server** for this change. Protocol 3 and the registered `ArenaMatch.prefab` replace the previous replication layout, so protocol-2 binaries are incompatible. **Rift Rumble → Configure Multi-Match Server** creates/registers the room prefab and connects it to the existing arena scene; this setup has already been applied in this project.

```powershell
# One server process and port for multiple simultaneous matches in both modes.
.\RiftRumbleServer.exe -batchmode -nographics -arenaServer -port 7777 -logFile server.log

# Each player selects Gem Rush and connects to 127.0.0.1:7777 in the menu,
# or passes its mode and character at startup. Launch six clients for a full match.
.\RiftRumble.exe -arenaConnect 127.0.0.1:7777 -mode gems -character vex -username PlayerOne

# Showdown clients use the same endpoint and are grouped separately from Gem Rush.
.\RiftRumble.exe -arenaConnect 127.0.0.1:7777 -mode showdown -character pip

# Local player plus bots, no cloud project required.
.\RiftRumble.exe -arenaPractice -mode showdown -character bront
```

Builds compiled with `UNITY_SERVER` or `ENABLE_UCS_SERVER` automatically start a dedicated server. Supported arguments are `-arenaServer`, `-arenaConnect <IP[:port]>`, `-arenaPractice`, `-mode gems|showdown`, `-port <1..65535>`, `-character bront|vex|pip`, `-username <name>`, `-profile <authentication-profile>`, and the optional server simulation arguments `-ping <ms>` / `-jitter <ms>` described below. Direct servers accept both modes when `-mode` is omitted or set to `all`; `-mode gems` or `-mode showdown` limits their rooms to that mode. Cloud allocations additionally use `-matchId <match-id>` and `-publicIp <public-ipv4>` and continue to run one allocated match. Dedicated servers listen on all IPv4 interfaces. Direct connections accept numeric IPv4 addresses and bracketed IPv6 addresses with optional ports; for this IPv4 server listener, use its IPv4 endpoint. Cross-machine clients need the server's reachable IPv4 address and UDP port allowed through its firewall/NAT.

The log line `[Rift Rumble] READY MULTI <all|mode> UDP <port>` marks the shared direct listener. `ROOM_CREATED`, `ROOM_ASSIGNED`, `ROOM_CLOSED`, and `MATCH_COMPLETE` include room IDs for diagnostics. The server validates protocol, mode, character, bounded name and room capacity before spawning the player's brawler. The approved username is replicated through the existing `FixedString64Bytes` network variable. All clients and the server must use matching protocol-3 builds.

Visual clients enter their name in the startup login panel. Command-line practice/direct clients can supply `-username <name>` and otherwise use `Player`; dedicated servers do not show login. This is a display-name entry flow, not an account/password authentication system. Cloud anonymous authentication is unchanged.

## Optional server ping and jitter

Rebuild the server with **Rift Rumble → Build Windows Headless Server**, then add `-ping <ms>` and/or `-jitter <ms>` to its usual launch command:

```powershell
# Add roughly 150 ms of round-trip latency, varying each direction by up to 20 ms.
.\Builds\Server\StartServer.cmd -ping 150 -jitter 20

# Fixed added latency only, or jitter only.
.\Builds\Server\StartServer.cmd -ping 200
.\Builds\Server\StartServer.cmd -jitter 30
```

Both values are optional whole milliseconds from **0 to 10000**, defaulting to zero. Omitting both (or setting both to zero) uses the original transport without a simulation hook. These options require a dedicated server (`-arenaServer` or a server build) and can also be passed to cloud allocation processes. Invalid or missing values are logged and prevent server startup.

`-ping` is **additional round-trip latency**, divided equally between received game packets and sent game packets; an odd value rounds up to the next even millisecond. Existing network latency and the server's 30 Hz scheduling remain additive, so observed RTT will not exactly equal this argument. `-jitter` is random variation of up to the supplied milliseconds **per direction**, clamped so delay never becomes negative. Jitter alone therefore produces delays from zero up to that value and increases average latency. All clients and rooms on this server share the configured conditions; restart the server to change them.

`ArenaServerNetworkSimulation` preserves UnityTransport's default settings and reliable/unreliable pipelines and enables [Unity Transport's simulator stage](https://docs.unity.cn/Packages/com.unity.transport%402.6/manual/pipelines-usage.html) explicitly, including in release server builds. It delays real gameplay traffic, including input, replication and reliable gameplay messages; it adds no configured packet loss. Transport-level connection handshakes/heartbeats and Unity service HTTP calls are outside the gameplay pipelines. Very large delays can trigger the existing connection timeouts. The simulator buffers up to 400 packets per pipeline/direction/connection; if that buffer fills, Unity Transport logs a warning and passes excess packets without added delay. Treat such a warning as an invalid latency measurement and reduce delay/load.

## Gameplay connection counter

The top-right gameplay HUD shows server RTT in milliseconds and incoming/outgoing **reliable packet loss** estimates, refreshed once per second. IN is measured by the client's existing Unity Transport reliable pipeline; OUT is measured by the server's reliable pipeline for that client and reported in one small unreliable statistics message per second. The calculation matches UnityTransport's dropped/received counter deltas and is clamped to 0–100%. Sequence gaps can include packets that later arrive out of order or are retransmitted; these estimates do not measure all UDP traffic or unreliable snapshots.

Practice shows 0 ms and 0% because its local host connection does not traverse the transport. Missing samples and server reports older than three seconds show dashes. Each connection starts with fresh samples, and each server report goes only to its own client, including when multiple rooms share the listener.

Rebuild the client to get the HUD and the server to enable OUT reports. An older protocol-3 server still accepts the updated client, but OUT remains unavailable. This counter preserves the transport pipelines and protocol version, including the optional ping/jitter simulator.

## Configure online queues

1. Link this Unity project to your Unity cloud project in the Editor's Services/Project Settings and enable anonymous player authentication and matchmaking in that project's **production** environment. The `ArenaConnection` Inspector exposes the environment and queue names if you use another environment.
2. Create queue **`rift-gem-rush`**, with maximum ticket size **1**. Add a default pool and paste `Assets/Scripts/Networking/Config/gem-rush.rules.json` into the pool's JSON rules editor. The pool requires exactly two teams of three players. Use a ticket timeout appropriate to your player population, for example 120 seconds.
3. Create queue **`rift-solo-showdown`**, also with maximum ticket size **1**, and use `Assets/Scripts/Networking/Config/solo-showdown.rules.json`. This pool requires ten individual players.
4. Configure both pools for **Hosting via Cloud Code**, connected to a deployed allocation module for your hosting provider. Set the module name and allocation/poll function names. Do not choose Client Hosting; this game's online path requires a dedicated server. Do not configure Relay or Distributed Authority.
5. Deploy the queue/pool settings to the same project and environment as the game. Launch independent clients with `-profile player1`, `-profile player2`, etc. when validating multiple authenticated players on one machine. Authentication profiles separate locally cached anonymous identities; they do not create more players inside one running client.

The JSON files are **pool rules**, not whole queue `.mmq` deployment files. Queue names, environment, ticket timeout and provider-specific hosting fields must be set in the dashboard. The application sends ticket attributes `mode=gems|showdown` and `protocol=3`, plus player properties. It uses the two queue names to separate modes. Character selection is also checked through NGO connection approval, so the cloud player property is not gameplay authority. Gem Rush teams are balanced by the game server when clients connect; the first version does not use cloud team assignments for parties.

## Hosting allocation contract

The existing cloud allocation contract remains **one fresh server process per allocated match**. This section applies specifically to the `-matchId`/Find Match path; direct connections use the shared multi-match server above. The provider module must choose a reachable public IPv4 address and UDP port, and launch the matching arena:

| Queue | Server arguments | Required human slots |
| --- | --- | --- |
| `rift-gem-rush` | `-arenaServer -mode gems -port <allocated-port> -matchId <match-id> -publicIp <public-ipv4>` | 6 |
| `rift-solo-showdown` | `-arenaServer -mode showdown -port <allocated-port> -matchId <match-id> -publicIp <public-ipv4>` | 10 |

Use a **Dedicated Server build**, or a headless desktop server build compiled with **`ENABLE_UCS_SERVER`**, for cloud allocations. The installed Unity server assemblies explicitly support both defines. The project's Windows headless server build passes `ENABLE_UCS_SERVER` through `BuildPlayerOptions.extraScriptingDefines`, without changing persistent player settings. A normal desktop player using `-arenaServer` remains suitable for local direct play. Inject `RIFT_SERVER_KEY_ID` and `RIFT_SERVER_KEY_SECRET` through the provider's secret environment variables. These are Unity service-account credentials scoped to this project/environment and the server Session/matchmaking operations. They are never serialized into game assets or sent by clients. Without `-matchId`, the server uses the local direct path and needs no credentials.

The server authenticates using `ServerAuthenticationService.SignInWithServiceAccountAsync`, calls `MultiplayerServerService.CreateMatchSessionAsync` for the supplied match ID, and publishes the direct endpoint. The server Session starts NGO as a server. All server-only references are guarded by `UNITY_SERVER || ENABLE_UCS_SERVER`, matching the SDK assemblies' build constraints. The local direct path and online clients never call server authentication.

The allocation polling function must remain pending until the log contains **`[Rift Rumble] ONLINE_READY <match-id> <mode> UDP <port>`**, then return Unity's **IpPortAssignment** with that match ID and reachable IP/port. Merely seeing the earlier `READY` line is insufficient: clients join the server Session by match ID, so registration must finish first. The client joins that Session and then connects NGO directly. It must never receive a Relay assignment or an unrelated game's IP. The allocation module also owns failed-start cleanup, process lifetime and returning the allocation after the match. Backfill is disabled because joining a round already in progress is not part of the game's match flow.

The game logs `[Rift Rumble] MATCH_COMPLETE room=<room-id> mode=<mode> winner=<team>` when results are available. Give clients time to view the results before reclaiming the cloud allocation. Shared direct servers create independent waiting rooms for new clients and close empty rooms while keeping the listener running. Cloud hosting must allocate a fresh process and register a new match ID for each new match; do not return an old process still registered to the previous match ID.

Unity provides allocation adapters for several hosting providers. Select and configure the adapter for your actual provider rather than inventing credentials or hard-coding an always-available server:

- [Unity's provider integration guide](https://docs.unity.com/en-us/mps-sdk/matchmaking/mm-hosting-providers)
- [Unity's allocation module examples and Editor deployment workflow](https://github.com/Unity-Technologies/matchmaker-hosting-providers)
- [Sessions matchmaking and dedicated direct-network configuration](https://docs.unity.com/en-us/mps-sdk/adv-matchmaking)
- [Queue and pool setup](https://docs.unity.com/en-us/mps-sdk/matchmaking/get-started-mm)

The game's admission payload validates protocol, mode, character and capacity; it does not include a hosting-provider-issued admission token or a server-side authenticated roster. Before a public production deployment, bind allocated player identities to short-lived join credentials and validate them in NGO approval using the chosen provider's trusted allocation data. This is an integration boundary, not a second gameplay networking stack.

## Connection lifetime and diagnostics

`ArenaConnection.FindMatchAsync` initializes services, signs in anonymously, submits a cancellable ticket and connects the existing NGO client through its Session network handler. Cancelling removes the ticket through the SDK; the same Cancel action can also stop a pending direct connection. Failures clean up the joined Session and network listener. Leaving waits for NGO's deferred shutdown before allowing another match. A connection has a 20-second timeout by default; the Inspector can change it.

When a real online search fails, check the project link, environment, queue names, hosting module deployment, allocation logs, server ONLINE_READY line and public UDP reachability in that order. Direct mode can verify the same NGO server independently of the cloud allocation flow. Online matchmaking was not end-to-end verified against a provisioned cloud project during implementation.

## Package versions

The project uses ordinary Unity registry references: `com.unity.netcode.gameobjects` **2.7.0**, `com.unity.services.multiplayer` **2.1.1** and `com.unity.services.authentication` **3.6.0**. Unity Package Manager resolves their dependencies. Multiplayer Services 2.1 or newer is required for the Cloud Code `IpPortAssignment` flow; the Editor's bundled version 2.0.0 is insufficient. These APIs were checked against the installed package sources.
