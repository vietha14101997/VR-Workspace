using UnityEngine;

/// <summary>
/// Captures the scene behind an RTT panel and applies Gaussian blur.
/// Used for glassmorphism effects in RTT mode where GrabPass doesn't work.
///
/// Attach to an RTTCanvasBase panel to enable blur effect.
/// Call ApplyToMaterial() to assign the blurred texture to a glass material.
/// </summary>
public class RTTBlurBackgroundCapture : MonoBehaviour
{
    #region Serialized Fields
    [Header("Blur Settings")]
    [SerializeField] private float blurRadius = 4f;
    [SerializeField, Range(1, 4)] private int blurIterations = 2;
    [SerializeField, Range(0.1f, 0.5f)] private float downsampleFactor = 0.25f;

    [Header("Capture Settings")]
    [SerializeField] private LayerMask excludeLayers;
    [SerializeField, Range(1, 10)] private int refreshFrameInterval = 3;
    [SerializeField] private float captureDistance = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
    #endregion

    #region Private Fields
    private RTTCanvasBase _rttCanvas;
    private MeshRenderer _displayQuad;

    private Camera _captureCamera;
    private RenderTexture _capturedRT;
    private RenderTexture _blurTempRT;
    private RenderTexture _blurredOutputRT;

    private Material _blurMaterial;
    private Shader _blurShader;

    private bool _isInitialized = false;
    private bool _initFailed = false;
    private bool _isDirty = true;
    private int _framesSinceLastCapture = 0;

    // Shader property IDs
    private static readonly int BlurRadiusProp = Shader.PropertyToID("_BlurRadius");
    #endregion

    #region Properties
    /// <summary>Blurred background texture ready for use in materials</summary>
    public RenderTexture BlurredTexture => _blurredOutputRT;

    /// <summary>Is the blur capture system ready</summary>
    public bool IsReady => _isInitialized && _blurredOutputRT != null && _blurredOutputRT.IsCreated();

    /// <summary>Blur radius (affects blur intensity)</summary>
    public float BlurRadius
    {
        get => blurRadius;
        set { blurRadius = value; MarkDirty(); }
    }

    /// <summary>Number of blur passes (affects quality)</summary>
    public int BlurIterations
    {
        get => blurIterations;
        set { blurIterations = Mathf.Clamp(value, 1, 4); MarkDirty(); }
    }
    #endregion

    #region Initialization
    /// <summary>
    /// Initialize the blur capture system
    /// </summary>
    /// <param name="rttCanvas">The RTT panel this blur belongs to</param>
    /// <param name="displayQuad">The display quad (for positioning capture camera)</param>
    public void Initialize(RTTCanvasBase rttCanvas, MeshRenderer displayQuad)
    {
        _rttCanvas = rttCanvas;
        _displayQuad = displayQuad;

        if (_rttCanvas == null || _displayQuad == null)
        {
            Debug.LogError("[RTTBlurCapture] Invalid initialization: rttCanvas or displayQuad is null");
            _initFailed = true;
            return;
        }

        // Load blur shader
        _blurShader = Shader.Find("Hidden/RTT/GaussianBlur");
        if (_blurShader == null)
        {
            Debug.LogWarning("[RTTBlurCapture] GaussianBlur shader not found - falling back to procedural blur");
            _initFailed = true;
            return;
        }

        try
        {
            _blurMaterial = new Material(_blurShader);
            _blurMaterial.hideFlags = HideFlags.HideAndDontSave;

            if (!CreateRenderTextures())
            {
                Debug.LogWarning("[RTTBlurCapture] Failed to create RenderTextures - falling back to procedural blur");
                _initFailed = true;
                Cleanup();
                return;
            }

            SetupCaptureCamera();

            _isInitialized = true;
            _initFailed = false;
            MarkDirty();

            Debug.Log($"[RTTBlurCapture] Initialized for {rttCanvas.GetType().Name}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RTTBlurCapture] Initialization failed: {e.Message} - falling back to procedural blur");
            _initFailed = true;
            Cleanup();
        }
    }

