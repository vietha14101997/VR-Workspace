using UnityEngine;
using System.Collections.Generic;

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
    #endregion

    #region Private Fields
    private Dictionary<VideoProjectionType, IProjectionRenderer> _renderers;
    private Transform _cameraRig;
    private Transform _projectionRoot;
    private DisplaySettings _currentSettings = DisplaySettings.Default;
    private bool _isInitialized = false;

    // Save flat position when switching to immersive so we can restore it
    private Vector3 _savedFlatPosition;
    private Quaternion _savedFlatRotation;
    private bool _hasSavedFlatTransform = false;
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
            _projectionRoot.position = position;
            _projectionRoot.rotation = rotation;
            // New target position invalidates any saved flat transform from previous session
            _hasSavedFlatTransform = false;
            Debug.Log($"[VRVideoProjectionSystem] Set target position: {position}, rotation: {rotation.eulerAngles}");
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

        // Align immersive sphere center to the flat screen direction (from camera).
        // Only on flat→immersive transition; immersive→immersive keeps current rotation.
        if (willBeImmersive && !wasImmersive && ActiveRenderer is ImmersiveSphereRenderer immersiveRenderer && _cameraRig != null)
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
            ActiveRenderer.Show();
        }

        Debug.Log($"[VRVideoProjectionSystem] Set projection: {type}, stereo: {stereo}");
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
        _currentSettings.Distance = Mathf.Clamp(meters, 1.0f, 5.0f);
        ActiveRenderer?.UpdateDisplay(_currentSettings);
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
        Debug.Log("[VRVideoProjectionSystem] Disposed");
    }
    #endregion

    #region Private Methods
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
        if (_projectionRoot == null || _cameraRig == null) return;

        if (IsImmersiveProjection())
        {
            // Immersive projections (360/dome) surround the viewer — follow camera position
            // Rotation must stay identity: shader uses object-space directions for UV mapping,
            // so any parent rotation would misalign the projection center.
            _projectionRoot.position = _cameraRig.position;
            _projectionRoot.rotation = Quaternion.identity;
        }
        else if (IsVisible)
        {
            // Flat projection: face-to-camera rotation (same as MenuFrame/ClusterRig panels)
            Vector3 toCamera = _cameraRig.position - _projectionRoot.position;
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
