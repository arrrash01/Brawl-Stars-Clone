using UnityEngine;

namespace RiftRumble
{
    [RequireComponent(typeof(Camera))]
    public sealed class ArenaCamera : MonoBehaviour
    {
        public static ArenaCamera Main { get; private set; }
        public Camera Camera { get; private set; }
        Transform menuTarget;
        NetworkBrawler spectatorTarget;
        Vector3 smoothedFocus;
        bool focusInitialized, showMenu = true;

        void Awake()
        {
            Main = this;
            Camera = GetComponent<Camera>();
            Camera.orthographic = true;
            Camera.nearClipPlane = .1f;
            Camera.farClipPlane = 150;
            Camera.backgroundColor = new Color(.055f, .091f, .16f);
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.allowHDR = true;
        }

        public void ShowMenu(Transform previewTarget)
        {
            menuTarget = previewTarget;
            showMenu = true;
            spectatorTarget = null;
            focusInitialized = false;
        }

        public void ShowArena()
        {
            showMenu = false;
            spectatorTarget = null;
            focusInitialized = false;
        }

        void LateUpdate()
        {
            if (Camera == null) return;
            var local = NetworkBrawler.Local;
            if (!showMenu || local != null)
            {
                NetworkBrawler follow = local;
                var match = ArenaMatch.Instance;
                if (local != null && !local.Alive.Value && match != null && match.Mode == GameMode.SoloShowdown)
                {
                    if (spectatorTarget == null || !spectatorTarget.IsSpawned || !spectatorTarget.Alive.Value)
                    {
                        spectatorTarget = null;
                        foreach (var player in match.Players)
                        {
                            if (player == null || player == local || !player.IsSpawned || !player.Alive.Value) continue;
                            spectatorTarget = player;
                            break;
                        }
                    }
                    follow = spectatorTarget;
                }
                else spectatorTarget = null;
                Vector3 focus = follow != null ? follow.transform.position : Vector3.zero;
                focus.x = Mathf.Clamp(focus.x, -ArenaLayout.HalfWidth + 7f, ArenaLayout.HalfWidth - 7f);
                focus.z = Mathf.Clamp(focus.z, -ArenaLayout.HalfDepth + 7.5f, ArenaLayout.HalfDepth - 7.5f);
                if (!focusInitialized) { smoothedFocus = focus; focusInitialized = true; }
                smoothedFocus = Vector3.Lerp(smoothedFocus, focus, 1f - Mathf.Exp(-9f * Time.deltaTime));
                transform.rotation = Quaternion.Euler(57f, 0, 0);
                transform.position = smoothedFocus - transform.forward * 28f;
                Camera.orthographicSize = 9.6f;
            }
            else if (menuTarget != null)
            {
                Vector3 center = menuTarget.position + Vector3.up * 1.02f;
                transform.position = center + new Vector3(3.8f, 2.0f, 6.2f);
                transform.LookAt(center);
                Camera.orthographicSize = 2.45f;
            }
        }

        void OnDestroy()
        {
            if (Main == this) Main = null;
        }
    }
}
