using UnityEngine;

/// <summary>
/// 180° dome projection renderer for VR180 video.
/// Renders video on an inside-out hemisphere for immersive viewing.
/// </summary>
public class Dome180Renderer : MonoBehaviour, IProjectionRenderer
{
    #region Constants
    private const string SHADER_NAME = "VRWorkspace/Media/Video180Dome";
    private const string FALLBACK_SHADER = "Unlit/Texture";
    private const int HEMISPHERE_SEGMENTS = 64;
    private const int HEMISPHERE_RINGS = 32;
    private const float DOME_RADIUS = 100f;  // Large radius for immersive view
    #endregion

    #region Properties
    public VideoProjectionType Type => VideoProjectionType.Dome180;
    public bool IsActive => _isActive;
    #endregion

    #region Private Fields
    private bool _isActive = false;
    private bool _isInitialized = false;

    private GameObject _domeObject;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Material _material;

    private Transform _parentTransform;
    private StereoMode _stereoMode = StereoMode.Mono;
    private DisplaySettings _currentSettings = DisplaySettings.Immersive;
    private float _rotationOffset = 0f;
    #endregion

    #region IProjectionRenderer Implementation
    public void Initialize(Transform parent)
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[Dome180Renderer] Already initialized");
            return;
        }

        _parentTransform = parent;
        CreateDomeObject();
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

        if (_domeObject == null) return;

        // Position dome at parent (camera) position
        if (_parentTransform != null)
        {
            _domeObject.transform.position = _parentTransform.position;
        }

        // Apply rotation offset
        _domeObject.transform.rotation = Quaternion.Euler(0, _rotationOffset, 0);

        // Update material settings (distance/scale not applicable for immersive 180)
        if (_material != null)
        {
            _material.SetFloat("_Rotation", settings.RotationOffset.eulerAngles.y);
        }
    }

    public void Show()
    {
        if (_domeObject != null)
        {
            _domeObject.SetActive(true);
        }
        _isActive = true;
    }

    public void Hide()
    {
        if (_domeObject != null)
        {
            _domeObject.SetActive(false);
        }
        _isActive = false;
    }

    public void RecenterView()
    {
        if (Camera.main != null)
        {
            // Get current camera Y rotation and use as offset
            _rotationOffset = Camera.main.transform.eulerAngles.y;
        }
        else
        {
            _rotationOffset = 0f;
        }

        UpdateDisplay(_currentSettings);
    }

    public void Dispose()
    {
        if (_domeObject != null)
        {
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
            Destroy(_domeObject);
            _domeObject = null;
        }

        _isInitialized = false;
        _isActive = false;
    }
    #endregion

    #region Private Methods
    private void CreateDomeObject()
    {
        _domeObject = new GameObject("Dome180Video");
        _domeObject.transform.SetParent(_parentTransform, false);
        _domeObject.layer = LayerMask.NameToLayer("VirtualObjects");

        _meshFilter = _domeObject.AddComponent<MeshFilter>();
        _meshRenderer = _domeObject.AddComponent<MeshRenderer>();

        // Create material
        Shader shader = Shader.Find(SHADER_NAME);
        if (shader == null)
        {
            Debug.LogWarning($"[Dome180Renderer] Shader '{SHADER_NAME}' not found, using fallback");
            shader = Shader.Find(FALLBACK_SHADER);
        }

        _material = new Material(shader);
        _material.SetFloat("_Brightness", 1);
        _material.SetFloat("_Contrast", 1);
        _material.SetFloat("_Saturation", 1);
        _material.SetFloat("_StereoMode", 0);
        _material.SetFloat("_UseNV12", 0);
        _material.SetFloat("_FOV", 180);
        _material.SetFloat("_Rotation", 0);

        _meshRenderer.material = _material;
        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;

        // Generate hemisphere mesh
        GenerateHemisphereMesh();
    }

    /// <summary>
    /// Generate a hemisphere mesh for 180° video.
    /// The mesh is rendered inside-out (viewer is at center).
    /// </summary>
    private void GenerateHemisphereMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Dome180Mesh";

        int segments = HEMISPHERE_SEGMENTS;  // Horizontal segments
        int rings = HEMISPHERE_RINGS;        // Vertical rings
        float radius = DOME_RADIUS;

        // +1 for closing the mesh properly
        int vertexCount = (segments + 1) * (rings + 1);
        int triangleCount = segments * rings * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[triangleCount];

        // Generate vertices
        int v = 0;
        for (int ring = 0; ring <= rings; ring++)
        {
            // Vertical angle: 0 (bottom) to PI/2 (top)
            float phi = (float)ring / rings * Mathf.PI * 0.5f;
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);

            for (int seg = 0; seg <= segments; seg++)
            {
                // Horizontal angle: -PI/2 (left) to PI/2 (right) for 180°
                float theta = ((float)seg / segments - 0.5f) * Mathf.PI;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                // Position on hemisphere (z forward)
                vertices[v] = new Vector3(
                    radius * cosPhi * sinTheta,  // X: left-right
                    radius * sinPhi,              // Y: up
                    radius * cosPhi * cosTheta    // Z: forward
                );

                // UV: u = horizontal (0-1), v = vertical (0-1)
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
    /// Set the field of view (for non-standard 180 content).
    /// </summary>
    public void SetFieldOfView(float fov)
    {
        if (_material != null)
        {
            _material.SetFloat("_FOV", Mathf.Clamp(fov, 90f, 220f));
        }
    }

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
    #endregion

    #region Unity Lifecycle
    private void OnDestroy()
    {
        Dispose();
    }
    #endregion
}
