using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Audio visualization system for VR Music Player.
/// Creates reactive visual effects based on audio spectrum analysis.
/// </summary>
public class AudioVisualizationSystem : MonoBehaviour
{
    #region Enums
    public enum VisualizationType
    {
        None,
        Bars,           // Classic equalizer bars
        Waveform,       // Audio waveform
        Particles,      // Reactive particle system
        Sphere,         // Ambient reactive sphere
        Combined        // Multiple effects combined
    }
    #endregion

    #region Properties
    public VisualizationType CurrentVisualization { get; private set; } = VisualizationType.Bars;
    public bool IsActive { get; private set; }
    public float Intensity { get; set; } = 1f;
    #endregion

    #region Settings
    [Header("General")]
    [SerializeField] private VisualizationType _defaultVisualization = VisualizationType.Bars;
    
    [Header("Bars Visualization")]
    [SerializeField] private int _barCount = 32;
    [SerializeField] private float _barWidth = 0.05f;
    [SerializeField] private float _barMaxHeight = 2f;
    [SerializeField] private float _barSpacing = 0.02f;
    [SerializeField] private Gradient _barColorGradient;
    
    [Header("Particle Visualization")]
    [SerializeField] private int _particleCount = 500;
    [SerializeField] private float _particleBaseSpeed = 1f;
    [SerializeField] private float _particleBassMultiplier = 5f;
    
    [Header("Sphere Visualization")]
    [SerializeField] private float _sphereBaseScale = 1f;
    [SerializeField] private float _spherePulseAmount = 0.3f;
    [SerializeField] private Gradient _sphereColorGradient;
    
    [Header("Smoothing")]
    [SerializeField] private float _smoothSpeed = 10f;
    [SerializeField] private float _fallSpeed = 5f;
    #endregion

    #region Private Fields
    private AudioPlaybackEngine _audioEngine;
    private Transform _visualizationContainer;
    
    // Bars
    private Transform[] _bars;
    private Material _barMaterial;
    private float[] _barHeights;
    private float[] _targetBarHeights;
    
    // Particles
    private ParticleSystem _particleSystem;
    private ParticleSystem.MainModule _particleMain;
    
    // Sphere
    private GameObject _sphere;
    private Material _sphereMaterial;
    private float _sphereScale;
    
    // Audio data
    private float _bass, _mid, _treble;
    private float _smoothBass, _smoothMid, _smoothTreble;
    private float[] _bandData;
    
    private bool _isInitialized;
    #endregion

    #region Initialization
    /// <summary>
    /// Initialize the visualization system
    /// </summary>
    public void Initialize(AudioPlaybackEngine audioEngine)
    {
        if (_isInitialized) return;

        _audioEngine = audioEngine;
        
        // Create container
        _visualizationContainer = new GameObject("VisualizationContainer").transform;
        _visualizationContainer.SetParent(transform);
        _visualizationContainer.localPosition = Vector3.zero;
        
        // Initialize arrays
        _barHeights = new float[_barCount];
        _targetBarHeights = new float[_barCount];
        _bandData = new float[_barCount];
        
        // Setup default color gradient if not set
        if (_barColorGradient == null)
        {
            _barColorGradient = CreateDefaultGradient();
        }
        if (_sphereColorGradient == null)
        {
            _sphereColorGradient = CreateDefaultGradient();
        }
        
        // Create visualizations (hidden by default)
        CreateBarsVisualization();
        CreateParticleVisualization();
        CreateSphereVisualization();
        
        // Set default
        SetVisualizationType(_defaultVisualization);
        
        _isInitialized = true;
        Debug.Log("[AudioVisualizationSystem] Initialized");
    }

