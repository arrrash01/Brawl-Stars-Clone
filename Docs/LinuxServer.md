# Run the Rift Rumble Linux server

This package is a Linux x86_64 Mono **Dedicated Server** build made with Unity 6000.3.2f1 (`StandaloneBuildSubtarget.Server`, `UNITY_SERVER`, and `ENABLE_UCS_SERVER`). `StartServer.sh` supplies `-batchmode -nographics -arenaServer`; no desktop session, GPU, Unity Editor, separate Mono installation, or cloud credentials are needed for direct connections. Keep the executable, `UnityPlayer.so`, the `libdecor` libraries, and the entire `RiftRumbleServer_Data` folder together. The Mono runtime is included inside that data folder.

Use an **x86_64/AMD64** Linux server, preferably Ubuntu Server 22.04 or 24.04. This binary does not run on ARM64 servers. Ubuntu 22.04 and 24.04 are listed in [Unity's Linux system requirements](https://docs.unity3d.com/6000.3/Documentation/Manual/system-requirements.html#server). Other distributions need compatible native libraries.

The bundled runtime requires glibc **2.35 or newer**, including `GLIBC_2.35` symbols from `libm.so.6`; Ubuntu 22.04 supplies this version. The original desktop-player package could crash inside Unity's native `InputProcess()` when no X11/Wayland backend was available, even with headless flags. This package uses the Dedicated Server runtime to avoid that desktop input path. Replace the whole old build folder when upgrading.

## Copy and start

Upload `RiftRumble-LinuxServer.tar.gz` to your Linux server with SCP/SFTP, then run:

```sh
tar -xzf RiftRumble-LinuxServer.tar.gz
cd LinuxServer
chmod +x StartServer.sh RiftRumbleServer.x86_64
./StartServer.sh -port 7777
```

Alternatively, copy the entire `Builds/LinuxServer` folder and run the last three commands inside it. Run as a regular user. The launcher works from any current directory and forwards all additional arguments to the executable.

The server listens on **UDP 7777 on all IPv4 interfaces** by default and serves both Gem Rush and Solo Showdown, with multiple independent rooms on one process/port. Look for:

```text
[Rift Rumble] READY MULTI all UDP 7777
```

`READY MULTI` and the selected UDP port confirm that startup reached the listening state. Logs go to the terminal. Stop a foreground server with `Ctrl+C`.

To restrict the allowed mode or choose another port:

```sh
./StartServer.sh -mode gems -port 7777
./StartServer.sh -mode showdown -port 7778
```

Run only one of those commands for a single instance. `-mode all` (or omitting `-mode`) accepts both modes. Optional `-ping <milliseconds>` and `-jitter <milliseconds>` enable the existing network simulation; omit them for normal hosting. The headless process retains the project's 30 FPS cap and 30 Hz network tick. Incomplete rooms fill with bots 15 seconds after their first human player joins.

## Allow clients to reach the server

Allow **inbound UDP 7777** in the hosting provider's firewall/security group and the server firewall. If using an already enabled UFW firewall:

```sh
sudo ufw allow 7777/udp
```

Use your selected port if different. For a machine behind a router, forward that same UDP port to the server. TCP 7777 is not used by the game.

In a matching client build, enter a username, the server's reachable IPv4 address, and port `7777`, select a game mode, and choose **Join Server**. Use `127.0.0.1` only when the client runs on the server machine. This build uses the existing protocol 3 and network prefabs. **Find Match** still requires separately configured Unity cloud matchmaking and server allocations; direct **Join Server** does not.

## Keep it running after SSH logout

For a simple background launch from the package folder:

```sh
nohup ./StartServer.sh -port 7777 > server.log 2>&1 &
echo $! > server.pid
tail -f server.log
```

`Ctrl+C` stops `tail`, while the server continues running. Stop that background process with `kill "$(cat server.pid)"`. `nohup` does not restart the server after a crash or reboot. Use the service below for that.

## Optional systemd service

The included `rift-rumble.service` runs the launcher as user `rift-rumble`, restarts it on failure, and sends logs to the system journal. Installing and enabling it creates a system user, copies the build to `/opt/rift-rumble`, and starts it automatically at boot. Run these commands from the extracted `LinuxServer` folder on a systemd host:

```sh
sudo useradd --system --user-group --home-dir /opt/rift-rumble --no-create-home --shell /usr/sbin/nologin rift-rumble
sudo install -d -o rift-rumble -g rift-rumble /opt/rift-rumble
sudo cp -a . /opt/rift-rumble/
sudo chown -R rift-rumble:rift-rumble /opt/rift-rumble
sudo chmod +x /opt/rift-rumble/StartServer.sh /opt/rift-rumble/RiftRumbleServer.x86_64
sudo install -m 644 rift-rumble.service /etc/systemd/system/rift-rumble.service
sudo systemctl daemon-reload
sudo systemctl enable --now rift-rumble
sudo systemctl status rift-rumble
```

Skip `useradd` if that account already exists. Edit the service's `ExecStart` to change the mode or port before installing it. Stop any foreground/background instance using that port first.

```sh
# Follow logs
journalctl -u rift-rumble -f
# Stop, start, or restart
sudo systemctl stop rift-rumble
sudo systemctl start rift-rumble
sudo systemctl restart rift-rumble
# Stop automatic startup and the running service
sudo systemctl disable --now rift-rumble
```

After replacing the build files, restart the service to load the update.

## Troubleshooting

- **Permission denied:** run `chmod +x StartServer.sh RiftRumbleServer.x86_64`. The folder must be on a filesystem that permits execution.
- **Exec format error:** check `uname -m`; this package requires `x86_64`.
- **Missing native library or GLIBC version error:** run `ldd ./UnityPlayer.so` and `ldd ./RiftRumbleServer_Data/MonoBleedingEdge/x86_64/libmonobdwgc-2.0.so`. Resolve any `not found` libraries using your distribution's package manager, or use a compatible Ubuntu version. Keep the bundled runtime files with the executable.
- **Segmentation fault after `UnloadTime` with `Selected window backend: (null)`:** confirm you replaced the original desktop-player package with the Dedicated Server package. That crash was reproduced on glibc 2.39 as well as reported on 2.35; upgrading glibc is not the fix for this startup failure.
- **Failed to bind UDP port:** another instance may already own it. Check `ss -lunp` or choose another `-port`.
- **Clients time out:** confirm the `READY MULTI` log, public/reachable IP, inbound UDP rule, and client port. A web browser or TCP port checker cannot check this UDP game listener.
- **Wrong game mode or incompatible client:** match the selected mode to the allowed server mode and use clients with the current protocol and prefabs.

## Rebuild

In Unity 6000.3.2f1 with Linux Mono and **Linux Dedicated Server Build Support** available, choose **Rift Rumble → Build Linux Headless Server**. This writes `Builds/LinuxServer`, its launcher, service example, README, and `build-result.txt`. The build includes only `Assets/Scenes/RiftRumble.unity`, uses the existing Mono backend, and selects `StandaloneBuildSubtarget.Server`. Package the whole output folder for deployment; exclude any `*_BurstDebugInformation_DoNotShip` folders from the upload.
