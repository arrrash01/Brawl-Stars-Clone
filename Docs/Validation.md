# Implementation validation

Validated on 2026-10-04 in Unity 6000.3.2f1 on Windows. No test suites or test source files were added, following the project instructions. Checks used the running Unity Editor and compiled game processes, through the existing Unity MCP connection; Unity CLI was not used.

## Observed behavior

- The character selection screen renders all three original models with usable character, mode, practice, matchmaking and direct-connect controls. All three characters are unlocked.
- Gem Rush practice spawns six participants split into teams of three. Bots move, attack, collect gems, take damage and respawn.
- Solo Showdown practice spawns ten participants. Elimination switches the camera to spectating; a complete round reaches one survivor and displays results. Returning to character selection shuts down NGO cleanly.
- A built headless server plus five built headless clients and one visual Editor client ran a six-human Gem Rush match. All three character types replicated, with three players on each team and no bots. The visual client reported `IsServer=false`.
- The Vex owner moved from `(-4,0,-17)` to `(-4,0,-8)`; the authoritative snapshot agreed. Injecting a temporary three-unit local position error to `(-1,0,-8)` was corrected back to `(-4,0,-8)` by authoritative snapshots and input replay. Pending input counts remained bounded (observed 2–4), and settled positional error was zero.
- The dedicated match produced no Unity Console errors or warnings; logs from the server and all five standalone clients contained no exception/error/warning entries. The validation processes were stopped afterward and the Editor left out of Play Mode.
- The direct-connect dialog initializes its endpoint field correctly. Find Match in the unlinked project reports missing cloud configuration and returns to an idle state.
- Generated meshes, character/environment prefabs, materials and metadata were checked for broken references; no generated asset defects were found.

Screenshots are saved in `Captures/selection-final.png`, `gem-rush.png`, `solo-showdown.png`, `results.png` and `dedicated-match.png`. Local multiplayer logs are in `Builds/Validation`.

## Build and external limits

Windows client and headless server builds both succeeded with zero errors. Each build writes its summary to `Builds/Client/build-result.txt` or `Builds/Server/build-result.txt`. The server build includes `ENABLE_UCS_SERVER`, so the installed Multiplayer Services server-authentication and Session code is compiled as well.

Unity reports that dashboard services require a linked cloud project. Live cloud allocation and matchmaking have **not** been deployed or verified: the project currently has no cloud project ID, provider allocation module, provisioned queues or server credentials. Complete the account-specific setup in [Networking.md](Networking.md).

The network check used local processes without simulated latency or packet loss. Mobile controls are implemented, but Android/iOS builds and physical-device input have not been verified. The current server binary is a Windows headless desktop build, not a Linux server artifact.

## Startup login update

The startup panel was visually checked at 1920×1080. Its Continue button rejected a blank username, an invalid IP, a port without an IP, and port 70000; a trimmed username with empty server fields opened selection. Practice displayed `Yousef` in both the nameplate and local HUD. Name labels render plain text.

Both protocol-2 Windows builds succeeded with zero errors (the existing unlinked-cloud warning remains). A separate headless server on port 7781 accepted a standalone client named `GuestTwo` and the Editor client named `Yousef`, joined through the new login and Join Server UI. The Editor received both names with their respective Pip/Bront characters while running as a non-authoritative client. No runtime errors or warnings were observed. Logs are in `Builds/Validation/Login`, and the startup screenshot is `Captures/login.png`. No test source files were created.

## Release aiming and quick-fire update

The original aim-stick bug was reproduced: dragging set the continuous attack flag, while pointer-up erased the aim vector and cleared attack. The corrected pointer flow retains aim and emits one release request. Human releases are queued with their captured world target until the 30 Hz input tick; stale movement compaction preserves action commands in sequence order.

Manual runtime checks confirmed that holding aim kept all three ammo slots, release consumed one, and Pip's target stayed at the same world point. PC Input System mouse events confirmed held aiming at a farther enemy, release damage to that enemy, and no retargeting from idle mouse movement. Both an on-screen quick tap and a quick mouse click hit the nearest enemy instead, ignoring a closer friendly player. With enemies moved out of range, quick-fire produced a projectile along the remembered aim. Disabling an active aim control cancelled its gesture without queuing fire.

