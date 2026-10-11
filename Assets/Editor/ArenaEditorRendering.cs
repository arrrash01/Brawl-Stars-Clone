using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RiftRumble.Editor
{
    [InitializeOnLoad]
    public static class ArenaEditorRendering
    {
        struct CameraState
        {
            public bool Hdr;
            public RenderTexture Target;
            public RenderTexture LdrTarget;
        }

        static readonly Dictionary<Camera, CameraState> Cameras = new Dictionary<Camera, CameraState>();

        static ArenaEditorRendering()
        {
            RenderPipelineManager.beginCameraRendering += BeforeCameraRendering;
            RenderPipelineManager.endCameraRendering += AfterCameraRendering;
        }

        static void BeforeCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().path != ArenaProjectBuilder.ScenePath || Cameras.ContainsKey(camera))
                return;

            // LDR avoids the Editor HDR artifacts in both Game and Scene views.
            var state = new CameraState { Hdr = camera.allowHDR, Target = camera.targetTexture };
            if (camera.cameraType == CameraType.SceneView && state.Target != null)
            {
                var descriptor = state.Target.descriptor;
                descriptor.graphicsFormat = SystemInfo.GetGraphicsFormat(UnityEngine.Experimental.Rendering.DefaultFormat.LDR);
                state.LdrTarget = RenderTexture.GetTemporary(descriptor);
                camera.targetTexture = state.LdrTarget;
            }
            Cameras.Add(camera, state);
            camera.allowHDR = false;
        }

        static void AfterCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!Cameras.Remove(camera, out var state)) return;
            camera.allowHDR = state.Hdr;
            if (state.LdrTarget != null)
            {
                camera.targetTexture = state.Target;
                Graphics.Blit(state.LdrTarget, state.Target);
                RenderTexture.ReleaseTemporary(state.LdrTarget);
            }
        }
    }
}
