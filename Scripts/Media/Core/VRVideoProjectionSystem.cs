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
    #endregion

    #region Private Fields
    private Dictionary<VideoProjectionType, IProjectionRenderer> _renderers;
    private Transform _cameraRig;
    private Transform _projectionRoot;
    private DisplaySettings _currentSettings = DisplaySettings.Default;
    private bool _isInitialized = false;
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

        // Other renderers will be created on demand in Phase 3
        // CreateDome180Renderer();
        // CreateSphere360Renderer();
        // CreateStereoscopicRenderer();

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
        ActiveRenderer.SetStereoMode(stereo);
        ActiveRenderer.UpdateDisplay(_currentSettings);

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
            foreach (var renderer in _renderers.Values)
            {
                renderer.Dispose();
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
            case VideoProjectionType.SideBySide3D:
            case VideoProjectionType.OverUnder3D:
                CreateFlatRenderer();
                break;

            case VideoProjectionType.Dome180:
            case VideoProjectionType.VR180Stereo:
                CreateDome180Renderer();
                break;

            case VideoProjectionType.Sphere360:
                CreateSphere360Renderer();
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

        // Flat renderer is also used for SBS and OU
        _renderers[VideoProjectionType.SideBySide3D] = renderer;
        _renderers[VideoProjectionType.OverUnder3D] = renderer;

        Debug.Log("[VRVideoProjectionSystem] Created FlatProjectionRenderer");
    }

    private void CreateDome180Renderer()
    {
        if (_renderers.ContainsKey(VideoProjectionType.Dome180)) return;

        GameObject rendererObj = new GameObject("Dome180Projection");
        rendererObj.transform.SetParent(_projectionRoot);

        Dome180Renderer renderer = rendererObj.AddComponent<Dome180Renderer>();
        renderer.Initialize(_projectionRoot);

        _renderers[VideoProjectionType.Dome180] = renderer;

        // Dome180 renderer is also used for VR180 Stereo
        _renderers[VideoProjectionType.VR180Stereo] = renderer;

        Debug.Log("[VRVideoProjectionSystem] Created Dome180Renderer");
    }

    private void CreateSphere360Renderer()
    {
        if (_renderers.ContainsKey(VideoProjectionType.Sphere360)) return;

        GameObject rendererObj = new GameObject("Sphere360Projection");
        rendererObj.transform.SetParent(_projectionRoot);

        Sphere360Renderer renderer = rendererObj.AddComponent<Sphere360Renderer>();
        renderer.Initialize(_projectionRoot);

        _renderers[VideoProjectionType.Sphere360] = renderer;

        Debug.Log("[VRVideoProjectionSystem] Created Sphere360Renderer");
    }
    #endregion

    #region Unity Lifecycle
    private void LateUpdate()
    {
        if (_projectionRoot == null || _cameraRig == null) return;

        if (IsImmersiveProjection())
        {
            // Immersive projections (360/dome) surround the viewer — follow camera position
            _projectionRoot.position = _cameraRig.position;
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

    private bool IsImmersiveProjection()
    {
        return CurrentProjection == VideoProjectionType.Dome180
            || CurrentProjection == VideoProjectionType.VR180Stereo
            || CurrentProjection == VideoProjectionType.Sphere360;
    }

    private void OnDestroy()
    {
        Dispose();
    }
    #endregion
}
