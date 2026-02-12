using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// Controls the VR environment based on video projection type.
/// - Flat/Mono mode: Uses existing environment with lights toggle
/// - 180°/360° mode: Hides environment (video becomes the environment)
/// </summary>
public class MediaEnvironmentController : MonoBehaviour
{
    #region Constants
    private const float FADE_DURATION = 0.5f;
    private const float ENVIRONMENT_SCALE_HIDDEN = 0.001f;
    #endregion

    #region Events
    public event Action<bool> OnLightsChanged;
    public event Action<bool> OnEnvironmentVisibilityChanged;
    #endregion

    #region Properties
    public bool LightsEnabled { get; private set; } = true;
    public bool EnvironmentVisible { get; private set; } = true;
    public VideoProjectionType CurrentProjection { get; private set; } = VideoProjectionType.Flat;
    #endregion

    #region Private Fields
    private GameObject _environmentRoot;
    private Light[] _sceneLights;
    private float[] _originalLightIntensities;
    private Vector3 _originalEnvironmentScale;
    private Coroutine _fadeCoroutine;
    private bool _isInitialized;

    // Visibility Reason Tracking
    private System.Collections.Generic.HashSet<string> _visibilityBlockers = new System.Collections.Generic.HashSet<string>();

    // Cached references
    private Material _skyboxMaterial;
    private Color _originalAmbientColor;
    private float _originalAmbientIntensity;
    #endregion

