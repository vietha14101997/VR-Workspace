using UnityEngine;
using System.Collections.Generic;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.Projections;
using VRWorkspace.Media.UI;

namespace VRWorkspace.Media.Core
{
    /// <summary>
    /// Manages video projection rendering for VR.
    /// Supports multiple projection types (Flat, 180, 360, SBS, OU, VR180).
    /// </summary>
    public class VRVideoProjectionSystem : MonoBehaviour
    {
        #region Properties
        /// <summary>Currently active projection type</summary>
        public VideoProjectionType CurrentProjection { get; private set; } = VideoProjectionType.Flat;

        /// <summary>Current stereo mode</summary>
        public StereoMode CurrentStereoMode { get; private set; } = StereoMode.Mono;

        /// <summary>Currently active projection renderer</summary>
        public IProjectionRenderer ActiveRenderer { get; private set; }

        /// <summary>Is projection currently visible</summary>
        public bool IsVisible { get; private set; }

        /// <summary>Current projection root world position</summary>
        public Vector3 ProjectionPosition => _projectionRoot != null ? _projectionRoot.position : Vector3.zero;

        /// <summary>Current projection root world rotation</summary>
        public Quaternion ProjectionRotation => _projectionRoot != null ? _projectionRoot.rotation : Quaternion.identity;

        /// <summary>Current screen scale (1.0 = default, 0.5–3.0). Used by the Gaze-mode
        /// Menu Button to keep its visual size and gaze-target collider proportional
        /// to the video screen.</summary>
        public float CurrentScale => _currentSettings.Scale;

        /// <summary>Live flat-screen world position (recomputed every LateUpdate from
        /// the camera's direction and the saved flat transform's distance). Falls back
        /// to <see cref="SavedFlatPosition"/> when the lazy position cache hasn't been
        /// populated yet. The Menu Button tracks this so it follows the video screen
        /// when the user dollies the camera in/out.</summary>
        public Vector3 FlatWorldPosition => _hasFlatWorldPosition ? _flatWorldPosition : _savedFlatPosition;
        #endregion

        #region Private Fields
        private Dictionary<VideoProjectionType, IProjectionRenderer> _renderers;
        private Transform _cameraRig;
        private Transform _projectionRoot;
        private DisplaySettings _currentSettings = DisplaySettings.Default;
        private bool _isInitialized = false;
        private Camera _cachedMainCamera; // Cached Camera.main — lookup is expensive per frame

        // Save flat position when switching to immersive so we can restore it
        private Vector3 _savedFlatPosition;
        private Quaternion _savedFlatRotation;
        private bool _hasSavedFlatTransform = false;

        // Cached flat screen placement (computed once, applied every frame by LateUpdate)
        private Vector3 _flatDirection = Vector3.forward;
        private Vector3 _flatWorldPosition;
        private bool _hasFlatWorldPosition = false;

        /// <summary>Saved flat screen position (for controls alignment in immersive mode)</summary>
        public Vector3 SavedFlatPosition => _savedFlatPosition;
        public bool HasSavedFlatTransform => _hasSavedFlatTransform;