    private Gradient CreateDefaultGradient()
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.4f, 0.2f, 1f), 0f),    // Purple
                new GradientColorKey(new Color(0f, 0.8f, 1f), 0.5f),    // Cyan
                new GradientColorKey(new Color(1f, 0.3f, 0.5f), 1f)     // Pink
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        return gradient;
    }

    private void CreateBarsVisualization()
    {
        GameObject barsContainer = new GameObject("Bars");
        barsContainer.transform.SetParent(_visualizationContainer);
        barsContainer.transform.localPosition = new Vector3(0, -0.5f, 2f);

        _barMaterial = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
        _barMaterial.SetFloat("_Glossiness", 0.8f);

        _bars = new Transform[_barCount];
        float totalWidth = _barCount * (_barWidth + _barSpacing) - _barSpacing;
        float startX = -totalWidth / 2f + _barWidth / 2f;

        for (int i = 0; i < _barCount; i++)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = $"Bar_{i}";
            bar.transform.SetParent(barsContainer.transform);
            
            float x = startX + i * (_barWidth + _barSpacing);
            bar.transform.localPosition = new Vector3(x, 0, 0);
            bar.transform.localScale = new Vector3(_barWidth, 0.01f, _barWidth);
            
            // Each bar gets its own material instance for color
            var renderer = bar.GetComponent<MeshRenderer>();
            renderer.material = new Material(_barMaterial);
            
            // Remove collider
            Destroy(bar.GetComponent<Collider>());
            
            _bars[i] = bar.transform;
        }

        barsContainer.SetActive(false);
    }

    private void CreateParticleVisualization()
    {
        GameObject particleObj = new GameObject("Particles");
        particleObj.transform.SetParent(_visualizationContainer);
        particleObj.transform.localPosition = new Vector3(0, 0, 3f);

        _particleSystem = particleObj.AddComponent<ParticleSystem>();
        _particleMain = _particleSystem.main;
        
        _particleMain.maxParticles = _particleCount;
        _particleMain.startLifetime = 3f;
        _particleMain.startSpeed = _particleBaseSpeed;
        _particleMain.startSize = 0.1f;
        _particleMain.loop = true;
        _particleMain.playOnAwake = false;
        _particleMain.simulationSpace = ParticleSystemSimulationSpace.World;

        // Shape - sphere emission
        var shape = _particleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;

        // Emission
        var emission = _particleSystem.emission;
        emission.rateOverTime = _particleCount / 3f;

        // Color over lifetime
        var colorOverLifetime = _particleSystem.colorOverLifetime;
        colorOverLifetime.enabled = true;
        
        var colorGradient = new ParticleSystem.MinMaxGradient(_barColorGradient);
        colorOverLifetime.color = colorGradient;

        // Size over lifetime
        var sizeOverLifetime = _particleSystem.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0, 0.5f),
            new Keyframe(0.3f, 1f),
            new Keyframe(1, 0f)
        ));

        // Renderer settings
        var renderer = particleObj.GetComponent<ParticleSystemRenderer>();
        renderer.material = new Material(Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Legacy Shaders/Particles/Additive"));
        
        particleObj.SetActive(false);
    }

    private void CreateSphereVisualization()
    {
        _sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _sphere.name = "VisualizationSphere";
        _sphere.transform.SetParent(_visualizationContainer);
        _sphere.transform.localPosition = new Vector3(0, 1f, 3f);
        _sphere.transform.localScale = Vector3.one * _sphereBaseScale;

        // Remove collider
        Destroy(_sphere.GetComponent<Collider>());

        // Create material with emission
        _sphereMaterial = new Material(Shader.Find("Standard"));
        _sphereMaterial.SetFloat("_Metallic", 0.5f);
        _sphereMaterial.SetFloat("_Glossiness", 0.9f);
        _sphereMaterial.EnableKeyword("_EMISSION");
        _sphereMaterial.SetColor("_EmissionColor", Color.black);
        
        _sphere.GetComponent<MeshRenderer>().material = _sphereMaterial;
        _sphereScale = _sphereBaseScale;

        _sphere.SetActive(false);
    }
    #endregion

    #region Public API
    /// <summary>
    /// Set visualization type
    /// </summary>
    public void SetVisualizationType(VisualizationType type)
    {
        CurrentVisualization = type;
        
        // Hide all
        if (_bars != null && _bars.Length > 0 && _bars[0] != null)
            _bars[0].parent.gameObject.SetActive(false);
        if (_particleSystem != null)
            _particleSystem.gameObject.SetActive(false);
        if (_sphere != null)
            _sphere.SetActive(false);

        // Show selected
        switch (type)
        {
            case VisualizationType.Bars:
                if (_bars != null && _bars.Length > 0 && _bars[0] != null)
                    _bars[0].parent.gameObject.SetActive(true);
                break;
                
            case VisualizationType.Particles:
                if (_particleSystem != null)
                {
                    _particleSystem.gameObject.SetActive(true);
                    _particleSystem.Play();
                }
                break;
                
            case VisualizationType.Sphere:
                if (_sphere != null)
                    _sphere.SetActive(true);
                break;
                
            case VisualizationType.Combined:
                if (_bars != null && _bars.Length > 0 && _bars[0] != null)
                    _bars[0].parent.gameObject.SetActive(true);
                if (_sphere != null)
                    _sphere.SetActive(true);
                break;
        }

        Debug.Log($"[AudioVisualizationSystem] Visualization set to {type}");
    }

    /// <summary>
    /// Cycle to next visualization type
    /// </summary>
    public void NextVisualization()
    {
        var values = Enum.GetValues(typeof(VisualizationType));
        int currentIndex = Array.IndexOf(values, CurrentVisualization);
        int nextIndex = (currentIndex + 1) % values.Length;
        SetVisualizationType((VisualizationType)values.GetValue(nextIndex));
    }

    /// <summary>
    /// Show visualization
    /// </summary>
    public void Show()
    {
        IsActive = true;
        _visualizationContainer?.gameObject.SetActive(true);
        SetVisualizationType(CurrentVisualization);
    }

    /// <summary>
    /// Hide visualization
    /// </summary>
    public void Hide()
    {
        IsActive = false;
        
        if (_particleSystem != null && _particleSystem.isPlaying)
            _particleSystem.Stop();
            
        _visualizationContainer?.gameObject.SetActive(false);
    }

    /// <summary>
    /// Set position of visualization
    /// </summary>
    public void SetPosition(Vector3 position)
    {
        if (_visualizationContainer != null)
        {
            _visualizationContainer.position = position;
        }
    }

    /// <summary>
    /// Set rotation of visualization
    /// </summary>
    public void SetRotation(Quaternion rotation)
    {
        if (_visualizationContainer != null)
        {
            _visualizationContainer.rotation = rotation;
        }
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        if (_visualizationContainer != null)
        {
            Destroy(_visualizationContainer.gameObject);
        }
        
        if (_barMaterial != null) Destroy(_barMaterial);
        if (_sphereMaterial != null) Destroy(_sphereMaterial);
    }
    #endregion

    #region Update
    private void Update()
    {
        if (!IsActive || _audioEngine == null) return;

        // Get frequency bands from audio engine
        _audioEngine.GetFrequencyBands(out _bass, out _mid, out _treble);
        
        // Smooth the values
        _smoothBass = Mathf.Lerp(_smoothBass, _bass * Intensity, Time.deltaTime * _smoothSpeed);
        _smoothMid = Mathf.Lerp(_smoothMid, _mid * Intensity, Time.deltaTime * _smoothSpeed);
        _smoothTreble = Mathf.Lerp(_smoothTreble, _treble * Intensity, Time.deltaTime * _smoothSpeed);

        // Get detailed spectrum data
        UpdateBandData();

        // Update visualizations
        switch (CurrentVisualization)
        {
            case VisualizationType.Bars:
                UpdateBars();
                break;
            case VisualizationType.Particles:
                UpdateParticles();
                break;
            case VisualizationType.Sphere:
                UpdateSphere();
                break;
            case VisualizationType.Combined:
                UpdateBars();
                UpdateSphere();
                break;
        }
    }

    private void UpdateBandData()
    {
        if (_audioEngine == null || _audioEngine.SpectrumData == null) return;

        float[] spectrum = _audioEngine.SpectrumData;
        int samplesPerBand = spectrum.Length / _barCount;

        for (int i = 0; i < _barCount; i++)
        {
            float sum = 0;
            int startIdx = i * samplesPerBand;
            int endIdx = Mathf.Min(startIdx + samplesPerBand, spectrum.Length);

            for (int j = startIdx; j < endIdx; j++)
            {
                sum += spectrum[j];
            }

            _targetBarHeights[i] = (sum / samplesPerBand) * _barMaxHeight * Intensity * 50f;
        }
    }

    private void UpdateBars()
    {
        if (_bars == null) return;

        for (int i = 0; i < _barCount; i++)
        {
            // Smooth height with faster rise, slower fall
            if (_targetBarHeights[i] > _barHeights[i])
            {
                _barHeights[i] = Mathf.Lerp(_barHeights[i], _targetBarHeights[i], Time.deltaTime * _smoothSpeed);
            }
            else
            {
                _barHeights[i] = Mathf.Lerp(_barHeights[i], _targetBarHeights[i], Time.deltaTime * _fallSpeed);
            }

            float height = Mathf.Max(0.01f, _barHeights[i]);
            
            // Update scale and position
            var bar = _bars[i];
            Vector3 pos = bar.localPosition;
            pos.y = height / 2f;
            bar.localPosition = pos;
            
            Vector3 scale = bar.localScale;
            scale.y = height;
            bar.localScale = scale;

            // Update color based on height
            float colorT = Mathf.Clamp01(height / _barMaxHeight);
            Color barColor = _barColorGradient.Evaluate(colorT);
            bar.GetComponent<MeshRenderer>().material.color = barColor;
        }
    }

    private void UpdateParticles()
    {
        if (_particleSystem == null) return;

        // Modify particle speed and size based on bass
        _particleMain.startSpeed = _particleBaseSpeed + _smoothBass * _particleBassMultiplier;
        _particleMain.startSize = 0.05f + _smoothBass * 0.2f;

        // Modify emission rate based on overall intensity
        var emission = _particleSystem.emission;
        float intensity = (_smoothBass + _smoothMid + _smoothTreble) / 3f;
        emission.rateOverTime = _particleCount / 3f * (1f + intensity * 2f);
    }

    private void UpdateSphere()
    {
        if (_sphere == null || _sphereMaterial == null) return;

        // Pulse scale based on bass
        float targetScale = _sphereBaseScale + _smoothBass * _spherePulseAmount;
        _sphereScale = Mathf.Lerp(_sphereScale, targetScale, Time.deltaTime * _smoothSpeed);
        _sphere.transform.localScale = Vector3.one * _sphereScale;

        // Update color and emission based on frequency bands
        float colorT = Mathf.Clamp01((_smoothBass + _smoothMid + _smoothTreble) * 2f);
        Color sphereColor = _sphereColorGradient.Evaluate(colorT);
        _sphereMaterial.color = sphereColor;
        
        // Emission intensity based on overall audio intensity
        float emissionIntensity = (_smoothBass * 2f + _smoothMid + _smoothTreble * 0.5f) * 2f;
        _sphereMaterial.SetColor("_EmissionColor", sphereColor * emissionIntensity);

        // Rotate slowly
        _sphere.transform.Rotate(Vector3.up, Time.deltaTime * 30f * (1f + _smoothMid));
    }
    #endregion

    #region Unity Lifecycle
    private void OnDestroy()
    {
        Dispose();
    }
    #endregion
}
