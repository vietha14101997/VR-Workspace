using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum ViewMode { VirtualSpace, RealWorld }

public class ModeController : MonoBehaviour
{
    #region Events
    /// <summary>
    /// Fired when mode transition starts. Parameters: (fromMode, toMode)
    /// </summary>
    public static event Action<ViewMode, ViewMode> OnModeTransitionStarted;

    /// <summary>
    /// Fired during transition with progress value 0-1.
    /// </summary>
    public static event Action<float> OnModeTransitionProgress;

    /// <summary>
    /// Fired when mode transition completes. Parameter: newMode
    /// </summary>
    public static event Action<ViewMode> OnModeChanged;
    #endregion

    [Header("Camera References")]
    public Camera backgroundReal;     // BackgroundCamera_Real
    public Camera backgroundVirtual;  // BackgroundCamera_Virtual
    public CameraPassthrough cameraPassthrough; // Script trên PassthroughQuad
    public GameObject virtualEnvironment; // Virtual environment objects (CubeRoom, etc.)

    [Header("Transition Settings")]
    public float transitionDuration = 0.5f;
    [Tooltip("Fade the virtual environment materials during transition")]
    public bool fadeEnvironmentMaterials = true;
    [Tooltip("Use crossfade effect (blend both cameras during transition)")]
    public bool useCrossfade = true;

    [Header("Advanced Transition")]
    [Tooltip("Custom animation curve for transition easing")]
    public AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private bool isTransitioning;
    private Coroutine transitionCoroutine;

    // Cached renderers and their materials for fading
    private List<Renderer> environmentRenderers = new List<Renderer>();
    private Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    private Dictionary<Renderer, Material[]> instanceMaterials = new Dictionary<Renderer, Material[]>();

    public ViewMode mode = ViewMode.VirtualSpace;
    private ViewMode previousMode;

    /// <summary>
    /// Returns true if a mode transition is currently in progress.
    /// </summary>
    public bool IsTransitioning => isTransitioning;

    void Start()
    {
        // Validate required references
        if (backgroundReal == null || backgroundVirtual == null)
        {
            Debug.LogError("Background cameras not set in ModeController!");
            return;
        }

        if (cameraPassthrough == null)
        {
            Debug.LogError("CameraPassthrough reference not set in ModeController!");
            return;
        }

        previousMode = mode;

        // Cache environment renderers for fading
        CacheEnvironmentRenderers();

        // Apply initial state without transition
        ApplyImmediate();
    }

    void OnDestroy()
    {
        // Cleanup cached material instances
        foreach (var kvp in instanceMaterials)
        {
            if (kvp.Value != null)
            {
                foreach (var mat in kvp.Value)
                {
                    if (mat != null) Destroy(mat);
                }
            }
        }
        instanceMaterials.Clear();
    }