        /// <summary>
        /// Update the saved flat transform. Flat mode applies the pose atomically so
        /// dependent UI never observes an intermediate pre-LateUpdate position.
        /// </summary>
        public void UpdateSavedFlatTransform(Vector3 position, Quaternion rotation)
        {
            _savedFlatPosition = position;
            _savedFlatRotation = rotation;
            _hasSavedFlatTransform = true;

            Camera cam = _cachedMainCamera != null ? _cachedMainCamera : (_cachedMainCamera = Camera.main);
            if (cam != null)
            {
                Vector3 horizontal = position - cam.transform.position;
                horizontal.y = 0f;
                if (horizontal.sqrMagnitude > 0.001f)
                {
                    _flatDirection = horizontal.normalized;
                }
                else
                {
                    Vector3 rotationForward = rotation * Vector3.forward;
                    rotationForward.y = 0f;
                    if (rotationForward.sqrMagnitude > 0.001f)
                        _flatDirection = rotationForward.normalized;
                }
            }

            if (IsImmersiveProjection())
            {
                // The immersive root must remain camera-centered with identity rotation.
                // Invalidate the flat cache so returning to flat recomputes from this pose.
                _hasFlatWorldPosition = false;
                return;
            }

            _flatWorldPosition = position;
            _hasFlatWorldPosition = true;

            if (_projectionRoot != null)
            {
                _projectionRoot.SetPositionAndRotation(position, rotation);
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// Initialize the projection system with camera rig reference.
        /// </summary>
        public void Initialize(Transform cameraRig)
        {
            if (_isInitialized)
            {
                Debug.LogWarning("[VRVideoProjectionSystem] Already initialized");
                return;
            }

            _cameraRig = cameraRig;

            // Create projection root
            GameObject rootObj = new GameObject("ProjectionRoot");
            rootObj.transform.SetParent(transform);

            // Set layer - use Default if VirtualObjects doesn't exist
            int layer = LayerMask.NameToLayer("VirtualObjects");
            if (layer < 0)
            {
                Debug.LogWarning("[VRVideoProjectionSystem] 'VirtualObjects' layer not found, using Default layer");
                layer = 0; // Default layer
            }
            rootObj.layer = layer;
            _projectionRoot = rootObj.transform;

            // Position at camera rig
            if (_cameraRig != null)
            {
                _projectionRoot.position = _cameraRig.position;
            }

            // Initialize renderers dictionary
            _renderers = new Dictionary<VideoProjectionType, IProjectionRenderer>();

            // Create flat projection renderer (default)
            CreateFlatRenderer();

            // Immersive renderer (ImmersiveSphereRenderer) created on demand when needed

            _isInitialized = true;
            Debug.Log("[VRVideoProjectionSystem] Initialized");
        }

        /// <summary>
        /// Set target position for the projection (e.g., menu frame position).
        /// This overrides the default camera-relative positioning.
        /// </summary>
        public void SetTargetPosition(Vector3 position, Quaternion rotation)
        {
            if (_projectionRoot != null)
            {
                // Save as flat reference — used for direction computation,
                // controls alignment in immersive mode, and restoring when switching back
                _savedFlatPosition = position;
                _savedFlatRotation = rotation;
                _hasSavedFlatTransform = true;

                // Compute screen direction and target world position
                ComputeFlatWorldPosition();

                // If flat projection is already visible, apply immediately
                if (IsVisible && !IsImmersiveProjection() && _hasFlatWorldPosition)
                {
                    _projectionRoot.position = _flatWorldPosition;
                    _projectionRoot.rotation = Quaternion.LookRotation(_flatDirection, Vector3.up);
                }
                else if (!IsVisible)
                {
                    // Not visible yet — set raw position as placeholder
                    _projectionRoot.position = position;
                    _projectionRoot.rotation = rotation;
                }

                Debug.Log($"[VRVideoProjectionSystem] Set target position: {position}, rotation: {rotation.eulerAngles}, flatWorldPos: {_flatWorldPosition}");
            }
        }

        /// <summary>
        /// Set active projection type and stereo mode.
        /// </summary>
        public void SetProjection(VideoProjectionType type, StereoMode stereo = StereoMode.Mono)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[VRVideoProjectionSystem] Not initialized");
                return;
            }

            bool wasImmersive = IsImmersiveProjection();
            bool willBeImmersive = IsImmersiveType(type);

            // Save flat position before switching TO immersive
            if (!wasImmersive && willBeImmersive && _projectionRoot != null)
            {
                _savedFlatPosition = _projectionRoot.position;
                _savedFlatRotation = _projectionRoot.rotation;
                _hasSavedFlatTransform = true;
                // Reset rotation to identity — immersive sphere uses shader-based rotation only.
                // Must happen before RecenterView() so the shader offset is calculated correctly.
                _projectionRoot.rotation = Quaternion.identity;
                Debug.Log($"[VRVideoProjectionSystem] Saved flat position: {_savedFlatPosition}");
            }

            // Restore flat position when switching FROM immersive TO flat
            if (wasImmersive && !willBeImmersive && _hasSavedFlatTransform && _projectionRoot != null)
            {
                _projectionRoot.position = _savedFlatPosition;
                _projectionRoot.rotation = _savedFlatRotation;
                Debug.Log($"[VRVideoProjectionSystem] Restored flat position: {_savedFlatPosition}");
            }

            // Hide current renderer
            if (ActiveRenderer != null && ActiveRenderer.IsActive)
            {
                ActiveRenderer.Hide();
            }

            // Get or create renderer for this type
            if (!_renderers.ContainsKey(type))
            {
                CreateRenderer(type);
            }

            if (!_renderers.ContainsKey(type))
            {
                Debug.LogError($"[VRVideoProjectionSystem] Failed to create renderer for {type}");
                return;
            }

            // Switch to new renderer
            CurrentProjection = type;
            CurrentStereoMode = stereo;
            ActiveRenderer = _renderers[type];

            // Configure immersive renderer projection mode (180 vs 360)
            if (ActiveRenderer is ImmersiveSphereRenderer immersive)
            {
                switch (type)
                {
                    case VideoProjectionType.Dome180:
                    case VideoProjectionType.VR180Stereo:
                        immersive.SetProjectionMode(ImmersiveSphereRenderer.ProjectionMode.Equirect180);
                        break;
                    case VideoProjectionType.Sphere360:
                        immersive.SetProjectionMode(ImmersiveSphereRenderer.ProjectionMode.Equirect360);
                        break;
                }
            }

            ActiveRenderer.SetStereoMode(stereo);
            ActiveRenderer.UpdateDisplay(_currentSettings);

            // Align immersive sphere center to the saved flat screen direction (menu frame).
            // Runs for any transition TO immersive (flat→immersive AND immersive→immersive on new video).
            if (willBeImmersive && _hasSavedFlatTransform && ActiveRenderer is ImmersiveSphereRenderer immersiveRenderer && _cameraRig != null)
            {
                Vector3 toScreen = _savedFlatPosition - _cameraRig.position;
                toScreen.y = 0;
                if (toScreen.sqrMagnitude > 0.001f)
                {
                    float yAngle = Mathf.Atan2(toScreen.x, toScreen.z) * Mathf.Rad2Deg;
                    immersiveRenderer.SetRotation(yAngle);
                    Debug.Log($"[VRVideoProjectionSystem] Aligned immersive center to flat direction: {yAngle:F1}°");
                }
                else
                {
                    // Fallback: flat screen at camera position, use camera forward
                    immersiveRenderer.RecenterView();
                }
            }

            if (IsVisible)
            {
                // Recompute flat position for the new projection
                if (!willBeImmersive)
                {
                    ComputeFlatWorldPosition();
                }
                ActiveRenderer.Show();
            }

            Debug.Log($"[VRVideoProjectionSystem] Set projection: {type}, stereo: {stereo}");
        }

