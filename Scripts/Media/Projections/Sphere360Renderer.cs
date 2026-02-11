using UnityEngine;

/// <summary>
/// 360° sphere projection renderer for equirectangular video.
/// Renders video on an inside-out sphere for full immersive viewing.
/// </summary>
public class Sphere360Renderer : MonoBehaviour, IProjectionRenderer
{
    #region Constants
    private const string SHADER_NAME = "VRWorkspace/Media/Video360Sphere";
    private const string FALLBACK_SHADER = "Unlit/Texture";
    private const int SPHERE_SEGMENTS = 64;
    private const int SPHERE_RINGS = 32;
    private const float SPHERE_RADIUS = 100f;  // Large radius for immersive view
    #endregion

    #region Properties
    public VideoProjectionType Type => VideoProjectionType.Sphere360;
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
    private StereoMode _stereoMode = StereoMode.Mono;
    private DisplaySettings _currentSettings = DisplaySettings.Immersive;
    private float _rotationOffset = 0f;
    private float _tiltOffset = 0f;
    #endregion

    #region IProjectionRenderer Implementation
    public void Initialize(Transform parent)
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[Sphere360Renderer] Already initialized");
            return;
        }

        _parentTransform = parent;
        CreateSphereObject();
        _isInitialized = true;
        Hide();  // Start hidden
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
            // Shader expects: 0=Mono, 1=SBS, 2=OU
            _material.SetFloat("_StereoMode", (float)mode);
        }
    }

    public void UpdateDisplay(DisplaySettings settings)
    {
        _currentSettings = settings;

        if (_sphereObject == null) return;

        // Position sphere at parent (camera) position
        if (_parentTransform != null)
        {
            _sphereObject.transform.position = _parentTransform.position;
        }

        // Apply rotation offset (from recenter)
        _sphereObject.transform.rotation = Quaternion.Euler(_tiltOffset, _rotationOffset, 0);

        // Update material settings
        if (_material != null)
        {
            _material.SetFloat("_Rotation", settings.RotationOffset.eulerAngles.y);
            _material.SetFloat("_Tilt", settings.RotationOffset.eulerAngles.x);
        }
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
        if (Camera.main != null)
        {
            // Get current camera rotation and use as offset
            Vector3 euler = Camera.main.transform.eulerAngles;
            _rotationOffset = euler.y;
            // Don't recenter tilt for 360 videos (keep horizon level)
            _tiltOffset = 0f;
        }
        else
        {
            _rotationOffset = 0f;
            _tiltOffset = 0f;
        }

        UpdateDisplay(_currentSettings);
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

        _isInitialized = false;
        _isActive = false;
    }
    #endregion

    #region Private Methods
    private void CreateSphereObject()
    {
        _sphereObject = new GameObject("Sphere360Video");
        _sphereObject.transform.SetParent(_parentTransform, false);
        _sphereObject.layer = LayerMask.NameToLayer("VirtualObjects");

        _meshFilter = _sphereObject.AddComponent<MeshFilter>();
        _meshRenderer = _sphereObject.AddComponent<MeshRenderer>();

        // Create material
        Shader shader = Shader.Find(SHADER_NAME);
        if (shader == null)
        {
            Debug.LogWarning($"[Sphere360Renderer] Shader '{SHADER_NAME}' not found, using fallback");
            shader = Shader.Find(FALLBACK_SHADER);
        }

        _material = new Material(shader);
        _material.SetFloat("_Brightness", 1);
        _material.SetFloat("_Contrast", 1);
        _material.SetFloat("_Saturation", 1);
        _material.SetFloat("_StereoMode", 0);
        _material.SetFloat("_UseNV12", 0);
        _material.SetFloat("_Rotation", 0);
        _material.SetFloat("_Tilt", 0);

        _meshRenderer.material = _material;
        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;

        // Generate sphere mesh
        GenerateSphereMesh();
    }

    /// <summary>
    /// Generate a sphere mesh for 360° video.
    /// The mesh is rendered inside-out (viewer is at center).
    /// </summary>
    private void GenerateSphereMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Sphere360Mesh";

        int segments = SPHERE_SEGMENTS;  // Horizontal segments
        int rings = SPHERE_RINGS;        // Vertical rings
        float radius = SPHERE_RADIUS;

        int vertexCount = (segments + 1) * (rings + 1);
        int triangleCount = segments * rings * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[triangleCount];

        // Generate vertices
        int v = 0;
        for (int ring = 0; ring <= rings; ring++)
        {
            // Vertical angle: -PI/2 (bottom) to PI/2 (top)
            float phi = ((float)ring / rings - 0.5f) * Mathf.PI;
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);

            for (int seg = 0; seg <= segments; seg++)
            {
                // Horizontal angle: 0 to 2*PI
                float theta = (float)seg / segments * Mathf.PI * 2f;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                // Position on sphere
                vertices[v] = new Vector3(
                    radius * cosPhi * sinTheta,   // X
                    radius * sinPhi,               // Y
                    radius * cosPhi * cosTheta     // Z
                );

                // UV: equirectangular mapping
                // u: 0 at center-back, wrapping around
                // v: 0 at bottom, 1 at top
                uvs[v] = new Vector2(
                    (float)seg / segments,
                    (float)ring / rings
                );

                v++;
            }
        }

        // Generate triangles (inside-out winding for backface rendering)
        int t = 0;
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int current = ring * (segments + 1) + seg;
                int next = current + segments + 1;

                // Triangle 1 (reversed winding for inside-out)
                triangles[t++] = current;
                triangles[t++] = current + 1;
                triangles[t++] = next;

                // Triangle 2 (reversed winding for inside-out)
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

    #region Public Methods
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
    /// Set manual rotation offset.
    /// </summary>
    public void SetRotation(float rotation)
    {
        if (_material != null)
        {
            _material.SetFloat("_Rotation", rotation);
        }
    }

    /// <summary>
    /// Set manual tilt offset.
    /// </summary>
    public void SetTilt(float tilt)
    {
        if (_material != null)
        {
            _material.SetFloat("_Tilt", tilt);
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
