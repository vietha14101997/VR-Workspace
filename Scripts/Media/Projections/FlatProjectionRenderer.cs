using UnityEngine;

/// <summary>
/// Flat screen projection renderer for standard 2D video.
/// Supports optional screen curvature for immersive viewing.
/// </summary>
public class FlatProjectionRenderer : MonoBehaviour, IProjectionRenderer
{
    #region Constants
    private const string SHADER_NAME = "VRWorkspace/Media/VideoFlatProjection";
    private const string FALLBACK_SHADER = "Unlit/Texture";
    private const int CURVED_SEGMENTS = 32;
    #endregion

    #region Properties
    public VideoProjectionType Type => VideoProjectionType.Flat;
    public bool IsActive => _isActive;
    #endregion

    #region Private Fields
    private bool _isActive = false;
    private bool _isInitialized = false;

    private WorldPanelPlus _worldPanel;
    private Transform _parentTransform;
    private Vector2Int _resolution = new Vector2Int(1920, 1080);
    private DisplaySettings _currentSettings = DisplaySettings.Default;

    // Recenter tracking
    private Quaternion _recenterRotation = Quaternion.identity;
    #endregion

    #region IProjectionRenderer Implementation
    public void Initialize(Transform parent)
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[FlatProjectionRenderer] Already initialized");
            return;
        }

        _parentTransform = parent;
        CreateScreenObject();
        _isInitialized = true;
        Hide(); // Start hidden
    }

    public void SetTexture(Texture texture)
    {
        if (_worldPanel == null) return;

        _worldPanel.contentTexture = texture;
        _worldPanel.Apply();
        
        Debug.Log($"[FlatProjectionRenderer] SetTexture: {(texture != null ? $"{texture.width}x{texture.height}" : "null")}");

        // Update resolution for aspect ratio calculations
        if (texture != null)
        {
            _resolution = new Vector2Int(texture.width, texture.height);
            UpdateScreenAspect();
        }
    }

    public void SetTextureNV12(Texture2D yPlane, Texture2D uvPlane)
    {
        // WorldPanelPlus standard shader doesn't support NV12 out of the box with edge feathering.
        // For now, we fallback to setting the Y plane as content (grayscale) or would need a custom shader.
        // Assuming Windows platform where direct texture is mostly used.
        // If NV12 is strict requirement, we would need to swap the WorldPanelPlus material shader here.
        
        if (_worldPanel == null) return;
        
        _worldPanel.contentTexture = yPlane; // Fallback
        _worldPanel.Apply();

        if (yPlane != null)
        {
            _resolution = new Vector2Int(yPlane.width, yPlane.height);
            UpdateScreenAspect();
        }
    }

    public void SetStereoMode(StereoMode mode)
    {
        if (_worldPanel == null) return;

        _worldPanel.stereoMode = mode;
        _worldPanel.Apply();
    }

    public void UpdateDisplay(DisplaySettings settings)
    {
        _currentSettings = settings;

        if (_worldPanel == null) return;

        // Position screen at local origin - parent transform handles world positioning
        // Apply small Z offset based on distance setting for fine-tuning
        Vector3 localPos = new Vector3(
            settings.PositionOffset.x,
            settings.PositionOffset.y,
            settings.Distance // Use distance as Z offset from parent
        );
        _worldPanel.transform.localPosition = localPos;
        
        // Keep facing away from parent origin (toward viewer)
        _worldPanel.transform.localRotation = settings.RotationOffset;

        // Update size based on scale and aspect ratio
        UpdateScreenAspect();
    }

    public void Show()
    {
        if (_worldPanel != null)
        {
            _worldPanel.gameObject.SetActive(true);
            UpdateDisplay(_currentSettings); // Ensure position is updated
        }
        _isActive = true;
    }

    public void Hide()
    {
        if (_worldPanel != null)
        {
            _worldPanel.gameObject.SetActive(false);
        }
        _isActive = false;
    }

    public void RecenterView()
    {
        if (Camera.main != null)
        {
            // Store current camera forward as the new center
            Vector3 forward = Camera.main.transform.forward;
            forward.y = 0; // Keep horizontal
            if (forward.sqrMagnitude > 0.001f)
            {
                _recenterRotation = Quaternion.LookRotation(forward.normalized);
            }
        }
        else
        {
            _recenterRotation = Quaternion.identity;
        }

        UpdateDisplay(_currentSettings);
    }

    public void Dispose()
    {
        if (_worldPanel != null)
        {
            Destroy(_worldPanel.gameObject);
            _worldPanel = null;
        }

        _isInitialized = false;
        _isActive = false;
    }
    #endregion

    #region Private Methods
    private void CreateScreenObject()
    {
        GameObject go = new GameObject("FlatVideoScreen");
        go.transform.SetParent(_parentTransform, false);
        
        // Set layer - use Default if VirtualObjects doesn't exist
        int layer = LayerMask.NameToLayer("VirtualObjects");
        if (layer < 0) layer = 0;
        go.layer = layer;

        // Add WorldPanelPlus
        _worldPanel = go.AddComponent<WorldPanelPlus>();
        
        // Configure WorldPanelPlus defaults
        _worldPanel.useBoardEdgeFeather = true;
        _worldPanel.boardEdgeWidthUV = 0.02f;
        _worldPanel.boardCornerRadius = 0.03f;
        _worldPanel.boardEdgeColor = Color.black; // Dark border looks good for video
        _worldPanel.panelTint = Color.white;
        _worldPanel.enableSharpening = true; // Enable sharpening for video
        _worldPanel.sharpnessStrength = 0.5f;
        _worldPanel.anisoLevel = 16;
        _worldPanel.mipMapBias = -0.5f;
        
        // Initialize
        _worldPanel.Rebuild();
    }

    private void UpdateScreenAspect()
    {
        if (_worldPanel == null) return;

        float resX = _resolution.x;
        float resY = _resolution.y;

        // Adjust resolution for aspect ratio calculation based on stereo mode
        if (_worldPanel.stereoMode == StereoMode.SideBySide)
        {
            resX /= 2f;
        }
        else if (_worldPanel.stereoMode == StereoMode.OverUnder)
        {
            resY /= 2f;
        }

        float aspect = resY > 0 ? resX / resY : 16f / 9f;

        // Base height of 1 meter, adjust width by aspect ratio
        float baseHeight = 1.0f * _currentSettings.Scale;
        float baseWidth = baseHeight * aspect;

        bool changed = false;
        if (Mathf.Abs(_worldPanel.width - baseWidth) > 0.001f)
        {
            _worldPanel.width = baseWidth;
            changed = true;
        }
        if (Mathf.Abs(_worldPanel.height - baseHeight) > 0.001f)
        {
            _worldPanel.height = baseHeight;
            changed = true;
        }

        if (changed)
        {
            // Apply scale changes (WorldPanelPlus Apply handles localScale based on width/height)
            _worldPanel.Apply();
        }
    }
    #endregion
    #region Unity Lifecycle
    private void OnDestroy()
    {
        Dispose();
    }
    #endregion
}