        /// <summary>
        /// Update stereo mode state without re-applying projection.
        /// Used when FlatProjectionRenderer handles the animated transition itself.
        /// </summary>
        public void UpdateStereoModeOnly(StereoMode stereo)
        {
            CurrentStereoMode = stereo;
        }

        /// <summary>
        /// Set video texture for current projection.
        /// </summary>
        public void SetTexture(Texture texture)
        {
            ActiveRenderer?.SetTexture(texture);
        }

        /// <summary>
        /// Set NV12 textures for HEVC hardware decode.
        /// </summary>
        public void SetTextureNV12(Texture2D yPlane, Texture2D uvPlane)
        {
            ActiveRenderer?.SetTextureNV12(yPlane, uvPlane);
        }

        /// <summary>
        /// Update display settings (distance, scale, curvature).
        /// </summary>
        public void UpdateDisplay(DisplaySettings settings)
        {
            _currentSettings = settings;
            ActiveRenderer?.UpdateDisplay(settings);
        }

        /// <summary>
        /// Set screen distance (for flat projection).
        /// </summary>
        public void SetScreenDistance(float meters)
        {
            _currentSettings.Distance = Mathf.Clamp(meters, 1.0f, 3.0f);
            ActiveRenderer?.UpdateDisplay(_currentSettings);

            // Recompute world position with new distance (direction stays the same)
            if (IsVisible && !IsImmersiveProjection())
            {
                RecomputeFlatWorldPositionForDistance();
            }
        }

