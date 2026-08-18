using System;
using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Presentation.Input.VCS
{
    /// <summary>
    /// Reconciles VCS with the keyboard open/close lifecycle (Phase 11).
    ///
    /// When the keyboard is shown:
    ///   - Set taskbar + pagination surfaces to IsVisible=false in VCS (cursor traversal skips them).
    ///   - Re-register the keyboard surface with <see cref="EdgePolicy.Up"/> only.
    ///
    /// When hidden: reverse the visibility flags.
    ///
    /// Also tracks the keyboard's runtime world position each frame (keyboard slides
    /// via RTTMobileKeyboard.UpdatePositionRelativeToTaskbar in its own LateUpdate);
    /// we keep the registered VCS surface.center in sync so edge-traversal raycasts
    /// hit the actual location.
    /// </summary>
    [DefaultExecutionOrder(-4000)]
    public sealed class KeyboardSurfaceController : MonoBehaviour
    {
        public static KeyboardSurfaceController Instance { get; private set; }

        private RTTMobileKeyboard _keyboard;
        private RTTTaskbar _taskbar;
        private RTTFilePagination _pagination;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[KeyboardSurfaceController]");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<KeyboardSurfaceController>();
        }

        private void OnDestroy()
        {
            if (_keyboard != null)
            {
                _keyboard.OnKeyboardShown  -= OnShown;
                _keyboard.OnKeyboardHidden -= OnHidden;
            }
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            // Wait until RTTMobileKeyboard exists in the scene (typically after RTTManager.Start).
            StartCoroutine(LateBindToKeyboard());
        }

        private System.Collections.IEnumerator LateBindToKeyboard()
        {
            // Resolve up to 2 seconds; the keyboard singleton is set in RTTMobileKeyboard.Awake.
            float deadline = Time.unscaledTime + 2f;
            while (_keyboard == null && Time.unscaledTime < deadline)
            {
                _keyboard = RTTMobileKeyboard.Instance;
                if (_keyboard != null) break;
                yield return null;
            }

            if (_keyboard == null) yield break;

            _keyboard.OnKeyboardShown  += OnShown;
            _keyboard.OnKeyboardHidden += OnHidden;
        }

        private void OnShown()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null) return;

            HideTaskbarAndPaginationInVcs(vcs, hide: true);

            // Override EdgePolicy of the keyboard surface to Up-only (Decision 2).
            var kbId = FindSurfaceIdFor(_keyboard);
            if (kbId.HasValue)
            {
                var kb = vcs.Surfaces.TryGet(kbId.Value, out var s) ? s : null;
                if (kb != null)
                {
                    vcs.UnregisterSurface(kbId.Value);
                    vcs.RegisterSurface(new VirtualSurface(
                        kbId.Value,
                        kb.Center,
                        kb.Size,
                        EdgePolicy.Up,
                        SurfacePriority.Modal,
                        kb.RuntimeRef));
                }
            }
        }

        private void OnHidden()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null) return;
            HideTaskbarAndPaginationInVcs(vcs, hide: false);

            // Restore keyboard EdgePolicy to All.
            var kbId = FindSurfaceIdFor(_keyboard);
            if (kbId.HasValue)
            {
                var kb = vcs.Surfaces.TryGet(kbId.Value, out var s) ? s : null;
                if (kb != null)
                {
                    vcs.UnregisterSurface(kbId.Value);
                    vcs.RegisterSurface(new VirtualSurface(
                        kbId.Value,
                        kb.Center,
                        kb.Size,
                        EdgePolicy.All,
                        SurfacePriority.Modal,
                        kb.RuntimeRef));
                    vcs.NotifySurfaceVisibilityChanged(kbId.Value, false); // Restored keyboard is physically hidden
                }
            }
        }

        private void HideTaskbarAndPaginationInVcs(VirtualCursorSpace vcs, bool hide)
        {
            if (_taskbar != null)
            {
                var miniframe = _taskbar.GetComponent<RTTMiniFrame>();
                if (miniframe != null)
                {
                    var id = FindSurfaceIdFor(miniframe);
                    if (id.HasValue) vcs.NotifySurfaceVisibilityChanged(id.Value, !hide);
                }
            }

            if (_pagination == null) _pagination = FindAnyObjectByType<RTTFilePagination>(FindObjectsInactive.Include);
            if (_pagination != null)
            {
                var id = FindSurfaceIdFor(_pagination);
                if (id.HasValue) vcs.NotifySurfaceVisibilityChanged(id.Value, !hide);
            }
        }

        private void LateUpdate()
        {
            // Late-bind taskbar/pagination references on first use (they may not exist at OnEnable).
            if (_taskbar == null) _taskbar = RTTTaskbar.Instance;
            if (_pagination == null) _pagination = FindAnyObjectByType<RTTFilePagination>(FindObjectsInactive.Include);

            // NOTE: Keyboard center sync is handled by RTTCanvasAutoRegistrar.LateUpdate (Order -8000)
            // which automatically syncs ALL RTTCanvasBase positions every frame.  No duplication needed.
        }

        private static Guid? FindSurfaceIdFor(RTTCanvasBase canvas)
        {
            if (RTTCanvasAutoRegistrar.Instance == null) return null;
            return RTTCanvasAutoRegistrar.Instance.TryGetSurfaceId(canvas);
        }
    }
}
