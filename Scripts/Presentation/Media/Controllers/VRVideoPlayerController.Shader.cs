using UnityEngine;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.Projections;
using VRWorkspace.Media.UI;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Media.Core
{
    /// <summary>
    /// VRVideoPlayerController partial: Stereo shader, zoom override, controls positioning, and stereo conversion helpers.
    /// </summary>
    public partial class VRVideoPlayerController
    {
        /// <summary>
        /// Reposition the controls container to be centered in front of the video.
        /// For immersive (180/360): centers in front of camera at 2m distance.
        /// For flat: centers below the flat screen position.
        /// </summary>
        private void RepositionControlsForProjection(bool isImmersive, bool forceReposition = true)
        {
            Camera cam = Camera.main;
            if (cam == null || _controlsPanel == null) return;

            // Find VideoControlsContainer by traversing up from controls panel
            Transform container = FindControlsContainer();
            if (container == null) return;

            Vector3 camPos = cam.transform.position;
            Vector3 camForward = cam.transform.forward;
            camForward.y = 0;
            if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
            camForward.Normalize();

            Vector3 newPos = container.position;
            Vector3 facingDir = camForward;

            bool keptPosition = false;

            if (isImmersive)
            {
                if (!forceReposition)
                {
                    // Flat→immersive transition: keep current world position (no visible jump)
                    Vector3 currentPos = container.position;
                    Vector3 toControls = currentPos - camPos;
                    toControls.y = 0;
                    float dist = toControls.magnitude;

                    if (dist > 0.5f)
                    {
                        newPos = currentPos;
                        facingDir = toControls.normalized;
                        keptPosition = true;
                    }
                }

                if (!keptPosition)
                {
                    // Force reposition or no valid current position:
                    // use saved flat position direction so controls align with video content center
                    Vector3 contentDir = camForward;
                    if (_projectionSystem != null && _projectionSystem.HasSavedFlatTransform)
                    {
                        Vector3 toContent = _projectionSystem.SavedFlatPosition - camPos;
                        toContent.y = 0;
                        if (toContent.sqrMagnitude > 0.001f)
                            contentDir = toContent.normalized;
                    }

                    // Distance 2.0m — same as flat mode; 3D effect is disabled when controls are visible
                    newPos = camPos + contentDir * 2.0f;
                    newPos.y = camPos.y - 0.625f;
                    facingDir = contentDir;
                }
            }
            else
            {
                // Flat: position controls below the screen, facing same direction as screen
                Vector3 screenPos = _projectionSystem != null
                    ? _projectionSystem.ProjectionPosition
                    : container.position;

                newPos = screenPos;
                newPos.y = camPos.y - 0.625f;

                // Face direction = from camera toward screen (horizontal)
                Vector3 toScreen = screenPos - camPos;
                toScreen.y = 0;
                if (toScreen.sqrMagnitude < 0.001f) toScreen = camForward;
                toScreen.Normalize();
                facingDir = toScreen;
            }

            container.position = newPos;
            container.rotation = Quaternion.LookRotation(facingDir);

            // Scale: always 1.0 — immersive now at same 2.0m distance as flat
            float scaleFactor = 1.0f;

            Transform frame = container.Find("VideoControlsFrame");
            if (frame != null)
            {
                frame.localRotation = Quaternion.identity;
                ScaleFrameQuad(frame, scaleFactor);
            }

            Transform overlay = container.Find("DismissOverlayFrame");
            if (overlay != null)
            {
                overlay.localRotation = Quaternion.identity;
                ScaleFrameQuad(overlay, scaleFactor);
            }

            Transform sideFrame = container.Find("SideControlsFrame");
            if (sideFrame != null)
            {
                ScaleFrameQuad(sideFrame, scaleFactor);
            }

            Debug.Log($"[VRVideoPlayerController] Controls repositioned (immersive={isImmersive}, force={forceReposition}, scale={scaleFactor:F2}) at {newPos}");
        }

        /// <summary>
        /// Find the VideoControlsContainer transform by traversing up from controls panel.
        /// </summary>
        private Transform FindControlsContainer()
        {
            if (_controlsPanel == null) return null;

            Transform current = _controlsPanel.transform;
            while (current.parent != null && current.name != "VideoControlsContainer")
            {
                current = current.parent;
            }
            return current.name == "VideoControlsContainer" ? current : null;
        }

        /// <summary>
        /// Scale an RTTMenuFrame's DisplayQuad to maintain angular size at different distances.
        /// scaleFactor=1.0 for standard 2m distance, 1.25 for 2.5m immersive distance.
        /// </summary>
        private void ScaleFrameQuad(Transform frameTransform, float scaleFactor)
        {
            var menuFrame = frameTransform.GetComponent<RTTMenuFrame>();
            if (menuFrame == null) return;

            var quad = menuFrame.GetDisplayQuad();
            if (quad == null) return;

            // Store original scale on first access
            if (!_controlsQuadOriginalScales.ContainsKey(frameTransform))
            {
                _controlsQuadOriginalScales[frameTransform] = quad.transform.localScale;
            }

            Vector3 orig = _controlsQuadOriginalScales[frameTransform];
            quad.transform.localScale = new Vector3(
                orig.x * scaleFactor,
                orig.y * scaleFactor,
                orig.z
            );
        }

        /// <summary>
        /// Handle controls panel visibility changes.
        /// When controls are visible in stereo mode: instantly set mono rendering
        /// to eliminate vergence-accommodation conflict. Works for both immersive and flat modes.
        /// Note: Animation was tested but causes worse dizziness (sustained rotation from UV shift).
        /// Instant switch produces only a brief "pop" which the brain dismisses easily.
        /// </summary>
        private void HandleControlsVisibilityChanged(bool visible)
        {
            if (_projectionSystem?.ActiveRenderer == null) return;

            bool isStereo = _projectionSystem.CurrentStereoMode != StereoMode.Mono;
            if (!isStereo) return;

            _projectionSystem.ActiveRenderer.SetStereoStrength(visible ? 0f : 1f);
        }

        /// <summary>
        /// Ensure controls panel uses StereoUIPanel shader with given stereo offset.
        /// Currently always called with offset=0 (vergence handled by force-mono on video).
        /// </summary>
        private void SetControlsStereoDepthOffset(float offset)
        {
            Transform container = FindControlsContainer();
            if (container == null) return;

            // Always use StereoUIPanel shader — avoid shader swapping artifacts.
            // With offset=0, StereoUIPanel renders identically to Sprites/Default.
            Shader stereoShader = Shader.Find(STEREO_UI_SHADER);

            Transform frame = container.Find("VideoControlsFrame");
            if (frame != null) ApplyStereoShader(frame, stereoShader, offset);

            Transform overlay = container.Find("DismissOverlayFrame");
            if (overlay != null) ApplyStereoShader(overlay, stereoShader, offset);

            Transform sideFrame = container.Find("SideControlsFrame");
            if (sideFrame != null) ApplyStereoShader(sideFrame, stereoShader, offset);
        }

        private void ApplyStereoShader(Transform frameTransform, Shader stereoShader, float offset)
        {
            var menuFrame = frameTransform.GetComponent<RTTMenuFrame>();
            if (menuFrame == null) return;
            var quad = menuFrame.GetDisplayQuad();
            if (quad?.material == null) return;

            // Swap to StereoUIPanel shader once (subsequent calls skip if already applied)
            if (stereoShader != null && quad.material.shader != stereoShader)
            {
                Texture tex = quad.material.mainTexture;
                Color color = quad.material.color;
                int queue = quad.material.renderQueue;
                quad.material.shader = stereoShader;
                quad.material.mainTexture = tex;
                quad.material.color = color;
                quad.material.renderQueue = queue;
            }

            if (quad.material.HasProperty("_StereoOffset"))
                quad.material.SetFloat("_StereoOffset", offset);
        }

        /// <summary>
        /// Setup zoom override so zoom inside video player never affects VirtualObjects.
        /// Flat: zoom adjusts screen scale. Immersive: zoom adjusts FOV.
        /// </summary>
        private void SetupZoomOverride(bool isImmersive)
        {
            var zoomController = VirtualObjectsZoomController.Instance;
            if (zoomController == null) return;

            if (isImmersive && _projectionSystem != null)
            {
                zoomController.SetZoomOverride(
                    () => _projectionSystem.ZoomImmersive(-10f),  // zoom in = decrease FOV
                    () => _projectionSystem.ZoomImmersive(10f)    // zoom out = increase FOV
                );
            }
            else
            {
                // Flat mode: zoom adjusts screen scale (isolated from VirtualObjects)
                zoomController.SetZoomOverride(
                    () => SetScreenScale(_displaySettings.Scale + 0.1f),  // zoom in = bigger
                    () => SetScreenScale(_displaySettings.Scale - 0.1f)   // zoom out = smaller
                );
            }
        }

        private RTTMediaProjectionPopup.StereoMode ConvertToUIStereo(StereoMode mode)
        {
            switch (mode)
            {
                case StereoMode.SideBySide: return RTTMediaProjectionPopup.StereoMode.SideBySide;
                case StereoMode.OverUnder: return RTTMediaProjectionPopup.StereoMode.OverUnder;
                default: return RTTMediaProjectionPopup.StereoMode.Mono;
            }
        }

        private StereoMode ConvertFromUIStereo(RTTMediaProjectionPopup.StereoMode mode)
        {
            switch (mode)
            {
                case RTTMediaProjectionPopup.StereoMode.SideBySide: return StereoMode.SideBySide;
                case RTTMediaProjectionPopup.StereoMode.OverUnder: return StereoMode.OverUnder;
                default: return StereoMode.Mono;
            }
        }
    }
}