PC event checks used a temporary synthetic mouse because the open Device Simulator disabled the native mouse. Temporary input-routing/background settings were restored and the synthetic device was removed. No test source files or debug behavior were added to the game.

The updated Windows client and server builds succeeded with zero errors (client: two build warnings; server: the existing unlinked-cloud warning). A separate server on port 7782 ran Gem Rush with five standalone clients and the Editor client. The Editor was a non-authoritative Pip owner (`IsServer=false`); an on-screen quick tap at 5.84 units selected the nearest enemy, and the server replicated its health change from 7,200 to 5,650. This confirms the quick-fire flag travels through NGO to authoritative targeting. The Console and standalone process logs contained no runtime errors or warnings. Logs are in `Builds/Validation/Aiming`; all validation processes were stopped and the Editor left outside Play Mode.

## Aim direction follow-up (2026-10-05)

The previous behavior was reproduced in Play Mode: an auto-shot fired left while the local guide returned right, and moving the player left the lob landing point fixed in world space. Retained human aim now stores a planar offset from the player. Release requests copy that offset, and the guide reconstructs its target from the current player position. The server confirms accepted quick-fire aim through a reliable owner RPC; sequence and respawn-epoch guards prevent older confirmations from replacing a newer manual aim or a new life.

Play Mode checks confirmed all three quick-fire guides face the selected enemy: Pip's landing offset was `(-3,0,0)`, Vex's line extended 12 units left, and Bront's cone faced left. During real NGO host movement, the player and Pip's landing marker both translated 2.28 units while retaining a four-unit forward aim offset. An older quick-fire confirmation left a newer forward manual aim unchanged. The Console contained no runtime errors or warnings. No test source files were added.

The updated Windows builds succeeded with zero errors (server: 38 warnings; client: 37 warnings). Build warnings included URP cached-array capacity warnings when switching from Android to Windows and the existing missing cloud-project link. The Editor's original Android build target was restored.

A separate server on port 7783 ran a six-human Gem Rush match. The non-authoritative Editor Pip client changed from a rightward manual aim to the server-confirmed offset `(-1.14,0,5.50)` after a quick tap; its arc endpoint matched that offset, and the selected enemy's health changed from 7,200 to 5,650. Moving afterward translated both client and landing marker 1.90 units left while preserving the confirmed offset. No runtime errors or warnings appeared in the Console or standalone logs. Logs are in `Builds/Validation/AimFollow`; all validation processes were stopped and the Editor left outside Play Mode.

## Headless server packaging (2026-10-05)

The Windows server build now generates `Builds/Server/StartServer.cmd`. It supplies the headless flags and forwards mode/port arguments; logs are written next to the server executable. Removed `ArenaConnection.Awake`'s redundant 120 FPS assignment so `ArenaBootstrap` retains its 30 FPS cap for batch processes.

Both Windows builds succeeded with zero errors (server: 40 warnings; client: 37 warnings). Warnings include the existing unlinked cloud project and URP array-capacity messages after switching platform. The Windows Dedicated Server module is not installed; this remains a Windows desktop server build launched with `-batchmode -nographics` and compiled with `ENABLE_UCS_SERVER`.

Launching `StartServer.cmd -mode gems -port 7784` produced `[Rift Rumble] READY GemRush UDP 7784`, a Null graphics device, no game window (`MainWindowHandle=0`), and a UDP listener on `0.0.0.0:7784`. Five standalone clients plus the visual Editor client entered `Playing` with six human brawlers, three on each team, and all three character types. The Editor reported `IsClient=true`, `IsServer=false`. The server and standalone client logs contained no exception/error/warning/failure entries; the Unity Console had no errors. Existing URP warnings persisted in the Editor. Logs are in `Builds/Validation/Headless`. Validation processes were stopped, Play Mode exited, and the original Android build target restored. No test source files were created.

## Multiple matches on one port (2026-10-05)

