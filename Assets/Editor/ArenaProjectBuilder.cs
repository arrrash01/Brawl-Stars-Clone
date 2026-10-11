using System.IO;
using System.Linq;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RiftRumble.Editor
{
    public static class ArenaProjectBuilder
    {
        public const string ScenePath = "Assets/Scenes/RiftRumble.unity";

        [MenuItem("Rift Rumble/Create Playable Arena")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Leave Play Mode before authoring the arena.");
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                ConfigureMultiMatchServer();
                Debug.Log("Rift Rumble scene already exists. Existing scene wiring has been preserved.");
                return;
            }
            ArenaArtBuilder.BuildAll();
            Directory.CreateDirectory("Assets/Prefabs/Networking");
            Directory.CreateDirectory("Assets/Resources/RiftRumble/UI");
            AssetDatabase.Refresh();
            if (TMP_Settings.instance == null) TMP_PackageResourceImporter.ImportResources(true, false, false);
            CreateFont();

            var brawlerRoot = new GameObject("Brawler");
            brawlerRoot.AddComponent<NetworkObject>();
            var player = brawlerRoot.AddComponent<NetworkBrawler>();
            var visual = new GameObject("Visuals"); visual.transform.SetParent(brawlerRoot.transform, false); visual.AddComponent<BrawlerView>();
            var brawlerAsset = PrefabUtility.SaveAsPrefabAsset(brawlerRoot, "Assets/Prefabs/Networking/Brawler.prefab");
            Object.DestroyImmediate(brawlerRoot);

            var projectileRoot = new GameObject("Projectile"); projectileRoot.AddComponent<NetworkObject>(); projectileRoot.AddComponent<NetworkProjectile>();
            var projectileAsset = PrefabUtility.SaveAsPrefabAsset(projectileRoot, "Assets/Prefabs/Networking/Projectile.prefab");
            Object.DestroyImmediate(projectileRoot);

            var prefabs = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            prefabs.Add(new NetworkPrefab { Prefab = brawlerAsset }); prefabs.Add(new NetworkPrefab { Prefab = projectileAsset });
            AssetDatabase.CreateAsset(prefabs, "Assets/Prefabs/Networking/ArenaNetworkPrefabs.asset");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera"); cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>(); cameraObject.AddComponent<AudioListener>(); cameraObject.AddComponent<ArenaCamera>();
            camera.orthographic = true; camera.orthographicSize = 9.6f;
            camera.transform.SetPositionAndRotation(new Vector3(0, 26, -16), Quaternion.Euler(57, 0, 0));
            camera.cullingMask = ~(1 << 2);
            var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>(); cameraData.renderPostProcessing = true;
            var light = new GameObject("Arena Sun").AddComponent<Light>(); light.type = LightType.Directional;
            light.color = new Color(1, .9f, .8f); light.intensity = 1.5f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -32, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.65f, .72f, .85f);
            RenderSettings.reflectionIntensity = .6f;
            var arena = new GameObject("Arena Environment").AddComponent<ArenaView>();
            var matchObject = new GameObject("Arena Match"); matchObject.AddComponent<NetworkObject>();
            var match = matchObject.AddComponent<ArenaMatch>();
            match.ConfigureAssets(brawlerAsset.GetComponent<NetworkBrawler>(), projectileAsset.GetComponent<NetworkProjectile>(), arena);

            var networkRoot = new GameObject("Network Manager");
            var transport = networkRoot.AddComponent<UnityTransport>();
            var manager = networkRoot.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport, TickRate = GameRules.TickRate, ConnectionApproval = true,
                EnableSceneManagement = true, ForceSamePrefabs = true, ClientConnectionBufferTimeout = 15,
                PlayerPrefab = null, ProtocolVersion = GameRules.ProtocolVersion
            };
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabs);
            var app = new GameObject("Rift Rumble");
            app.AddComponent<ArenaBootstrap>().Configure(manager, match);
            app.AddComponent<ArenaConnection>(); app.AddComponent<ArenaHud>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            ConfigureMultiMatchServer();
            var existing = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            existing.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = existing.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("Rift Rumble ready. Press Play to select a brawler; Practice starts an NGO match with bots.");
        }

        [MenuItem("Rift Rumble/Configure Multi-Match Server")]
        public static void ConfigureMultiMatchServer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Leave Play Mode before configuring server assets.");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                throw new System.InvalidOperationException("Open " + ScenePath + " before configuring server assets.");
            var connection = Object.FindFirstObjectByType<ArenaConnection>();
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/Prefabs/Networking/ArenaNetworkPrefabs.asset");
            if (connection == null || list == null)
                throw new System.InvalidOperationException("The arena connection or network prefab list is missing.");
            const string path = "Assets/Prefabs/Networking/ArenaMatch.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var root = new GameObject("Arena Match");
                try
                {
                    root.AddComponent<NetworkObject>();
                    root.AddComponent<ArenaMatch>().ConfigureAssets(
                        AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Networking/Brawler.prefab").GetComponent<NetworkBrawler>(),
                        AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Networking/Projectile.prefab").GetComponent<NetworkProjectile>(), null);
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { Object.DestroyImmediate(root); }
            }
            if (!list.Contains(prefab))
            {
                list.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(list);
            }
            var serialized = new SerializedObject(connection);
            var matchProperty = serialized.FindProperty("matchPrefab");
            if (matchProperty.objectReferenceValue != prefab.GetComponent<ArenaMatch>())
            {
                Undo.RecordObject(connection, "Configure multi-match server");
                connection.ConfigureMatchPrefab(prefab.GetComponent<ArenaMatch>());
                EditorUtility.SetDirty(connection);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Rift Rumble multi-match prefab registered and connected.");
        }

        public static void CreateFont()
        {
            const string path = "Assets/Resources/RiftRumble/UI/ArenaFont.asset";
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) != null) return;
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/Fonts/Rajdhani-Bold.ttf");
            if (source == null) return;
            var font = TMP_FontAsset.CreateFontAsset(source, 64, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            font.name = "ArenaFont";
            font.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?+-/<>%()[]=•");
            AssetDatabase.CreateAsset(font, path);
            foreach (var texture in font.atlasTextures) { texture.name = "ArenaFont Atlas"; AssetDatabase.AddObjectToAsset(texture, font); }
            font.material.name = "ArenaFont Material"; AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
        }

        [MenuItem("Rift Rumble/Build Windows Client")]
        public static void BuildClient() => BuildWindows(false);

        [MenuItem("Rift Rumble/Build Windows Headless Server")]
        public static void BuildServer() => BuildWindows(true);

        [MenuItem("Rift Rumble/Build Linux Headless Server")]
        public static void BuildLinuxServer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Leave Play Mode before building.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
                throw new System.InvalidOperationException("Install Linux Build Support (Mono) for this Unity Editor, then restart the Editor.");

            const string folder = "Builds/LinuxServer";
            Directory.CreateDirectory(folder);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = folder + "/RiftRumbleServer.x86_64",
                target = BuildTarget.StandaloneLinux64,
                // The desktop Linux player crashes in native InputProcess without a window backend.
                subtarget = (int)StandaloneBuildSubtarget.Server,
                extraScriptingDefines = new[] { "ENABLE_UCS_SERVER" },
                options = BuildOptions.CompressWithLz4
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            File.WriteAllText(folder + "/build-result.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings + "\nBytes: " + report.summary.totalSize);
            Debug.Log("Rift Rumble Linux server build: " + report.summary.result);
            if (report.summary.result != BuildResult.Succeeded) throw new System.InvalidOperationException("Build failed. Inspect the Unity Console and " + folder + "/build-result.txt.");
            if (!File.Exists(options.locationPathName) || !File.Exists(folder + "/UnityPlayer.so"))
                throw new System.InvalidOperationException("Linux build runtime files are missing. Restart Unity after installing Linux build support, then rebuild.");

            var utf8 = new System.Text.UTF8Encoding(false);
            File.WriteAllText(folder + "/StartServer.sh",
                "#!/bin/sh\nset -eu\n" +
                "server_dir=$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd)\n" +
                "cd \"$server_dir\"\n" +
                "exec \"$server_dir/RiftRumbleServer.x86_64\" -batchmode -nographics -arenaServer -logFile - \"$@\"\n", utf8);
            File.WriteAllText(folder + "/rift-rumble.service",
                "[Unit]\nDescription=Rift Rumble game server\nWants=network-online.target\nAfter=network-online.target\n\n" +
                "[Service]\nType=simple\nUser=rift-rumble\nGroup=rift-rumble\nWorkingDirectory=/opt/rift-rumble\n" +
                "ExecStart=/opt/rift-rumble/StartServer.sh -port 7777\nRestart=on-failure\nRestartSec=5\nTimeoutStopSec=30\n\n" +
                "[Install]\nWantedBy=multi-user.target\n", utf8);
            File.Copy("Docs/LinuxServer.md", folder + "/README.md", true);
        }

        private static void BuildWindows(bool server)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Leave Play Mode before building.");
            string folder = server ? "Builds/Server" : "Builds/Client";
            Directory.CreateDirectory(folder);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = folder + (server ? "/RiftRumbleServer.exe" : "/RiftRumble.exe"),
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                // Official Services SDK define exposes its server assemblies without requiring
                // the optional Dedicated Server platform module or changing PlayerSettings.
                extraScriptingDefines = server ? new[] { "ENABLE_UCS_SERVER" } : System.Array.Empty<string>(),
                options = BuildOptions.CompressWithLz4
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            File.WriteAllText(folder + "/build-result.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings + "\nBytes: " + report.summary.totalSize);
            Debug.Log("Rift Rumble " + (server ? "server" : "client") + " build: " + report.summary.result);
            if (report.summary.result != BuildResult.Succeeded) throw new System.InvalidOperationException("Build failed. Inspect the Unity Console and " + folder + "/build-result.txt.");
            if (server)
            {
                // Keep the headless launch flags with the build when it is copied to a server.
                File.WriteAllText(folder + "/StartServer.cmd",
                    "@echo off\r\n" +
                    "\"%~dp0RiftRumbleServer.exe\" -batchmode -nographics -arenaServer -logFile \"%~dp0server.log\" %*\r\n");
            }
        }
    }
}
