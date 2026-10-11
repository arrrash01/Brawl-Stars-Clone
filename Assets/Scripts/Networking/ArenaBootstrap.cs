using Unity.Netcode;
using UnityEngine;

namespace RiftRumble
{
    [DefaultExecutionOrder(-100)]
    public sealed class ArenaBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkManager manager;
        [SerializeField] private ArenaMatch match;

        public void Configure(NetworkManager networkManager, ArenaMatch arenaMatch)
        {
            manager = networkManager; match = arenaMatch;
        }

        private void Awake()
        {
            Application.runInBackground = true;
            if (Application.isMobilePlatform) Screen.orientation = ScreenOrientation.LandscapeLeft;
            Time.fixedDeltaTime = GameRules.TickDelta;
            Application.targetFrameRate = Application.isBatchMode ? GameRules.TickRate : 120;
            manager.NetworkConfig.ProtocolVersion = GameRules.ProtocolVersion;
            manager.ConnectionApprovalCallback = match.Approve;
        }

        private void OnDestroy()
        {
            if (manager != null) manager.ConnectionApprovalCallback = null;
        }
    }
}
