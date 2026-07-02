using System;
using System.Collections;
using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Presentation.Input.VCS
{
    /// <summary>
    /// Bridges VCS cursor to the RTTAppManager lifecycle so the cursor auto-snaps to
    /// the newly-opened app's surface (and back to Main Menu when the app closes).
    ///
    /// Without this component, after clicking a menu button the cursor remains bound
    /// to the Main Menu surface — clicks on the newly-opened app's content would route
    /// to the empty Main Menu hit area and fail.
    ///
    /// Singleton. Auto-bootstraps.
    /// </summary>
    [DefaultExecutionOrder(-3500)]
    public sealed class CursorAppFollower : MonoBehaviour
    {
        public static CursorAppFollower Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[CursorAppFollower]");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CursorAppFollower>();
        }

        private bool _appMgrSubscribed;
        private Coroutine _pendingSnap;

        private void OnEnable()
        {
            _appMgrSubscribed = false;
            TrySubscribeAppManager();
        }

        private void OnDisable()
        {
            if (_appMgrSubscribed)
            {
                var mgr = FindAnyObjectByType<RTTAppManager>();
                if (mgr != null)
                {
                    mgr.OnAppOpened         -= OnAppOpened;
                    mgr.OnAppClosed         -= OnAppClosed;
                    mgr.OnVisibleAppChanged -= OnVisibleAppChanged;
                }
                _appMgrSubscribed = false;
            }
            if (_pendingSnap != null) StopCoroutine(_pendingSnap);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            TrySubscribeAppManager();
        }

        private void TrySubscribeAppManager()
        {
            if (_appMgrSubscribed) return;
            var mgr = FindAnyObjectByType<RTTAppManager>();
            if (mgr == null) return;

            Debug.Log("[CursorAppFollower] subscribed to RTTAppManager events");
            mgr.OnAppOpened         += OnAppOpened;
            mgr.OnAppClosed         += OnAppClosed;
            mgr.OnVisibleAppChanged += OnVisibleAppChanged;
            _appMgrSubscribed = true;
        }

        private void OnAppOpened(string appId, RTTAppInstance instance)
        {
            Debug.Log($"[CursorAppFollower] OnAppOpened appId={appId} instance={instance} frame={instance?.Frame}");
            // Defer the snap one frame so the new RTTCanvasBase has finished Initialize()
            // and the auto-registrar has had a chance to add it to VCS.
            if (_pendingSnap != null) StopCoroutine(_pendingSnap);
            _pendingSnap = StartCoroutine(SnapToInstanceWhenReady(instance));
        }

        private void OnAppClosed(string appId)
        {
            // Snap back to the main menu (or highest-priority visible surface).
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null) return;
            var mainMenuFrame = RTTMenuFrame.PrimaryInstance;
            if (mainMenuFrame != null
                && RTTCanvasAutoRegistrar.Instance != null
                && RTTCanvasAutoRegistrar.Instance.TryGetSurfaceId(mainMenuFrame).HasValue)
            {
                var mainId = RTTCanvasAutoRegistrar.Instance.TryGetSurfaceId(mainMenuFrame).Value;
                vcs.SnapCursorTo(mainId, preserveWorldPosition: true);
            }
            else
            {
                vcs.SnapCursorToCenter();
            }
        }

        private void OnVisibleAppChanged(string appId) { /* no-op: opened/closed handle cursor */ }

        private IEnumerator SnapToInstanceWhenReady(RTTAppInstance instance)
        {
            if (instance == null || instance.Frame == null) yield break;

            // Wait up to 1 second for the new frame's RTTCanvasBase to register with VCS.
            float deadline = Time.unscaledTime + 1f;
            Guid? surfaceId = null;
            while (Time.unscaledTime < deadline && instance != null && instance.Frame != null)
            {
                if (RTTCanvasAutoRegistrar.Instance != null)
                {
                    surfaceId = RTTCanvasAutoRegistrar.Instance.TryGetSurfaceId(instance.Frame);
                    if (surfaceId.HasValue) break;
                }
                yield return null;
            }

            if (surfaceId.HasValue)
            {
                Debug.Log($"[CursorAppFollower] snap cursor to surfaceId={surfaceId.Value} (frame='{instance?.Frame?.name}')");
                VirtualCursorSpace.Instance?.SnapCursorTo(surfaceId.Value, preserveWorldPosition: true);
            }
            else
            {
                Debug.Log($"[CursorAppFollower] NO surfaceId found for frame='{instance?.Frame?.name}' after deadline");
            }
            _pendingSnap = null;
        }
    }
}