        /// <summary>
        /// Set screen scale.
        /// </summary>
        public void SetScreenScale(float scale)
        {
            _currentSettings.Scale = Mathf.Clamp(scale, 0.5f, 3.0f);
            ActiveRenderer?.UpdateDisplay(_currentSettings);
        }

        /// <summary>
        /// Set screen curvature (for flat projection).
        /// </summary>
        public void SetScreenCurvature(float curvature)
        {
            _currentSettings.Curvature = Mathf.Clamp01(curvature);
            ActiveRenderer?.UpdateDisplay(_currentSettings);
        }

        /// <summary>
        /// Set head tracking lock mode.
        /// </summary>
        public void SetHeadLocked(bool locked)
        {
            _currentSettings.HeadLocked = locked;
            ActiveRenderer?.UpdateDisplay(_currentSettings);
        }

        /// <summary>
        /// Get the immersive sphere renderer (if active or available).
        /// </summary>
        public ImmersiveSphereRenderer GetImmersiveRenderer()
        {
            if (ActiveRenderer is ImmersiveSphereRenderer immersive)
                return immersive;

            // Try to find from renderers dictionary
            if (_renderers != null)
            {
                foreach (var r in _renderers.Values)
                {
                    if (r is ImmersiveSphereRenderer imm)
                        return imm;
                }
            }
            return null;
        }

        /// <summary>
        /// Set vertical offset for flat screen position.
        /// </summary>
        public void SetVerticalOffset(float meters)
        {
            _currentSettings.PositionOffset = new Vector3(
                _currentSettings.PositionOffset.x,
                meters,
                _currentSettings.PositionOffset.z);
            ActiveRenderer?.UpdateDisplay(_currentSettings);
        }

        /// <summary>
        /// Set aspect ratio override for flat projection.
        /// </summary>
        public void SetAspectRatioOverride(string ratio)
        {
            if (ActiveRenderer is FlatProjectionRenderer flat)
                flat.SetAspectRatioOverride(ratio);
        }

        public string GetAspectRatioOverride()
        {
            if (ActiveRenderer is FlatProjectionRenderer flat)
                return flat.AspectRatioOverrideString;
            return "default";
        }

        /// <summary>
        /// Recenter the view.
        /// </summary>
        public void RecenterView()
        {
            ActiveRenderer?.RecenterView();
        }

        /// <summary>
        /// Show the projection.
        /// </summary>
        public void Show()
        {
            IsVisible = true;

            // Ensure we have a computed flat position for LateUpdate to apply
            if (!IsImmersiveProjection())
            {
                if (!_hasFlatWorldPosition)
                    ComputeFlatWorldPosition();

                // Apply immediately so the screen is visible on the very first frame
                if (_hasFlatWorldPosition)
                {
                    _projectionRoot.position = _flatWorldPosition;
                    _projectionRoot.rotation = Quaternion.LookRotation(_flatDirection, Vector3.up);
                }
            }

            ActiveRenderer?.Show();
        }

        /// <summary>
        /// Hide the projection.
        /// </summary>
        public void Hide()
        {
            IsVisible = false;
            ActiveRenderer?.Hide();
        }