    private bool CreateRenderTextures()
    {
        var panelResolution = _rttCanvas.CurrentResolution;
        if (panelResolution.x == 0 || panelResolution.y == 0)
        {
            panelResolution = new Vector2Int(1920, 1080);
        }

        int captureWidth = Mathf.Max(64, Mathf.RoundToInt(panelResolution.x * downsampleFactor));
        int captureHeight = Mathf.Max(64, Mathf.RoundToInt(panelResolution.y * downsampleFactor));

        // Get mobile-compatible format
        RenderTextureFormat format = GetMobileCompatibleFormat();
        int depthBits = GetMobileCompatibleDepthBits();

        try
        {
            // Captured scene texture
            _capturedRT = new RenderTexture(captureWidth, captureHeight, depthBits, format);
            _capturedRT.name = "RTTBlur_Captured";
            _capturedRT.filterMode = FilterMode.Bilinear;
            _capturedRT.antiAliasing = 1; // No MSAA for mobile compatibility
            if (!_capturedRT.Create())
            {
                Debug.LogError("[RTTBlurCapture] Failed to create _capturedRT");
                return false;
            }

            // Temporary blur texture
            _blurTempRT = new RenderTexture(captureWidth, captureHeight, 0, format);
            _blurTempRT.name = "RTTBlur_Temp";
            _blurTempRT.filterMode = FilterMode.Bilinear;
            _blurTempRT.antiAliasing = 1;
            if (!_blurTempRT.Create())
            {
                Debug.LogError("[RTTBlurCapture] Failed to create _blurTempRT");
                return false;
            }

            // Final blurred output
            _blurredOutputRT = new RenderTexture(captureWidth, captureHeight, 0, format);
            _blurredOutputRT.name = "RTTBlur_Output";
            _blurredOutputRT.filterMode = FilterMode.Bilinear;
            _blurredOutputRT.antiAliasing = 1;
            if (!_blurredOutputRT.Create())
            {
                Debug.LogError("[RTTBlurCapture] Failed to create _blurredOutputRT");
                return false;
            }

            Debug.Log($"[RTTBlurCapture] Created RTs: {captureWidth}x{captureHeight}, format={format}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RTTBlurCapture] RenderTexture creation failed: {e.Message}");
            return false;
        }
    }

    private RenderTextureFormat GetMobileCompatibleFormat()
    {
#if UNITY_ANDROID || UNITY_IOS
        // Use ARGB32 which is universally supported on mobile
        return RenderTextureFormat.ARGB32;
#else
        return RenderTextureFormat.ARGB32;
#endif
    }

    private int GetMobileCompatibleDepthBits()
    {
#if UNITY_ANDROID || UNITY_IOS
        // Use 16-bit depth on mobile for better compatibility
        return 16;
#else
        return 16;
#endif
    }

    private void SetupCaptureCamera()
    {
        var cameraGO = new GameObject("BlurCaptureCamera");
        cameraGO.transform.SetParent(transform);
        cameraGO.hideFlags = HideFlags.HideAndDontSave;

        _captureCamera = cameraGO.AddComponent<Camera>();
        _captureCamera.enabled = false; // Manual rendering only
        _captureCamera.clearFlags = CameraClearFlags.Skybox;
        _captureCamera.backgroundColor = new Color(0.2f, 0.3f, 0.4f, 1f); // Fallback color
        _captureCamera.targetTexture = _capturedRT;
        _captureCamera.allowHDR = false;
        _captureCamera.allowMSAA = false;
        _captureCamera.nearClipPlane = 0.01f;
        _captureCamera.farClipPlane = 50f;

        // Calculate culling mask (everything except UI and quad layer)
        int allLayers = ~0;
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer != -1) allLayers &= ~(1 << uiLayer);

        // Also exclude the quad's layer from capture
        if (_displayQuad != null)
        {
            int quadLayer = _displayQuad.gameObject.layer;
            allLayers &= ~(1 << quadLayer);
        }

        // Apply additional exclude layers
        allLayers &= ~excludeLayers.value;

        _captureCamera.cullingMask = allLayers;

