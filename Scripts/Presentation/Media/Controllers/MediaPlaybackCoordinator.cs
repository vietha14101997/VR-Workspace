using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Debug = UnityEngine.Debug;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.Projections;
using VRWorkspace.Media.UI;
using VRWorkspace.Media.Core;
using VRWorkspace.Media.Utils;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Presentation.Media.Controllers
{
    /// <summary>
    /// Manages StartPlayback / StopPlayback logic and the Library &lt;-&gt; Player
    /// mode transitions (with fade animations).
    ///
    /// Plain C# class. Requires a MonoBehaviour owner to run coroutines.
    /// All positioning helpers and animation coroutines live here.
    /// </summary>
    public class MediaPlaybackCoordinator
    {
        // ------------------------------------------------------------------ //
        //  Dependencies injected at construction                               //
        // ------------------------------------------------------------------ //

        private readonly MonoBehaviour _runner;       // coroutine host
        private readonly IPlaybackCoordinatorContext _ctx;

        // ------------------------------------------------------------------ //
        //  Fade constants                                                       //
        // ------------------------------------------------------------------ //

        private const float FADE_OUT_DURATION = 0.15f;
        private const float FADE_IN_DURATION  = 0.2f;

        // ------------------------------------------------------------------ //
        //  Constructor                                                          //
        // ------------------------------------------------------------------ //

        public MediaPlaybackCoordinator(MonoBehaviour runner, IPlaybackCoordinatorContext context)
        {
            _runner = runner;
            _ctx = context;
        }

        // ------------------------------------------------------------------ //
        //  Playback                                                             //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Start video playback using the player controller.
        /// Positions all UI relative to the saved menu-frame transform.
        /// </summary>
        public void StartPlayback(VRWorkspace.Media.Core.VRVideoPlayerController playerController,
            VRWorkspace.Media.Core.VRVideoProjectionSystem projectionSystem,
            MediaVideoInfo video)
        {
            // Position projection
            if (projectionSystem != null && _ctx.MenuFramePosition != Vector3.zero)
                projectionSystem.SetTargetPosition(_ctx.MenuFramePosition, _ctx.MenuFrameRotation);

            // Position controls below video screen
            if (_ctx.ControlsContainer != null && _ctx.MenuFramePosition != Vector3.zero)
            {
                Vector3 controlsPos = _ctx.MenuFramePosition + _ctx.MenuFrameRotation * new Vector3(0, -0.625f, 0);
                _ctx.ControlsContainer.transform.position = controlsPos;
                _ctx.ControlsContainer.transform.rotation = Quaternion.identity;

                if (_ctx.PlayerControlsGroup != null)
                {
                    _ctx.PlayerControlsBaseLocalPos = Vector3.zero;
                    _ctx.ApplyUISettingsToControlsGroup();
                }

                if (_ctx.ControlsFrameObject != null)
                    _ctx.ControlsFrameObject.transform.rotation = _ctx.MenuFrameRotation;

                _ctx.UpdateSideControlsFacing();
                _ctx.ControlsContainer.SetActive(true);
            }

            // Detect projection type for menu-button positioning
            ProjectionDetector.DetectProjectionAndStereo(
                video.Path, video.Width, video.Height,
                out var projType, out _);
            bool willBeImmersive = !ProjectionDetector.SupportsScreenSettings(projType);

            if (_ctx.MenuButtonFrameObject != null && _ctx.MenuFramePosition != Vector3.zero)
            {
                if (willBeImmersive && Camera.main != null)
                    _ctx.PositionMenuButtonImmersive(Camera.main);
                else
                    _ctx.PositionMenuButtonFlat();
            }

            if (willBeImmersive && Camera.main != null && _ctx.ControlsContainer != null)
                _ctx.SetupControlsFollowCamera(Camera.main);
            else
                _ctx.ControlsFollowCamera = false;

            _ctx.PositionSideControlsForProjection(willBeImmersive);

            playerController?.PlayVideo(video);
        }

        /// <summary>Stop current playback.</summary>
        public void StopPlayback(VRWorkspace.Media.Core.VRVideoPlayerController playerController)
        {
            playerController?.Stop();
        }

        // ------------------------------------------------------------------ //
        //  Mode transitions                                                     //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Library → Player with cross-fade.
        /// Returns the started Coroutine so the caller can track / stop it.
        /// </summary>
        public Coroutine SwitchToPlayerWithFade(MediaVideoInfo video, Action onComplete = null)
        {
            return _runner.StartCoroutine(SwitchToPlayerCoroutine(video, onComplete));
        }

        /// <summary>
        /// Player → Library with cross-fade.
        /// Returns the started Coroutine so the caller can track / stop it.
        /// </summary>
        public Coroutine SwitchToLibraryWithFade(Action onComplete = null)
        {
            return _runner.StartCoroutine(SwitchToLibraryCoroutine(onComplete));
        }

        /// <summary>Direct-play: fade in then notify completion.</summary>
        public Coroutine FadeInAfterDirectPlay(MediaVideoInfo videoInfo, Action onComplete = null)
        {
            return _runner.StartCoroutine(FadeInDirectPlayCoroutine(videoInfo, onComplete));
        }

        /// <summary>Direct-play: fade out then run cleanup callback.</summary>
        public Coroutine FadeOutAndCleanupDirectPlay(Action onFadeDone)
        {
            return _runner.StartCoroutine(FadeOutDirectPlayCoroutine(onFadeDone));
        }

        // ------------------------------------------------------------------ //
        //  Coroutines                                                           //
        // ------------------------------------------------------------------ //

        private IEnumerator SwitchToPlayerCoroutine(MediaVideoInfo video, Action onComplete)
        {
            yield return _runner.StartCoroutine(AnimateLibraryFade(1f, 0f, FADE_OUT_DURATION));

            _ctx.HideLibraryUI();
            RTTManager.Instance?.EnterImmersiveMode();
            _ctx.ShowPlayerUI();

            if (_ctx.QueuePanel != null)
            {
                var queue = MediaPlaylistService.Instance.GetPlaybackQueue();
                int currentIdx = MediaPlaylistService.Instance.CurrentQueueIndex;
                _ctx.QueuePanel.SetQueue(queue, currentIdx);
            }

            _ctx.SetAllPlayerFramesAlpha(0f);
            _ctx.StartPlaybackInternal(video);

            yield return _runner.StartCoroutine(AnimatePlayerFade(0f, 1f, FADE_IN_DURATION));

            onComplete?.Invoke();
            Debug.Log($"[MediaPlaybackCoordinator] Switched to Player: {video.Title}");
        }

        private IEnumerator SwitchToLibraryCoroutine(Action onComplete)
        {
            yield return _runner.StartCoroutine(AnimatePlayerFade(1f, 0f, FADE_OUT_DURATION));

            RTTManager.Instance?.ExitImmersiveMode();
            _ctx.ShowLibraryUI();
            _ctx.HidePlayerUI();
            _ctx.SetAllLibraryFramesAlpha(0f);

            yield return _runner.StartCoroutine(AnimateLibraryFade(0f, 1f, FADE_IN_DURATION));

            onComplete?.Invoke();
            Debug.Log("[MediaPlaybackCoordinator] Switched to Library");
        }

        private IEnumerator FadeInDirectPlayCoroutine(MediaVideoInfo videoInfo, Action onComplete)
        {
            yield return _runner.StartCoroutine(AnimatePlayerFade(0f, 1f, FADE_IN_DURATION));
            onComplete?.Invoke();
            Debug.Log($"[MediaPlaybackCoordinator] Direct play started: {videoInfo.Title}");
        }

        private IEnumerator FadeOutDirectPlayCoroutine(Action onFadeDone)
        {
            yield return _runner.StartCoroutine(AnimatePlayerFade(1f, 0f, FADE_OUT_DURATION));
            onFadeDone?.Invoke();
        }

        // ------------------------------------------------------------------ //
        //  Fade animation helpers                                               //
        // ------------------------------------------------------------------ //

        private IEnumerator AnimatePlayerFade(float from, float to, float duration)
        {
            var materials = new List<Material>();
            CollectFrameMaterial(_ctx.ControlsFrameObject, materials);
            CollectFrameMaterial(_ctx.OverlayFrameObject, materials);
            CollectFrameMaterial(_ctx.SideControlsFrameObject, materials);
            CollectFrameMaterial(_ctx.MenuButtonFrameObject, materials);

            if (_ctx.QueuePagination != null)
            {
                var quad = _ctx.QueuePagination.GetDisplayQuad();
                if (quad?.material != null)
                    materials.Add(quad.material);
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float alpha = Mathf.Lerp(from, to, t);
                foreach (var mat in materials)
                    mat.color = new Color(1f, 1f, 1f, alpha);
                SetProjectionAlpha(alpha);
                yield return null;
            }

            foreach (var mat in materials)
                mat.color = new Color(1f, 1f, 1f, to);
            SetProjectionAlpha(to);
        }

        private IEnumerator AnimateLibraryFade(float from, float to, float duration)
        {
            var materials = new List<Material>();

            if (_ctx.ParentMenuFrame != null)
            {
                var quad = _ctx.ParentMenuFrame.GetDisplayQuad();
                if (quad?.material != null)
                    materials.Add(quad.material);
            }

            foreach (var frame in _ctx.GetAllLibraryFrames())
            {
                if (frame == null) continue;
                var quad = frame.GetDisplayQuad();
                if (quad?.material != null)
                    materials.Add(quad.material);
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float alpha = Mathf.Lerp(from, to, t);
                foreach (var mat in materials)
                    mat.color = new Color(1f, 1f, 1f, alpha);
                yield return null;
            }

            foreach (var mat in materials)
                mat.color = new Color(1f, 1f, 1f, to);
        }

        // ------------------------------------------------------------------ //
        //  Static helpers                                                       //
        // ------------------------------------------------------------------ //

        private void SetProjectionAlpha(float alpha)
        {
            var projectionSystem = _ctx.ProjectionSystem;
            if (projectionSystem == null || projectionSystem.ActiveRenderer == null) return;

            if (projectionSystem.ActiveRenderer is VRWorkspace.Media.Projections.FlatProjectionRenderer flatRenderer)
                flatRenderer.SetBoardAlpha(alpha);
            else if (projectionSystem.ActiveRenderer is VRWorkspace.Media.Projections.ImmersiveSphereRenderer sphereRenderer)
                sphereRenderer.SetBrightness(alpha);
        }

        private static void CollectFrameMaterial(GameObject frameObj, List<Material> materials)
        {
            if (frameObj == null) return;
            var frame = frameObj.GetComponent<RTTMenuFrame>();
            if (frame == null) return;
            var quad = frame.GetDisplayQuad();
            if (quad?.material != null)
                materials.Add(quad.material);
        }
    }

    // ---------------------------------------------------------------------- //
    //  Context interface                                                        //
    // ---------------------------------------------------------------------- //

    /// <summary>
    /// All state and callbacks that <see cref="MediaPlaybackCoordinator"/> needs
    /// from its owning <c>VRMediaAppController</c>. Implemented as an interface
    /// so the coordinator remains decoupled from MonoBehaviour internals.
    /// </summary>
    public interface IPlaybackCoordinatorContext
    {
        // --- Projection system ---
        VRWorkspace.Media.Core.VRVideoProjectionSystem ProjectionSystem { get; }

        // --- Saved menu-frame pose (set in HideLibraryUI) ---
        Vector3 MenuFramePosition { get; }
        Quaternion MenuFrameRotation { get; }

        // --- UI GameObjects ---
        GameObject ControlsContainer { get; }
        GameObject PlayerControlsGroup { get; }
        GameObject ControlsFrameObject { get; }
        GameObject OverlayFrameObject { get; }
        GameObject SideControlsFrameObject { get; }
        GameObject MenuButtonFrameObject { get; }
        RTTMenuFrame ParentMenuFrame { get; }
        RTTFilePagination QueuePagination { get; }
        RTTMediaQueuePanel QueuePanel { get; }

        // --- UI settings state ---
        Vector3 PlayerControlsBaseLocalPos { get; set; }
        bool ControlsFollowCamera { get; set; }

        // --- Callbacks / delegated methods ---
        void ApplyUISettingsToControlsGroup();
        void UpdateSideControlsFacing();
        void PositionMenuButtonImmersive(Camera cam);
        void PositionMenuButtonFlat();
        void SetupControlsFollowCamera(Camera cam);
        void PositionSideControlsForProjection(bool isImmersive);

        void ShowPlayerUI();
        void HidePlayerUI();
        void ShowLibraryUI();
        void HideLibraryUI();
        void StartPlaybackInternal(MediaVideoInfo video);

        void SetAllPlayerFramesAlpha(float alpha);
        void SetAllLibraryFramesAlpha(float alpha);

        List<RTTMenuFrame> GetAllLibraryFrames();
    }
}