Protocol 3 adds a replicated owning-match reference to each brawler and registers `Assets/Prefabs/Networking/ArenaMatch.prefab`. A shared direct listener assigns approved players to separate rooms by mode, reserves slots before synchronization, and despawns empty rooms. Existing character/projectile prefabs are reused. No test suites or test source files were created; validation used real builds, live peers, and temporary inspection through Unity MCP.

An initial 22-client check found that NGO PlayerObject registration adds global player observers despite visibility callbacks. Room brawlers now use ordinary client-owned spawning. With the corrected code, the Editor server on UDP 7785 ran two six-player Gem Rush rooms and one ten-player Showdown room, all initially `Playing`. Every brawler referred to its assigned match. Comparing each brawler's observers against room membership across all 22 clients found **zero visibility leaks**. A spawned projectile likewise had zero foreign observers. Cross-room damage was rejected (foreign health remained 4,000); damage within the attacker's room changed its target from 4,000 to 3,900. Disconnecting all six clients from one Gem Rush room closed only that room: the listener remained active with 16 clients, the other Gem Rush still playing, and Showdown independently reaching results. Corrected-run client logs are in `Builds/Validation/MultiMatch/Isolated`; initial failed-attempt logs remain in the parent directory.

Final Windows client and server builds both succeeded with zero errors (client: 37 warnings; server: 42 warnings). Existing unlinked-cloud and URP platform-switch warnings remain. The final compiled server was launched through `StartServer.cmd -port 7786` from a different working directory. It logged `READY MULTI all UDP 7786`, used a Null graphics device, had no game window, and bound `0.0.0.0:7786`. Twenty-one standalone peers plus the Editor client populated two Gem Rush rooms and one Showdown room. The visual client was a non-authoritative client in Gem Rush room 3, with six players and **only room 3 spawned locally**. All its brawlers referenced that room. Leaving and reconnecting as Showdown created room 4 in `Waiting` with one participant; only room 4 was visible, and leaving it closed that room independently.

Final standalone/server logs in `Builds/Validation/MultiMatch/Final` contained no exception/error/warning/failure/deferred-spawn entries. The Unity Console had no errors in the final run. Gem Rush Practice was checked with six participants and correct owning-match references; after leaving the shared direct server, Showdown Practice started with ten participants and a local owner. Validation processes were stopped, the Editor left outside Play Mode, and the Android build target restored. Cloud allocation remains dependent on external provisioning and was not exercised; its existing single-allocation startup is retained and compiled in the server build. The local 22-client checks do not establish a production capacity limit or simulate latency/packet loss.

## Optional server ping and jitter (2026-10-05)

Added optional dedicated-server arguments `-ping <ms>` and `-jitter <ms>` using a scoped UnityTransport driver constructor and Unity Transport's real packet simulator. The project compiled in the Editor without errors. The Windows headless server rebuilt successfully with zero errors and 37 build warnings; the original Android target was restored. The existing protocol-3 client build was reused. No test source files or test suites were created.

The release server on UDP 7790 logged `NETWORK_SIMULATION ping=150ms jitter=+/-20ms per-direction-delay=75ms`, then accepted five standalone clients and the Editor client into one six-human Gem Rush match in `Playing`. The Editor reported `IsServer=false`, seven to ten pending inputs in sampled observations, and zero settled prediction error. Two seconds of owner movement changed its position from `(4,0,17)` to `(4,0,20.52)`; the received authoritative snapshot subsequently matched `(4,0,20.52)` exactly.

The local baseline server on UDP 7791 started without a simulation log and accepted the Editor client with a sampled transport RTT of 32 ms. The simulated match initially sampled 154 ms; movement sampling recorded ordinary values of 152–222 ms plus a 1,295 ms spike during in-memory Editor code execution. These functional observations include Editor stalls, frame scheduling and RTT estimation; they are not a calibrated latency or capacity benchmark.