        UpdateCameraTransform();
    }

    private void UpdateCameraTransform()
    {
        if (_captureCamera == null || _displayQuad == null) return;

        // Position camera slightly behind the quad, looking through it
        Transform quadTransform = _displayQuad.transform;
        Vector3 position = quadTransform.position - quadTransform.forward * captureDistance;

        _captureCamera.transform.position = position;
        _captureCamera.transform.rotation = quadTransform.rotation;

        // Calculate FOV based on quad size and distance
        Vector2 worldSize = _rttCanvas.GetWorldSize();
        float verticalSize = worldSize.y;
        float fov = 2f * Mathf.Atan2(verticalSize * 0.5f, captureDistance) * Mathf.Rad2Deg;
        _captureCamera.fieldOfView = Mathf.Clamp(fov, 30f, 120f);
    }
    #endregion

    #region Lifecycle
    private void LateUpdate()
    {
        if (!_isInitialized) return;

        _framesSinceLastCapture++;

        bool shouldCapture = _isDirty ||
            (_framesSinceLastCapture >= refreshFrameInterval);

        if (shouldCapture)
        {
            CaptureAndBlur();
            _isDirty = false;
            _framesSinceLastCapture = 0;
        }
    }

    private void OnDestroy()
    {
        Cleanup();
    }
    #endregion

    #region Capture and Blur
    /// <summary>
    /// Mark as dirty to force capture on next frame
    /// </summary>
    public void MarkDirty()
    {
        _isDirty = true;
    }

    /// <summary>
    /// Capture the scene and apply blur
    /// </summary>
    public void CaptureAndBlur()
    {
        if (!_isInitialized || _captureCamera == null) return;

        // Update camera position in case quad moved
        UpdateCameraTransform();

        // Capture scene
        _captureCamera.Render();

        // Apply blur
        ApplyBlur();

        if (showDebugInfo)
            Debug.Log($"[RTTBlurCapture] Captured and blurred frame {Time.frameCount}");
    }

    private void ApplyBlur()
    {
        if (_blurMaterial == null) return;

        _blurMaterial.SetFloat(BlurRadiusProp, blurRadius);

        RenderTexture current = _capturedRT;
        RenderTexture target = _blurTempRT;

        // Multiple blur iterations for stronger effect
        for (int i = 0; i < blurIterations; i++)
        {
            // Horizontal pass
            Graphics.Blit(current, target, _blurMaterial, 0);

            // Vertical pass
            if (i == blurIterations - 1)
            {
                // Final pass outputs to blurred result
                Graphics.Blit(target, _blurredOutputRT, _blurMaterial, 1);
            }
            else
            {
                // Intermediate passes swap buffers
                Graphics.Blit(target, current, _blurMaterial, 1);
            }
        }
    }
    #endregion

    #region Material Integration
    /// <summary>
    /// Apply the blurred texture to a material
    /// </summary>
    /// <param name="material">Target material with _BlurredBackgroundTex property</param>
    public void ApplyToMaterial(Material material)
    {
        if (material == null) return;

        if (_blurredOutputRT != null)
        {
            material.SetTexture("_BlurredBackgroundTex", _blurredOutputRT);
        }
    }

    /// <summary>
    /// Remove the blurred texture from a material
    /// </summary>
    public void RemoveFromMaterial(Material material)
    {
        if (material == null) return;
        material.SetTexture("_BlurredBackgroundTex", null);
    }
    #endregion

    #region Cleanup
    private void Cleanup()
    {
        if (_captureCamera != null)
        {
            if (Application.isPlaying)
                Destroy(_captureCamera.gameObject);
            else
                DestroyImmediate(_captureCamera.gameObject);
        }

        ReleaseRT(ref _capturedRT);
        ReleaseRT(ref _blurTempRT);
        ReleaseRT(ref _blurredOutputRT);

        if (_blurMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_blurMaterial);
            else
                DestroyImmediate(_blurMaterial);
            _blurMaterial = null;
        }

        _isInitialized = false;
    }

    private void ReleaseRT(ref RenderTexture rt)
    {
        if (rt != null)
        {
            rt.Release();
            if (Application.isPlaying)
                Destroy(rt);
            else
                DestroyImmediate(rt);
            rt = null;
        }
    }
    #endregion

    #region Debug
    private void OnDrawGizmosSelected()
    {
        if (!showDebugInfo || _captureCamera == null) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(_captureCamera.transform.position, 0.05f);
        Gizmos.DrawLine(_captureCamera.transform.position,
            _captureCamera.transform.position + _captureCamera.transform.forward * captureDistance);
    }
    #endregion
}