    void CacheEnvironmentRenderers()
    {
        environmentRenderers.Clear();
        originalMaterials.Clear();
        instanceMaterials.Clear();

        if (virtualEnvironment == null) return;

        // Get all renderers in virtual environment
        var renderers = virtualEnvironment.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            environmentRenderers.Add(renderer);
            // Store original shared materials
            originalMaterials[renderer] = renderer.sharedMaterials;
            // Create material instances once (avoid per-frame allocation)
            instanceMaterials[renderer] = renderer.materials;
        }
    }

    public void ToggleMode()
    {
        mode = (mode == ViewMode.VirtualSpace) ? ViewMode.RealWorld : ViewMode.VirtualSpace;
        Apply();
    }

    /// <summary>
    /// Set mode directly (used by VRTaskbar passthrough button).
    /// </summary>
    public void SetMode(ViewMode newMode)
    {
        if (mode != newMode)
        {
            mode = newMode;
            Apply();
        }
    }

    /// <summary>
    /// Apply mode immediately without transition (used for initial state).
    /// </summary>
    void ApplyImmediate()
    {
        bool isRealWorld = (mode == ViewMode.RealWorld);

        backgroundReal.enabled = isRealWorld;
        cameraPassthrough.enabled = isRealWorld;
        backgroundVirtual.enabled = !isRealWorld;
        if (virtualEnvironment) virtualEnvironment.SetActive(!isRealWorld);

        // Reset alphas
        SetEnvironmentAlpha(1f);
        if (cameraPassthrough != null) cameraPassthrough.SetAlpha(1f);

        // Notify listeners
        OnModeChanged?.Invoke(mode);
    }

    IEnumerator TransitionRoutine(bool toRealWorld)
    {
        if (isTransitioning)
        {
            Debug.LogWarning("[ModeController] Transition already in progress, skipping");
            yield break;
        }
        isTransitioning = true;

        ViewMode fromMode = toRealWorld ? ViewMode.VirtualSpace : ViewMode.RealWorld;
        ViewMode toMode = toRealWorld ? ViewMode.RealWorld : ViewMode.VirtualSpace;

        Debug.Log($"[ModeController] Starting transition: {fromMode} → {toMode}, useCrossfade={useCrossfade}, duration={transitionDuration}s");

        // Notify transition started
        OnModeTransitionStarted?.Invoke(fromMode, toMode);

        if (useCrossfade)
        {
            yield return StartCoroutine(CrossfadeTransition(toRealWorld));
        }
        else
        {
            yield return StartCoroutine(SequentialTransition(toRealWorld));
        }

        isTransitioning = false;

        Debug.Log($"[ModeController] Transition completed: now in {mode}");

        // Notify transition completed
        OnModeChanged?.Invoke(mode);
    }

    /// <summary>
    /// Crossfade transition - both systems visible during blend.
    /// </summary>
    IEnumerator CrossfadeTransition(bool toRealWorld)
    {
        if (toRealWorld)
        {
            // ========== VIRTUAL → REAL (Crossfade) ==========
            // Enable both systems
            backgroundReal.enabled = true;
            cameraPassthrough.enabled = true;

            // Start passthrough at 0 alpha
            cameraPassthrough.SetAlpha(0f);

            // Fade: passthrough alpha 0→1, virtual environment alpha 1→0
            float elapsed = 0f;
            while (elapsed < transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / transitionDuration);
                t = transitionCurve.Evaluate(t);

                // Report progress
                OnModeTransitionProgress?.Invoke(t);

                cameraPassthrough.SetAlpha(t);
                if (fadeEnvironmentMaterials) SetEnvironmentAlpha(1f - t);

                yield return null;
            }

            // Finalize
            cameraPassthrough.SetAlpha(1f);
            SetEnvironmentAlpha(1f); // Reset for next time

            // Disable virtual system
            backgroundVirtual.enabled = false;
            if (virtualEnvironment) virtualEnvironment.SetActive(false);
        }
        else
        {
            // ========== REAL → VIRTUAL (Crossfade) ==========
            // Enable virtual system first
            if (fadeEnvironmentMaterials) SetEnvironmentAlpha(0f);
            backgroundVirtual.enabled = true;
            if (virtualEnvironment) virtualEnvironment.SetActive(true);

            // Fade: passthrough alpha 1→0, virtual environment alpha 0→1
            float elapsed = 0f;
            while (elapsed < transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / transitionDuration);
                t = transitionCurve.Evaluate(t);

                // Report progress
                OnModeTransitionProgress?.Invoke(t);

                cameraPassthrough.SetAlpha(1f - t);
                if (fadeEnvironmentMaterials) SetEnvironmentAlpha(t);

                yield return null;
            }

            // Finalize
            cameraPassthrough.SetAlpha(1f); // Reset for next time
            SetEnvironmentAlpha(1f);

            // Disable real system
            backgroundReal.enabled = false;
            cameraPassthrough.enabled = false;
        }
    }

    /// <summary>
    /// Sequential transition - fade out then fade in (original behavior).
    /// </summary>
    IEnumerator SequentialTransition(bool toRealWorld)
    {
        float halfDuration = transitionDuration * 0.5f;

        if (toRealWorld)
        {
            // ========== VIRTUAL → REAL ==========
            // Phase 1: Fade out virtual environment
            if (fadeEnvironmentMaterials && virtualEnvironment != null)
            {
                yield return StartCoroutine(FadeEnvironment(1f, 0f, halfDuration, 0f, 0.5f));
            }

            // Phase 2: Switch cameras
            backgroundReal.enabled = true;
            cameraPassthrough.enabled = true;
            yield return new WaitForEndOfFrame();

            backgroundVirtual.enabled = false;
            if (virtualEnvironment) virtualEnvironment.SetActive(false);

            // Reset environment alpha for next transition
            SetEnvironmentAlpha(1f);

            // Report second half progress
            OnModeTransitionProgress?.Invoke(1f);
        }
        else
        {
            // ========== REAL → VIRTUAL ==========
            // Phase 1: Prepare virtual environment (hidden, alpha = 0)
            if (fadeEnvironmentMaterials)
            {
                SetEnvironmentAlpha(0f);
            }

            // Enable virtual environment
            backgroundVirtual.enabled = true;
            if (virtualEnvironment) virtualEnvironment.SetActive(true);
            yield return new WaitForEndOfFrame();

            // Disable real cameras
            backgroundReal.enabled = false;
            cameraPassthrough.enabled = false;

            // Report first half progress
            OnModeTransitionProgress?.Invoke(0.5f);

            // Phase 2: Fade in virtual environment
            if (fadeEnvironmentMaterials && virtualEnvironment != null)
            {
                yield return StartCoroutine(FadeEnvironment(0f, 1f, halfDuration, 0.5f, 1f));
            }
        }
    }

    /// <summary>
    /// Fade all environment renderers from startAlpha to endAlpha.
    /// </summary>
    IEnumerator FadeEnvironment(float startAlpha, float endAlpha, float duration,
        float progressStart = 0f, float progressEnd = 1f)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = transitionCurve.Evaluate(t);

            float alpha = Mathf.Lerp(startAlpha, endAlpha, t);
            SetEnvironmentAlpha(alpha);

            // Report progress
            float progress = Mathf.Lerp(progressStart, progressEnd, t);
            OnModeTransitionProgress?.Invoke(progress);

            yield return null;
        }

        SetEnvironmentAlpha(endAlpha);
    }

    /// <summary>
    /// Set alpha for all environment renderers using cached material instances.
    /// </summary>
    void SetEnvironmentAlpha(float alpha)
    {
        foreach (var renderer in environmentRenderers)
        {
            if (renderer == null) continue;

            // Use cached material instances instead of creating new ones
            if (!instanceMaterials.TryGetValue(renderer, out var mats)) continue;

            foreach (var mat in mats)
            {
                if (mat == null) continue;

                // Try different alpha property names
                if (mat.HasProperty("_Color"))
                {
                    Color color = mat.color;
                    color.a = alpha;
                    mat.color = color;
                }
                else if (mat.HasProperty("_BaseColor"))
                {
                    Color color = mat.GetColor("_BaseColor");
                    color.a = alpha;
                    mat.SetColor("_BaseColor", color);
                }
                else if (mat.HasProperty("_TintColor"))
                {
                    Color color = mat.GetColor("_TintColor");
                    color.a = alpha;
                    mat.SetColor("_TintColor", color);
                }

                // Enable transparency if fading out
                if (alpha < 1f)
                {
                    SetMaterialTransparent(mat, true);
                }
                else
                {
                    SetMaterialTransparent(mat, false);
                }
            }
        }
    }

    /// <summary>
    /// Configure material for transparent or opaque rendering.
    /// </summary>
    void SetMaterialTransparent(Material mat, bool transparent)
    {
        if (transparent)
        {
            mat.SetFloat("_Mode", 3); // Transparent mode for Standard shader
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }
        else
        {
            mat.SetFloat("_Mode", 0); // Opaque mode
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = -1;
        }
    }

    void Apply()
    {
        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);

        bool toRealWorld = (mode == ViewMode.RealWorld);
        transitionCoroutine = StartCoroutine(TransitionRoutine(toRealWorld));
    }
}