A jitter-only release server (`-jitter 30`, UDP 7792) logged zero base delay and 30 ms per-direction jitter and opened its listener. A launch with `-ping -1` logged the argument validation error and never logged `READY`. Omitted options use the default constructor, and the temporary constructor hook was confirmed restored after startup. The simulated, baseline, jitter-only and five standalone client logs contained no runtime errors/warnings or simulator-buffer overflow warnings; the Unity Console likewise had no runtime errors/warnings. Logs and the movement observation are in `Builds/Validation/NetworkSimulation`. Validation peers were stopped and the Editor left outside Play Mode. Cloud allocation was not exercised.

## Room bot fill (2026-10-10)

Added a per-room 15-second deadline from the first human join for Gem Rush and Solo Showdown. Bot fill reuses the existing network brawler prefab, AI and visibility filters, preserves pending human reservations, and removes server-owned bots when no humans/reservations remain. Full human rooms retain their immediate four-second countdown; Practice still fills immediately. The replicated state layout and protocol are unchanged.

The current runtime sources compiled with zero errors or warnings for Editor, Windows client and headless server configurations, using the bundled Roslyn C# compiler and cached Unity response-file references. Compilation outputs are isolated under `Builds/Validation/BotFill`; existing Unity assembly caches and built executables were not overwritten. No tests were written or run, and Unity CLI was not used.

Unity Editor was not running and no connected Unity MCP tools were available, so live multiplayer timing, bot replication and disconnect cleanup were not exercised. Rebuild the headless server in Unity to apply this implementation to the existing executable. Cloud queue population rules remain unchanged; the timeout applies after a client connects to a room.

## Gameplay connection counter (2026-10-10)

Added a top-right uGUI/TextMeshPro counter for server RTT and incoming/outgoing reliable-packet loss. Samples use the installed Unity Transport reliable pipeline; OUT is reported to the corresponding client once per second. The scoped driver-constructor hook delegates existing construction unchanged and supports the optional server ping/jitter simulator. The protocol, scene, prefabs and gameplay RPCs were not changed. The user approved the reporting traffic and server rebuild requirement.

The runtime sources compiled with zero errors or warnings for Editor, Windows client and headless server configurations using Unity's bundled Roslyn compiler and cached response-file references. Outputs are isolated under `Builds/Validation/LatencyCounter`; built executables and Unity assembly caches were not overwritten. No tests were written or run, and Unity CLI was not used.

Unity Editor was not running and no connected Unity MCP tools were available, so visual layout, real network loss, report expiry and reconnect behavior were reviewed in source but were not exercised live. Rebuild both client and server in Unity to use the full counter in standalone play; older servers leave OUT unavailable.

## Selection, HUD, healing and Showdown spawns (2026-10-11)

The runtime uGUI selection panel now has colored portrait lighting, rotating background rays, staggered card entrances, hover/press/selection motion and a short pose on selection. Removed slogans and idle instructional text. The full-canvas HUD uses circular blue/red touch controls, a radial yellow Super button beside the aiming stick, segmented ammunition, health nameplates and compact match/network information. Healing feedback reuses a green ground pulse and four rising crosses per view, driven by existing replicated health changes. Showdown has ten symmetric spawn positions checked with extra wall clearance and a bounded nearby fallback. Existing scenes, network prefabs, protocol and regeneration rules are reused.

Unity Editor compilation completed with no errors or warnings. Visual inspection used runtime screenshots, including a 1920x1080 landscape view and a portrait view. Temporary pointer events in paused Play Mode confirmed that both origins relocate, movement reaches `(1,0)` and clears on release, a deliberate aiming drag sets aim and queues a released shot without quick-fire, a short tap sets quick-fire, and a charged Super button signals the existing input path. These are editor interaction checks, not physical-device touch validation.

An existing Showdown Practice session spawned ten brawlers; observed positions were outside the immutable obstacles. Inspecting each of the ten configured spawn positions through the shared movement code gave a full 0.6-unit step east, west, north and south. A health increase created the 0.9-second healing effect; visual inspection showed its green pulse, and expiry disabled the ring and all crosses. Temporary runtime state was used to isolate presentation and was discarded on leaving Play Mode. No test source files or test suites were written. Unity CLI was not used. Standalone client/server packages were not rebuilt; rebuild them in Unity to include these changes.
