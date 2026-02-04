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
        string[] possibleNames = { "Environment", "VREnvironment", "Room", "Scene", "World" };

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

        if (shouldHide)
        {
            HideEnvironment();
        }
        else
        {
            ShowEnvironment();
        }

        Debug.Log($"[MediaEnvironmentController] Projection set to {projection}, environment {(shouldHide ? "hidden" : "visible")}");
    }

    /// <summary>
    /// Toggle room lights on/off.
    /// Only works in Flat/Mono mode where environment is visible.
    /// </summary>
    public void SetLightsEnabled(bool enabled)
    {
        if (LightsEnabled == enabled) return;
        if (!EnvironmentVisible) return; // No lights control in immersive mode

        LightsEnabled = enabled;

        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
        }

        _fadeCoroutine = StartCoroutine(FadeLights(enabled));
        OnLightsChanged?.Invoke(enabled);

        Debug.Log($"[MediaEnvironmentController] Lights {(enabled ? "enabled" : "disabled")}");
    }

    /// <summary>
    /// Toggle lights.
    /// </summary>
    public void ToggleLights()
    {
        SetLightsEnabled(!LightsEnabled);
    }

    /// <summary>
    /// Show environment (for Flat/Mono mode).
    /// </summary>
    public void ShowEnvironment()
    {
        if (EnvironmentVisible) return;

        EnvironmentVisible = true;

        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
        }

        _fadeCoroutine = StartCoroutine(FadeEnvironment(true));
        OnEnvironmentVisibilityChanged?.Invoke(true);
    }

    /// <summary>
    /// Hide environment (for immersive 180°/360° mode).
    /// </summary>
    public void HideEnvironment()
    {
        if (!EnvironmentVisible) return;

        EnvironmentVisible = false;

        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
        }

        _fadeCoroutine = StartCoroutine(FadeEnvironment(false));
        OnEnvironmentVisibilityChanged?.Invoke(false);
    }

    /// <summary>
    /// Reset to default state (environment visible, lights on).
    /// </summary>
    public void Reset()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
        }

        // Immediately restore
        RestoreEnvironmentImmediate();
        RestoreLightsImmediate();

        EnvironmentVisible = true;
        LightsEnabled = true;
        CurrentProjection = VideoProjectionType.Flat;

        Debug.Log("[MediaEnvironmentController] Reset to default state");
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
    private IEnumerator FadeLights(bool fadeIn)
    {
        float elapsed = 0f;
        float[] startIntensities = new float[_sceneLights.Length];
        float startAmbient = RenderSettings.ambientIntensity;

        // Cache start values
        for (int i = 0; i < _sceneLights.Length; i++)
        {
            if (_sceneLights[i] != null)
            {
                startIntensities[i] = _sceneLights[i].intensity;
            }
        }

        float targetAmbient = fadeIn ? _originalAmbientIntensity : _originalAmbientIntensity * 0.1f;

        while (elapsed < FADE_DURATION)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / FADE_DURATION);
            t = Mathf.SmoothStep(0, 1, t); // Smooth easing

            // Fade each light
            for (int i = 0; i < _sceneLights.Length; i++)
            {
                if (_sceneLights[i] != null)
                {
                    float target = fadeIn ? _originalLightIntensities[i] : 0f;
                    _sceneLights[i].intensity = Mathf.Lerp(startIntensities[i], target, t);
                }
            }

            // Fade ambient
            RenderSettings.ambientIntensity = Mathf.Lerp(startAmbient, targetAmbient, t);

            yield return null;
        }

        // Ensure final values
        for (int i = 0; i < _sceneLights.Length; i++)
        {
            if (_sceneLights[i] != null)
            {
                _sceneLights[i].intensity = fadeIn ? _originalLightIntensities[i] : 0f;
            }
        }
        RenderSettings.ambientIntensity = targetAmbient;

        _fadeCoroutine = null;
    }

    private IEnumerator FadeEnvironment(bool show)
    {
        if (_environmentRoot == null)
        {
            yield break;
        }

        float elapsed = 0f;
        Vector3 startScale = _environmentRoot.transform.localScale;
        Vector3 targetScale = show ? _originalEnvironmentScale : Vector3.one * ENVIRONMENT_SCALE_HIDDEN;

        // Also fade lights when hiding environment
        float[] startLightIntensities = new float[_sceneLights.Length];
        for (int i = 0; i < _sceneLights.Length; i++)
        {
            if (_sceneLights[i] != null)
            {
                startLightIntensities[i] = _sceneLights[i].intensity;
            }
        }

        float startAmbient = RenderSettings.ambientIntensity;
        float targetAmbient = show ? _originalAmbientIntensity : 0f;

        // Handle skybox
        if (!show)
        {
            RenderSettings.skybox = null;
        }

        while (elapsed < FADE_DURATION)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / FADE_DURATION);
            t = Mathf.SmoothStep(0, 1, t);

            // Scale environment
            _environmentRoot.transform.localScale = Vector3.Lerp(startScale, targetScale, t);

            // Fade lights
            for (int i = 0; i < _sceneLights.Length; i++)
            {
                if (_sceneLights[i] != null)
                {
                    float target = show ? (LightsEnabled ? _originalLightIntensities[i] : 0f) : 0f;
                    _sceneLights[i].intensity = Mathf.Lerp(startLightIntensities[i], target, t);
                }
            }

            // Fade ambient
            RenderSettings.ambientIntensity = Mathf.Lerp(startAmbient, targetAmbient, t);

            yield return null;
        }

        // Final state
        _environmentRoot.transform.localScale = targetScale;
        RenderSettings.ambientIntensity = targetAmbient;

        if (!show)
        {
            _environmentRoot.SetActive(false);
        }
        else
        {
            _environmentRoot.SetActive(true);
            RenderSettings.skybox = _skyboxMaterial;
        }

        for (int i = 0; i < _sceneLights.Length; i++)
        {
            if (_sceneLights[i] != null)
            {
                _sceneLights[i].intensity = show ? (LightsEnabled ? _originalLightIntensities[i] : 0f) : 0f;
            }
        }

        _fadeCoroutine = null;
    }

    private void RestoreEnvironmentImmediate()
    {
        if (_environmentRoot != null)
        {
            _environmentRoot.SetActive(true);
            _environmentRoot.transform.localScale = _originalEnvironmentScale;
        }

        RenderSettings.skybox = _skyboxMaterial;
        RenderSettings.ambientIntensity = _originalAmbientIntensity;
        RenderSettings.ambientLight = _originalAmbientColor;
    }

    private void RestoreLightsImmediate()
    {
        for (int i = 0; i < _sceneLights.Length; i++)
        {
            if (_sceneLights[i] != null)
            {
                _sceneLights[i].intensity = _originalLightIntensities[i];
            }
        }
    }
    #endregion

    #region Unity Lifecycle
    private void OnDestroy()
    {
        // Restore original state
        if (_isInitialized)
        {
            RestoreEnvironmentImmediate();
            RestoreLightsImmediate();
        }

        if (_instance == this)
        {
            _instance = null;
        }
    }
    #endregion
}