        /// <summary>
        /// Clean up all resources.
        /// </summary>
        public void Dispose()
        {
            if (_renderers != null)
            {
                // Use HashSet to avoid disposing shared instances multiple times
                // (ImmersiveSphereRenderer is registered under Dome180, Sphere360, and VR180Stereo)
                var disposed = new System.Collections.Generic.HashSet<IProjectionRenderer>();
                foreach (var renderer in _renderers.Values)
                {
                    if (!disposed.Contains(renderer))
                    {
                        renderer.Dispose();
                        disposed.Add(renderer);
                    }
                }
                _renderers.Clear();
            }

            if (_projectionRoot != null)
            {
                Destroy(_projectionRoot.gameObject);
                _projectionRoot = null;
            }

            _isInitialized = false;
            _hasFlatWorldPosition = false;
            Debug.Log("[VRVideoProjectionSystem] Disposed");
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Compute the screen direction from camera and cache the target world position.
        /// Direction is determined from camera toward savedFlatPosition; if they overlap
        /// (e.g., VirtualObjects Z offset makes menu frame world pos ≈ camera pos),
        /// falls back to camera forward.
        /// </summary>
        private void ComputeFlatWorldPosition()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            float distance;

            // Determine horizontal direction from camera to the saved flat position
            if (_hasSavedFlatTransform)
            {
                Vector3 toSaved = _savedFlatPosition - cam.transform.position;
                Vector3 horizontal = new Vector3(toSaved.x, 0f, toSaved.z);

                if (horizontal.sqrMagnitude > 0.01f)
                {
                    // Menu frame is far enough from camera to determine direction
                    _flatDirection = horizontal.normalized;
                    // Use actual horizontal distance to saved position.
                    // _currentSettings.Distance may be 0 (flat mode) which would place
                    // the screen at camera position, causing it to follow the camera.
                    distance = horizontal.magnitude;
                }
                else
                {
                    // Menu frame world position ≈ camera position
                    // (common when VirtualObjects Z offset cancels menu frame local position)
                    // Use camera's current horizontal forward as direction
                    Vector3 fwd = cam.transform.forward;
                    fwd.y = 0f;
                    _flatDirection = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
                    distance = 2.0f; // Fallback: place 2m in front
                }
            }
            else
            {
                // No saved position — use camera forward and _currentSettings.Distance
                Vector3 fwd = cam.transform.forward;
                fwd.y = 0f;
                _flatDirection = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
                distance = Mathf.Max(_currentSettings.Distance, 1.5f);
            }

            // Compute world position: distance from camera in the flat direction
            _flatWorldPosition = cam.transform.position + _flatDirection * distance;
            _flatWorldPosition.y = _hasSavedFlatTransform ? _savedFlatPosition.y : cam.transform.position.y;
            _hasFlatWorldPosition = true;

            Debug.Log($"[VRVideoProjectionSystem] ComputeFlatWorldPosition: cam={cam.transform.position}, dir={_flatDirection}, dist={distance}, result={_flatWorldPosition}");
        }

        /// <summary>
        /// Recompute flat world position using the already-cached direction but new Distance.
        /// Called when Distance changes via SetScreenDistance.
        /// </summary>
        private void RecomputeFlatWorldPositionForDistance()
        {
            if (!_hasFlatWorldPosition) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            // Keep the same direction, just change distance
            _flatWorldPosition = cam.transform.position + _flatDirection * _currentSettings.Distance;
            _flatWorldPosition.y = _hasSavedFlatTransform ? _savedFlatPosition.y : cam.transform.position.y;

            Debug.Log($"[VRVideoProjectionSystem] RecomputeForDistance: dist={_currentSettings.Distance}, result={_flatWorldPosition}");
        }

        private void CreateRenderer(VideoProjectionType type)
        {
            switch (type)
            {
                case VideoProjectionType.Flat:
                    CreateFlatRenderer();
                    break;

                case VideoProjectionType.Dome180:
                case VideoProjectionType.Sphere360:
                case VideoProjectionType.VR180Stereo:
                    CreateImmersiveRenderer();
                    break;
            }
        }

