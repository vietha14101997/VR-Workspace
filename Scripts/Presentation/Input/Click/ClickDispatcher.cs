using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.Presentation.Input.Cursor;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Presentation.Input.Click
{
    /// <summary>
    /// Bridges VCS click events to actual UI dispatch.
    ///
    /// Uses the cursor's WORLD position (from WorldSpaceCursorRenderer.CursorWorldPosition)
    /// and synthesizes a Ray from Camera.main through that point. The ray then re-uses
    /// the existing gaze flow: RTTRaycastManager.Raycast → Physics.Raycast against the
    /// quad BoxCollider → CalculateUV from worldHitPoint → GraphicRaycaster.Raycast.
    ///
    /// Why world raycast instead of UV-based:
    ///   - Visual cursor is positioned via world transform of the active surface's quad.
    ///   - Quad may have rotation (RTTMenu oriented towards camera) so UV → world mapping
    ///     is not a simple identity.
    ///   - World raycast hits the same physical quad collider that the gaze reticle uses,
    ///     so visual position == click raycast hit point == UI element. No drift.
    ///
    /// Mirrors the priority cascade in VRGazeReticle.ProcessDwellClickRTT (lines 619-735):
    ///   1. Click outside keyboard area &amp; keyboard open → close keyboard (suppress click).
    ///   2. Click anywhere else → forward via world raycast.
    /// </summary>
    [DefaultExecutionOrder(-5000)]
    public sealed class ClickDispatcher : MonoBehaviour
    {
        public static ClickDispatcher Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[ClickDispatcher]");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ClickDispatcher>();
        }

        private bool _subscribed;

        private void OnEnable()
        {
            _subscribed = false;
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (_subscribed && VirtualCursorSpace.Instance != null)
            {
                VirtualCursorSpace.Instance.OnClickDispatched -= OnClickDispatched;
            }
            _subscribed = false;
        }

        private void Update()
        {
            // Lazy-subscribe: VirtualCursorSpace is created lazily when the first surface
            // registers, which may happen AFTER this MonoBehaviour's OnEnable. Without
            // this retry, click events are silently dropped on the very first session.
            TrySubscribe();
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            if (VirtualCursorSpace.Instance == null) return;
            VirtualCursorSpace.Instance.OnClickDispatched += OnClickDispatched;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnClickDispatched(CursorState cursor)
        {
            if (!cursor.SurfaceId.HasValue)
            {
                Debug.Log("[ClickDispatcher] ABORT: cursor.SurfaceId is null");
                return;
            }

            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null || !vcs.Surfaces.TryGet(cursor.SurfaceId.Value, out var surface))
            {
                Debug.Log($"[ClickDispatcher] ABORT: surface {cursor.SurfaceId} not in registry");
                return;
            }

            var canvas = surface.RuntimeRef as RTTCanvasBase;
            if (canvas == null)
            {
                Debug.Log($"[ClickDispatcher] ABORT: surface.RuntimeRef is null or not RTTCanvasBase");
                return;
            }
            Debug.Log($"[ClickDispatcher] canvas '{canvas.name}' UV=({cursor.UV.x:F3},{cursor.UV.y:F3}) -> world raycast");

            // Click-outside-keyboard suppression
            var keyboard = RTTMobileKeyboard.CurrentlyOpenKeyboard;
            if (keyboard != null
                && surface.RuntimeRef as RTTMobileKeyboard != keyboard
                && !IsInsideKeyboard(surface))
            {
                Debug.Log("[ClickDispatcher] keyboard open + cursor not on keyboard -> closing keyboard, suppressing click");
                keyboard.Hide();
                return;
            }

            // World-based raycast — same flow as gaze reticle, ensures visual == click target.
            var raycastMgr = VRWorkspace.UI.RTT.Input.RTTRaycastManager.Instance;
            var renderer = WorldSpaceCursorRenderer.Instance;
            if (raycastMgr == null || renderer == null)
            {
                Debug.Log("[ClickDispatcher] ABORT: raycastMgr or renderer missing");
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.Log("[ClickDispatcher] ABORT: Camera.main null");
                return;
            }

            Vector3 worldPos = renderer.CursorWorldPosition;
            Vector3 dir = (worldPos - cam.transform.position);
            float dist = dir.magnitude;
            if (dist < 0.01f)
            {
                Debug.Log("[ClickDispatcher] ABORT: cursor too close to camera");
                return;
            }
            dir /= dist;

            Ray ray = new Ray(cam.transform.position, dir);
            var hit = raycastMgr.Raycast(ray);
            Debug.Log($"[ClickDispatcher] world raycast origin={cam.transform.position} dir={dir} cursorWorld={worldPos} hit.isValid={hit.isValid} hitElement={hit.hitUIElement?.name ?? "null"}");

            if (!hit.isValid || hit.hitUIElement == null) return;
            // The Raycast above already populated _currentHit; SendClick dispatches using it.
            raycastMgr.SendClick();
        }

        private static bool IsInsideKeyboard(VirtualSurface surface)
        {
            var vcs = VirtualCursorSpace.Instance;
            var keyboard = RTTMobileKeyboard.CurrentlyOpenKeyboard;
            if (vcs == null || keyboard == null) return false;

            object kbObj = keyboard;
            foreach (var s in vcs.Surfaces.All)
            {
                if (object.ReferenceEquals(s.RuntimeRef, kbObj)) return true;
            }
            return false;
        }
    }
}
