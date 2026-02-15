using UnityEngine;

/// <summary>
/// Unified immersive sphere renderer for 180/360 equirectangular video.
/// Uses a single full inverted sphere with shader-based projection mode switching.
/// Based on the YouTube VR / Google Cardboard approach:
/// - One sphere mesh for all immersive content
/// - Direction-based equirectangular UV sampling in fragment shader
/// - Shader parameter switches between 180 and 360 mode
/// </summary>
public class ImmersiveSphereRenderer : MonoBehaviour, IProjectionRenderer
{
    /// <summary>
    /// Projection mode controlling how equirectangular video maps onto the sphere.
    /// </summary>
    public enum ProjectionMode
    {
        /// <summary>Full 360 equirectangular (video wraps entire sphere)</summary>
        Equirect360 = 0,

        /// <summary>180 equirectangular (video covers front hemisphere only)</summary>
        Equirect180 = 1
    }

    #region Constants
    private const string SHADER_NAME = "VRWorkspace/Media/VideoImmersive";
    private const string FALLBACK_SHADER = "Unlit/Texture";
    private const int SPHERE_SEGMENTS = 128;
    private const int SPHERE_RINGS = 64;
    private const float SPHERE_RADIUS = 100f;
    #endregion

    #region Properties
    public VideoProjectionType Type
    {
        get
        {
            return _projectionMode == ProjectionMode.Equirect180
                ? VideoProjectionType.Dome180
                : VideoProjectionType.Sphere360;
        }
    }

    public bool IsActive => _isActive;
    #endregion

    #region Private Fields
    private bool _isActive = false;
    private bool _isInitialized = false;

    private GameObject _sphereObject;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Material _material;

    private Transform _parentTransform;
    private ProjectionMode _projectionMode = ProjectionMode.Equirect360;
    private StereoMode _stereoMode = StereoMode.Mono;
    private DisplaySettings _currentSettings = DisplaySettings.Immersive;
    private float _shaderRotationOffset = 0f;

    // FOV zoom state
    private float _defaultFOV = 300f;
    private float _currentFOV = 300f;
    private const float MIN_FOV = 180f;
    private const float MAX_FOV = 420f;
    private const float FOV_STEP = 12f;
    #endregion