        private void CreateFlatRenderer()
        {
            if (_renderers.ContainsKey(VideoProjectionType.Flat)) return;

            GameObject rendererObj = new GameObject("FlatProjection");
            rendererObj.transform.SetParent(_projectionRoot);

            FlatProjectionRenderer renderer = rendererObj.AddComponent<FlatProjectionRenderer>();
            renderer.Initialize(_projectionRoot);

            _renderers[VideoProjectionType.Flat] = renderer;

            Debug.Log("[VRVideoProjectionSystem] Created FlatProjectionRenderer");
        }

        /// <summary>
        /// Create a single ImmersiveSphereRenderer shared by all immersive projection types.
        /// Uses one inverted sphere mesh with shader-based 180/360 mode switching.
        /// </summary>
        private void CreateImmersiveRenderer()
        {
            // Single instance shared across Dome180, Sphere360, and VR180Stereo
            if (_renderers.ContainsKey(VideoProjectionType.Dome180)) return;

            GameObject rendererObj = new GameObject("ImmersiveProjection");
            rendererObj.transform.SetParent(_projectionRoot);

            ImmersiveSphereRenderer renderer = rendererObj.AddComponent<ImmersiveSphereRenderer>();
            renderer.Initialize(_projectionRoot);

            // Register same instance under all immersive projection keys
            _renderers[VideoProjectionType.Dome180] = renderer;
            _renderers[VideoProjectionType.Sphere360] = renderer;
            _renderers[VideoProjectionType.VR180Stereo] = renderer;

            Debug.Log("[VRVideoProjectionSystem] Created ImmersiveSphereRenderer (shared for 180/360/VR180)");
        }
        #endregion

        #region Unity Lifecycle
        private void LateUpdate()
        {
            if (_projectionRoot == null) return;
            if (!IsVisible) return; // Skip all work when projection is hidden

            // Cache Camera.main — the lookup internally does FindGameObjectWithTag.
            var cam = _cachedMainCamera != null ? _cachedMainCamera : (_cachedMainCamera = Camera.main);
            if (cam == null) return;

            if (IsImmersiveProjection())
            {
                // Immersive projections (360/dome) surround the viewer — follow camera position
                // Rotation must stay identity: shader uses object-space directions for UV mapping,
                // so any parent rotation would misalign the projection center.
                _projectionRoot.position = cam.transform.position;
                _projectionRoot.rotation = Quaternion.identity;
            }
            else if (_hasFlatWorldPosition)
            {
                // Flat projection: apply cached world position (fixed in world space)
                // and face-to-camera rotation.
                _projectionRoot.position = _flatWorldPosition;

                Vector3 toCamera = cam.transform.position - _flatWorldPosition;
                if (toCamera.sqrMagnitude > 0.001f)
                {
                    _projectionRoot.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
            }
        }

        /// <summary>
        /// Whether the current projection type is immersive (180/360).
        /// </summary>
        public bool IsImmersiveProjection()
        {
            return IsImmersiveType(CurrentProjection);
        }

        private static bool IsImmersiveType(VideoProjectionType type)
        {
            return type == VideoProjectionType.Dome180
                || type == VideoProjectionType.VR180Stereo
                || type == VideoProjectionType.Sphere360;
        }

        /// <summary>
        /// Zoom in immersive mode by adjusting FOV. Negative delta = zoom in, positive = zoom out.
        /// </summary>
        public void ZoomImmersive(float delta)
        {
            if (ActiveRenderer is ImmersiveSphereRenderer immersive)
            {
                immersive.ZoomByFOV(delta);
            }
        }

        /// <summary>
        /// Reset immersive zoom to default FOV.
        /// </summary>
        public void ResetImmersiveZoom()
        {
            if (ActiveRenderer is ImmersiveSphereRenderer immersive)
            {
                immersive.ResetFOVZoom();
            }
        }

        private void OnDestroy()
        {
            Dispose();
        }
        #endregion
    }

}