    #region Singleton
    private static MediaEnvironmentController _instance;
    public static MediaEnvironmentController Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject obj = new GameObject("MediaEnvironmentController");
                _instance = obj.AddComponent<MediaEnvironmentController>();
                DontDestroyOnLoad(obj);
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }
    #endregion

    #region Initialization
    /// <summary>
    /// Initialize with environment root object.
    /// </summary>
    public void Initialize(GameObject environmentRoot = null)
    {
        if (_isInitialized) return;

        // Find environment root if not provided
        _environmentRoot = environmentRoot ?? FindEnvironmentRoot();

        // Cache all scene lights
        CacheLights();

        // Cache ambient settings
        CacheAmbientSettings();

        // Store original scale
        if (_environmentRoot != null)
        {
            _originalEnvironmentScale = _environmentRoot.transform.localScale;
        }

        _isInitialized = true;
        Debug.Log("[MediaEnvironmentController] Initialized");
    }

    private GameObject FindEnvironmentRoot()
    {
        // Try to find common environment root names
        string[] possibleNames = { "Environment", "VREnvironment", "VirtualEnvironment", "Room", "Scene", "World" };

        foreach (string name in possibleNames)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                Debug.Log($"[MediaEnvironmentController] Found environment root: {name}");
                return obj;
            }
        }

        // Try to find by tag - wrapped in try-catch because tag may not exist
        try
        {
            GameObject taggedEnv = GameObject.FindWithTag("Environment");
            if (taggedEnv != null)
            {
                Debug.Log("[MediaEnvironmentController] Found environment root by tag");
                return taggedEnv;
            }
        }
        catch (UnityException)
        {
            // Tag doesn't exist in project settings, skip
            Debug.LogWarning("[MediaEnvironmentController] 'Environment' tag not defined, skipping tag search");
        }

        Debug.LogWarning("[MediaEnvironmentController] Could not find environment root - environment control will be limited");
        return null;
    }

    private void CacheLights()
    {
        // Find all lights in the scene (excluding directional sun light for VR)
        Light[] allLights = FindObjectsOfType<Light>();

        // Filter to room/environment lights (point and spot)
        var roomLights = new System.Collections.Generic.List<Light>();
        foreach (var light in allLights)
        {
            if (light.type == LightType.Point || light.type == LightType.Spot)
            {
                roomLights.Add(light);
            }
        }

        _sceneLights = roomLights.ToArray();
        _originalLightIntensities = new float[_sceneLights.Length];

        for (int i = 0; i < _sceneLights.Length; i++)
        {
            _originalLightIntensities[i] = _sceneLights[i].intensity;
        }

        Debug.Log($"[MediaEnvironmentController] Cached {_sceneLights.Length} room lights");
    }

    private void CacheAmbientSettings()
    {
        _skyboxMaterial = RenderSettings.skybox;
        _originalAmbientColor = RenderSettings.ambientLight;
        _originalAmbientIntensity = RenderSettings.ambientIntensity;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Set projection type and update environment accordingly.
    /// </summary>
    public void SetProjectionType(VideoProjectionType projection)
    {
        if (CurrentProjection == projection) return;

        CurrentProjection = projection;
        bool shouldHide = ProjectionDetector.ShouldHideEnvironment(projection);

        UpdateEnvironmentVisibility(!shouldHide, "Projection");

        Debug.Log($"[MediaEnvironmentController] Projection set to {projection}, environment {(shouldHide ? "blocked by Projection" : "allowed by Projection")}");
    }

    /// <summary>
    /// Enable or disable scene lights.
    /// </summary>
    public void SetLightsEnabled(bool enabled, bool updateVisibility = true)
    {
        if (LightsEnabled == enabled) return;
        
        LightsEnabled = enabled;

        // "Light Off" also hides the environment for immersion
        if (updateVisibility)
        {
            UpdateEnvironmentVisibility(enabled, "Lights");
        }

        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        ApplyVisualStateImmediate();
        OnLightsChanged?.Invoke(enabled);

        Debug.Log($"[MediaEnvironmentController] Lights {(enabled ? "enabled" : "disabled")} (Immediate)");
    }

    /// <summary>
    /// Toggle lights.
    /// </summary>
    public void ToggleLights()
    {
        SetLightsEnabled(!LightsEnabled);
    }

    /// <summary>
    /// Update environment visibility based on a specific source/reason.
    /// The environment is only visible if NO system is blocking it.
    /// </summary>
    public void UpdateEnvironmentVisibility(bool visible, string source)
    {
        if (visible)
        {
            _visibilityBlockers.Remove(source);
        }
        else
        {
            _visibilityBlockers.Add(source);
        }

        bool shouldBeVisible = _visibilityBlockers.Count == 0;

        if (EnvironmentVisible != shouldBeVisible)
        {
            EnvironmentVisible = shouldBeVisible;

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            ApplyVisualStateImmediate();
            OnEnvironmentVisibilityChanged?.Invoke(shouldBeVisible);
        }
    }

    /// <summary>
    /// Show environment (for Flat/Mono mode).
    /// </summary>
    public void ShowEnvironment()
    {
        UpdateEnvironmentVisibility(true, "Manual");
    }

    /// <summary>
    /// Hide environment (for immersive 180°/360° mode).
    /// </summary>
    public void HideEnvironment()
    {
        UpdateEnvironmentVisibility(false, "Manual");
    }

    /// <summary>
    /// Reset everything to default (Flat UI, Room environment).
    /// </summary>
    public void Reset(bool preserveUserPrefs = false)
    {
        // Stop ongoing fades
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        // Immediately restore technical state (scale/active root)
        if (_environmentRoot != null)
        {
            _environmentRoot.SetActive(true);
            _environmentRoot.transform.localScale = _originalEnvironmentScale;
            foreach (Transform child in _environmentRoot.transform) child.gameObject.SetActive(true);
        }

        // Clear projection blocker (always reset projection on exit)
        _visibilityBlockers.Remove("Projection");

        if (!preserveUserPrefs)
        {
            _visibilityBlockers.Clear();
            EnvironmentVisible = true;
            LightsEnabled = true;

            // Fire events to sync UI
            OnLightsChanged?.Invoke(true);
            OnEnvironmentVisibilityChanged?.Invoke(true);
        }
        else
        {
            // If preserving, update the logically visible state based on remaining blockers
            EnvironmentVisible = (_visibilityBlockers.Count == 0);
        }

        // Apply whatever state we ended up with
        ApplyVisualStateImmediate();

        CurrentProjection = VideoProjectionType.Flat;

        Debug.Log($"[MediaEnvironmentController] Reset to default state (Preserve: {preserveUserPrefs})");
    }

    /// <summary>
    /// Get current dimming level (0 = lights off, 1 = full brightness).
    /// </summary>
    public float GetLightLevel()
    {
        if (_sceneLights == null || _sceneLights.Length == 0) return 1f;
        if (_originalLightIntensities == null || _originalLightIntensities.Length == 0) return 1f;
        if (_originalLightIntensities[0] <= 0) return 1f;

        return _sceneLights[0].intensity / _originalLightIntensities[0];
    }
    #endregion

    #region Private Methods
    private void ApplyVisualStateImmediate()
    {
        if (!_isInitialized) return;

        bool isVisible = EnvironmentVisible;
        bool lightsOn = LightsEnabled;

        // 1. Environment Visibility (Scale + Children)
        if (_environmentRoot != null)
        {
            _environmentRoot.transform.localScale = isVisible ? _originalEnvironmentScale : Vector3.one * ENVIRONMENT_SCALE_HIDDEN;
            
            foreach (Transform child in _environmentRoot.transform)
            {
                child.gameObject.SetActive(isVisible);
            }
        }

        // 2. Skybox
        RenderSettings.skybox = isVisible ? _skyboxMaterial : null;

        // 3. Ambient Light
        // If invisible: 0
        // If visible but lights off: 10%
        // If visible and lights on: 100%
        float targetAmbient = 0f;
        if (isVisible)
        {
            targetAmbient = lightsOn ? _originalAmbientIntensity : _originalAmbientIntensity * 0.1f;
        }
        RenderSettings.ambientIntensity = targetAmbient;

        // 4. Scene Lights
        // Only ON if both environment is visible AND lights are enabled
        for (int i = 0; i < _sceneLights.Length; i++)
        {
            if (_sceneLights[i] != null)
            {
                float targetIntensity = (isVisible && lightsOn) ? _originalLightIntensities[i] : 0f;
                _sceneLights[i].intensity = targetIntensity;
            }
        }

        Debug.Log($"[MediaEnvironmentController] Applied Visual State: Visible={isVisible}, Lights={lightsOn}, Ambient={targetAmbient}");
    }

    public bool IsSourceBlockingVisibility(string source)
    {
        return _visibilityBlockers.Contains(source);
    }
    #endregion

    #region Unity Lifecycle
    private void OnDestroy()
    {
        // Restore original state
        if (_isInitialized)
        {
            EnvironmentVisible = true;
            LightsEnabled = true;
            ApplyVisualStateImmediate();
        }

        if (_instance == this)
        {
            _instance = null;
        }
    }
    #endregion
}