    #region IProjectionRenderer Implementation
    public void Initialize(Transform parent)
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[ImmersiveSphereRenderer] Already initialized");
            return;
        }

        _parentTransform = parent;
        CreateSphereObject();
        _isInitialized = true;
        Hide();
    }

    public void SetTexture(Texture texture)
    {
        if (_material == null) return;

        _material.SetTexture("_MainTex", texture);
        _material.SetFloat("_UseNV12", 0);
    }

    public void SetTextureNV12(Texture2D yPlane, Texture2D uvPlane)
    {
        if (_material == null) return;

        _material.SetTexture("_YTex", yPlane);
        _material.SetTexture("_UVTex", uvPlane);
        _material.SetFloat("_UseNV12", 1);
    }

    public void SetStereoMode(StereoMode mode)
    {
        _stereoMode = mode;
        if (_material != null)
        {
            _material.SetFloat("_StereoMode", (float)mode);
        }
    }

    public void SetForceMonoscopic(bool force)
    {
        if (_material != null)
        {
            _material.SetFloat("_ForceMono", force ? 1f : 0f);
        }
    }

    public void UpdateDisplay(DisplaySettings settings)
    {
        _currentSettings = settings;

        if (_sphereObject == null) return;

        // Position sphere at parent (camera) position — always centered on viewer
        if (_parentTransform != null)
        {
            _sphereObject.transform.position = _parentTransform.position;
        }

        // No transform rotation — all rotation is shader-based
    }

    public void Show()
    {
        if (_sphereObject != null)
        {
            _sphereObject.SetActive(true);
        }
        _isActive = true;
    }

    public void Hide()
    {
        if (_sphereObject != null)
        {
            _sphereObject.SetActive(false);
        }
        _isActive = false;
    }

    public void RecenterView()
    {
        // Shader-only rotation: capture current camera Y angle as offset
        if (Camera.main != null)
        {
            _shaderRotationOffset = Camera.main.transform.eulerAngles.y;
        }
        else
        {
            _shaderRotationOffset = 0f;
        }

        if (_material != null)
        {
            _material.SetFloat("_Rotation", _shaderRotationOffset);
        }
    }

    public void Dispose()
    {
        if (_sphereObject != null)
        {
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
            Destroy(_sphereObject);
            _sphereObject = null;
        }

        _meshFilter = null;
        _meshRenderer = null;
        _isInitialized = false;
        _isActive = false;
    }
    #endregion

    #region Projection Mode
    /// <summary>
    /// Switch between 180 and 360 equirectangular projection.
    /// Only sets a shader parameter — no mesh regeneration needed.
    /// </summary>
    public void SetProjectionMode(ProjectionMode mode)
    {
        _projectionMode = mode;

        // Set default FOV per projection mode:
        // 180°: FOV=300 for comfortable Cardboard viewing (reduces stereo parallax)
        // 360°: FOV=240 for standard immersive zoom-in
        _defaultFOV = (mode == ProjectionMode.Equirect180) ? 300f : 240f;
        _currentFOV = _defaultFOV;

        if (_material != null)
        {
            _material.SetFloat("_ProjectionMode", (float)mode);
            _material.SetFloat("_FOV", _currentFOV);
        }

        Debug.Log($"[ImmersiveSphereRenderer] Projection mode: {mode}, FOV: {_currentFOV}");
    }
    #endregion

    #region Public Adjustments
    /// <summary>
    /// Set the field of view for 180 mode (for non-standard content).
    /// </summary>
    public void SetFieldOfView(float fov)
    {
        _currentFOV = Mathf.Clamp(fov, MIN_FOV, MAX_FOV);
        if (_material != null)
        {
            _material.SetFloat("_FOV", _currentFOV);
        }
    }

    /// <summary>
    /// Zoom by adjusting FOV. Negative delta = zoom in (decrease FOV), positive = zoom out.
    /// </summary>
    public void ZoomByFOV(float delta)
    {
        SetFieldOfView(_currentFOV + delta);
        Debug.Log($"[ImmersiveSphereRenderer] FOV zoom: {_currentFOV:F0} / {_defaultFOV:F0}");
    }

    /// <summary>
    /// Reset FOV zoom to default for current projection mode.
    /// </summary>
    public void ResetFOVZoom()
    {
        SetFieldOfView(_defaultFOV);
    }

    /// <summary>Current FOV value</summary>
    public float CurrentFOV => _currentFOV;

    /// <summary>Default FOV for current projection mode</summary>
    public float DefaultFOV => _defaultFOV;

    /// <summary>
    /// Set brightness adjustment.
    /// </summary>
    public void SetBrightness(float brightness)
    {
        if (_material != null)
        {
            _material.SetFloat("_Brightness", brightness);
        }
    }

    /// <summary>
    /// Set contrast adjustment.
    /// </summary>
    public void SetContrast(float contrast)
    {
        if (_material != null)
        {
            _material.SetFloat("_Contrast", contrast);
        }
    }

    /// <summary>
    /// Set saturation adjustment.
    /// </summary>
    public void SetSaturation(float saturation)
    {
        if (_material != null)
        {
            _material.SetFloat("_Saturation", saturation);
        }
    }

    /// <summary>
    /// Set manual rotation offset (Y-axis).
    /// </summary>
    public void SetRotation(float rotation)
    {
        _shaderRotationOffset = rotation;
        if (_material != null)
        {
            _material.SetFloat("_Rotation", rotation);
        }
    }

    /// <summary>
    /// Set manual tilt offset (X-axis).
    /// </summary>
    public void SetTilt(float tilt)
    {
        if (_material != null)
        {
            _material.SetFloat("_Tilt", tilt);
        }
    }
    #endregion

    #region Private Methods
    private void CreateSphereObject()
    {
        _sphereObject = new GameObject("ImmersiveSphereVideo");
        _sphereObject.transform.SetParent(_parentTransform, false);

        int layer = LayerMask.NameToLayer("VirtualObjects");
        _sphereObject.layer = layer >= 0 ? layer : 0;

        _meshFilter = _sphereObject.AddComponent<MeshFilter>();
        _meshRenderer = _sphereObject.AddComponent<MeshRenderer>();

        // Create material with unified immersive shader
        Shader shader = Shader.Find(SHADER_NAME);
        if (shader == null)
        {
            Debug.LogWarning($"[ImmersiveSphereRenderer] Shader '{SHADER_NAME}' not found, using fallback");
            shader = Shader.Find(FALLBACK_SHADER);
        }

        _material = new Material(shader);
        _material.SetFloat("_Brightness", 1);
        _material.SetFloat("_Contrast", 1);
        _material.SetFloat("_Saturation", 1);
        _material.SetFloat("_StereoMode", 0);
        _material.SetFloat("_UseNV12", 0);
        _material.SetFloat("_ProjectionMode", (float)_projectionMode);
        _material.SetFloat("_FOV", 300);
        _material.SetFloat("_Rotation", 0);
        _material.SetFloat("_Tilt", 0);
        _material.SetFloat("_FadeSharpness", 8);
        _material.SetVector("_CameraForward", Vector3.forward);

        _meshRenderer.material = _material;
        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;

        GenerateSphereMesh();
    }

    /// <summary>
    /// Generate a full inverted sphere mesh for immersive video.
    /// Uses inside-out winding with Cull Front shader for rendering from inside.
    /// Same mesh is used for both 180 and 360 — the shader handles projection clipping.
    /// </summary>
    private void GenerateSphereMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "ImmersiveSphereMesh";

        int segments = SPHERE_SEGMENTS;
        int rings = SPHERE_RINGS;
        float radius = SPHERE_RADIUS;

        int vertexCount = (segments + 1) * (rings + 1);
        int triangleCount = segments * rings * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[triangleCount];

        // Generate vertices on sphere surface
        int v = 0;
        for (int ring = 0; ring <= rings; ring++)
        {
            // Vertical angle: -PI/2 (south pole) to PI/2 (north pole)
            float phi = ((float)ring / rings - 0.5f) * Mathf.PI;
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);

            for (int seg = 0; seg <= segments; seg++)
            {
                // Horizontal angle: 0 to 2*PI (full circle)
                float theta = (float)seg / segments * Mathf.PI * 2f;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                vertices[v] = new Vector3(
                    radius * cosPhi * sinTheta,   // X
                    radius * sinPhi,               // Y (up)
                    radius * cosPhi * cosTheta     // Z (forward)
                );

                // Equirectangular UV mapping
                uvs[v] = new Vector2(
                    (float)seg / segments,
                    (float)ring / rings
                );

                v++;
            }
        }

        // Generate triangles with standard winding order
        // Combined with shader's Cull Front, this renders inside-out
        int t = 0;
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int current = ring * (segments + 1) + seg;
                int next = current + segments + 1;

                triangles[t++] = current;
                triangles[t++] = current + 1;
                triangles[t++] = next;

                triangles[t++] = current + 1;
                triangles[t++] = next + 1;
                triangles[t++] = next;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        _meshFilter.mesh = mesh;
    }
    #endregion

    #region Unity Lifecycle
    private void Update()
    {
        if (!_isActive || _material == null) return;

        Camera cam = Camera.main;
        if (cam != null)
        {
            _material.SetVector("_CameraForward", cam.transform.forward);
        }
    }

    private void OnDestroy()
    {
        Dispose();
    }
    #endregion
}
