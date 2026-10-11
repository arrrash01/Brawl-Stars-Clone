# Rift Rumble

Open `Assets/Scenes/RiftRumble.unity` and press Play. The startup login asks for a username (1–20 characters), plus an optional server IP and port. The name is shown above your brawler to other players and in your HUD. Login details remain in memory for this run; no password or account registration is needed.

Leave the server fields blank for matchmaking or practice. If you enter a numeric server IP, the port defaults to 7777 when blank; entering only a port is invalid. Continue to character selection, select any unlocked brawler and a mode, then choose **Join Server** to use that endpoint. With no endpoint, the main button remains **Find Match**. **Practice / Bots** and **Direct Connect** remain available. Practice fills the selected mode to its normal participant count using local bots in an NGO host. Online and direct matches wait for real players.

The Windows executable is `Builds/Client/RiftRumble.exe`. Keep its accompanying data and runtime folders together. Start the headless server using `Builds/Server/StartServer.cmd`; it serves multiple simultaneous matches in both modes on UDP 7777 and writes `Builds/Server/server.log`. To allow only Solo Showdown rooms, run `StartServer.cmd -mode showdown -port 7777`. Copy the whole `Builds/Server` folder when hosting on another computer. Clients enter the same server IP and port in the login panel, select their desired mode, and choose **Join Server**. The server groups them into a waiting room of that mode and creates more rooms as needed. Gem Rush needs six players per room; Showdown needs ten. Rebuild and replace both client and server binaries for protocol 3. See [Validation.md](Validation.md) for the observed build and play checks.

## Brawlers

| Character | Basic attack | Super |
| --- | --- | --- |
| BRONT | Close cone punch, blocked by cover | Radial ground slam |
| VEX | Fast straight pulse projectile, blocked by cover | Three-shot spread |
| PIP | Gravity-driven bomb arc over cover, splash on landing | Larger explosive blast |

Each brawler has three ammo slots, automatic reload and healing after three seconds without attacking or taking damage. Damage dealt charges the super. Gems drop on knockout. Respawning restores health and grants a short visible shield, cancelled by attacking.

## Modes

- **Gem Rush:** six players split into two teams of three. Collect ten gems and keep a strictly higher team total for fifteen seconds to win. Losing the lead resets the hold countdown. Knocked-out players return after four seconds. The higher gem total wins at the three-minute limit; equal totals draw.
- **Solo Showdown:** ten individual players, one life each. Collect power cells to increase maximum health and damage. A circular storm starts closing after fifteen seconds and deals escalating damage outside its boundary. Last survivor wins. Eliminated players spectate a survivor.

## Controls

| Input | Move | Aim / basic attack | Super |
| --- | --- | --- | --- |
| Keyboard + mouse | WASD / arrows, or drag the movement control | Hold left mouse to aim; release to fire once | Space or right button |
| Gamepad | Left stick | Aim with right stick; release right trigger to fire once | Left trigger |
| Touch / on-screen controls | Left stick | Drag right stick to aim; release to fire once | Super button |

Pip's mouse aim and touch-stick distance choose the bomb landing point, clamped to attack range. The arc preview uses the same trajectory as the server. Releasing aim retains its direction and distance relative to the player, so the guide and landing marker move with you. Idle mouse movement does not reset or redirect them. A released shot is queued until the next network tick with its captured direction and distance. Both on-screen sticks also accept mouse dragging on PC. Character selection and primary menu buttons support mouse/touch; gameplay supports the gamepad controls above.

A quick tap auto-aims at the nearest living enemy within the brawler's attack range. The server selects the target and confirms the shot's aim to the owner, updating the guide; cover still blocks attacks normally. The confirmed direction and distance then move with the player. A newer manual aim takes priority over an older confirmation. If nobody is in range, the shot follows the last aimed direction. Mouse and on-screen gestures switch to deliberate aiming after dragging 12 screen pixels or holding for 0.18 seconds. On a gamepad, moving the right stick while holding the trigger selects deliberate aim. Deliberate shots use the chosen aim rather than auto-targeting.

## Technical layout

- `Assets/Scripts/Gameplay`: immutable character/mode rules, shared mathematical arena collision, authoritative match/combat/projectiles.
- `Assets/Scripts/Networking`: NGO setup, service connection lifetime, bounded input history and replay.
- `Assets/Scripts/Presentation`: original mesh rigs, procedural animation, camera, arena decoration and synthesized sound.
- `Assets/Scripts/UI`: responsive uGUI/TMP selection, HUD, nameplates, results, direct-connect dialog and touch controls.
- `Assets/Prefabs/Networking`: one shared network brawler prefab and one projectile prefab, registered in the network prefab list. Character behavior is data-driven through the shared prefab.
- `Assets/Resources/RiftRumble`: original model, environment, material and UI font assets. Authoring tools live in `Assets/Editor`.

`Rift Rumble > Create Playable Arena` authors the first scene and registered prefabs if absent; it preserves an existing scene. `Rift Rumble > Build Art Assets` rebuilds original art while retaining asset GUIDs. The original template SampleScene and render-pipeline settings are preserved.

## Prediction and authority

The owner simulates movement at 30 Hz and stores commands and predicted positions in a 128-entry ring buffer. The dedicated server validates finite input, ownership, character, sequence and respawn epoch and applies at most one movement command per tick. Fifteen snapshots per second acknowledge processed command sequences. The owner restores the server position and replays every pending command through the same `ArenaLayout.Move` function; remote players interpolate snapshots. Respawn increments an epoch, clears prediction history and rejects stale commands. There is no competing NetworkTransform on predicted brawlers.

Attacks, ammo, reloads, hits, health, gems, storm damage and match outcomes are server-owned. Lob projectiles evaluate initial velocity and gravity analytically; straight shots sweep mathematical collision bounds. These calculations work on a headless server without rendered geometry or a Rigidbody simulation.

See [Networking.md](Networking.md) for cloud prerequisites, exact queue rules and dedicated-server commands. Hosting credentials and cloud deployments are separate account resources and are not embedded in this project.

## Art attribution

All character meshes, rigs, environment props, materials and sound cues were authored for this project. The menu uses [Rajdhani Bold by Indian Type Foundry](https://github.com/google/fonts/tree/main/ofl/rajdhani), distributed under the SIL Open Font License; the license is included at `Assets/Art/Fonts/OFL.txt`. There are no imported Brawl Stars character models or proprietary game assets.
