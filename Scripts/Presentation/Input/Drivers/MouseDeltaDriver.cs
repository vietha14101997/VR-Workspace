using UnityEngine;
using UnityEngine.InputSystem;
using VRWorkspace.Domain.Input;

namespace VRWorkspace.Presentation.Input.Drivers
{
    /// <summary>
    /// Drives the VCS cursor from a connected mouse's pixel delta.
    ///
    /// Pipeline: <c>Mouse.current.delta</c> (pixels/frame) →
    ///   world-meters via <c>sensitivity</c> →
    ///   UV delta by dividing by the active surface's world size.
    /// Inverting Y is configurable; default OFF because Unity's InputSystem reports
    /// mouse delta with +Y up which already matches our virtual space (+Y up).
    ///
    /// Edge traversal is handled inside <see cref="VirtualCursorSpace.MoveCursor"/>.
    /// Click dispatch happens via <see cref="ClickDispatcher"/> on
    /// <c>Mouse.current.leftButton.wasPressedThisFrame</c>.
    /// </summary>
    [DefaultExecutionOrder(-6000)]
    public sealed class MouseDeltaDriver : MonoBehaviour
    {
        public static MouseDeltaDriver Instance { get; private set; }

        private Vector2 _lastMousePosition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[MouseDeltaDriver]");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MouseDeltaDriver>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null || vcs.Mode == InputMode.Gaze) return;

            var mouse = Mouse.current;
            if (mouse == null || !mouse.added) return;

            // 1) Click FIRST (independent of delta).
            //    Previously this came AFTER the delta-move early-return, so a click
            //    with no mouse movement was silently dropped — the bug that surfaced
            //    when the cursor appeared on screen but no button responded.
            if (mouse.leftButton != null && mouse.leftButton.wasPressedThisFrame)
            {
                vcs.RaiseClick();
            }

            // 2) Then apply the per-frame delta (cursor movement).
            Vector2 mouseDelta = mouse.delta.ReadValue();
            if (mouseDelta.sqrMagnitude < 1e-8f) return;
            ApplyCursorDelta(vcs, mouseDelta);
        }

        private static void ApplyCursorDelta(VirtualCursorSpace vcs, Vector2 mouseDelta)
        {
            if (!vcs.Cursor.SurfaceId.HasValue) return;
            if (!vcs.Surfaces.TryGet(vcs.Cursor.SurfaceId.Value, out var surface)) return;

            // pixels → meters (sensitivity tuned to feel natural; default 0.0008 m/px)
            Vector2 worldDelta = mouseDelta * vcs.Config.MouseSensitivity;
            if (vcs.Config.InvertY) worldDelta.y = -worldDelta.y;

            Vector2 uvDelta = new Vector2(
                worldDelta.x / Mathf.Max(1e-4f, surface.Size.x),
                worldDelta.y / Mathf.Max(1e-4f, surface.Size.y));

            vcs.MoveCursor(uvDelta);
        }
    }
}
