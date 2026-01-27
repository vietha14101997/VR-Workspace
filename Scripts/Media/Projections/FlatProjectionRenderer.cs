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

    private GameObject _screenObject;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Material _material;

    private Transform _parentTransform;
    private Vector2Int _resolution = new Vector2Int(1920, 1080);
    private float _currentCurvature = 0f;
    private StereoMode _stereoMode = StereoMode.Mono;
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
        if (_material == null) return;

        _material.SetTexture("_MainTex", texture);

        // Update resolution for aspect ratio calculations
        if (texture != null)
        {
            _resolution = new Vector2Int(texture.width, texture.height);
            UpdateScreenAspect();
        }
    }

    public void SetTextureNV12(Texture2D yPlane, Texture2D uvPlane)
    {
        if (_material == null) return;

        // For NV12, we need to use a different shader or shader variant
        _material.SetTexture("_YTex", yPlane);
        _material.SetTexture("_UVTex", uvPlane);
        _material.SetFloat("_UseNV12", 1);

        if (yPlane != null)
        {
            _resolution = new Vector2Int(yPlane.width, yPlane.height);
            UpdateScreenAspect();
        }
    }

    public void SetStereoMode(StereoMode mode)
    {
        _stereoMode = mode;
        if (_material != null)
        {
            _material.SetFloat("_StereoMode", (float)mode);
        }
    }

    public void UpdateDisplay(DisplaySettings settings)
    {
        _currentSettings = settings;

        if (_screenObject == null) return;

        // Update position based on distance
        Vector3 forward = _currentSettings.HeadLocked
            ? Vector3.forward
            : _recenterRotation * Vector3.forward;

        Vector3 position = _parentTransform != null
            ? _parentTransform.position + forward * settings.Distance + settings.PositionOffset
            : forward * settings.Distance + settings.PositionOffset;

        _screenObject.transform.position = position;

        // Update rotation
        if (_currentSettings.HeadLocked && Camera.main != null)
        {
            // Face the camera
            _screenObject.transform.rotation = Quaternion.LookRotation(
                _screenObject.transform.position - Camera.main.transform.position
            );
        }
        else
        {
            _screenObject.transform.rotation = _recenterRotation * settings.RotationOffset;
        }

        // Update scale
        UpdateScreenAspect();

        // Update curvature if changed
        if (!Mathf.Approximately(_currentCurvature, settings.Curvature))
        {
            _currentCurvature = settings.Curvature;
            RegenerateMesh(settings.Curvature);
        }

        // Update material properties
        if (_material != null)
        {
            _material.SetFloat("_Curvature", settings.Curvature);
        }
    }

    public void Show()
    {
        if (_screenObject != null)
        {
            _screenObject.SetActive(true);
        }
        _isActive = true;
    }

    public void Hide()
    {
        if (_screenObject != null)
        {
            _screenObject.SetActive(false);
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
        if (_screenObject != null)
        {
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
            Destroy(_screenObject);
            _screenObject = null;
        }

        _isInitialized = false;
        _isActive = false;
    }
    #endregion

    #region Private Methods
    private void CreateScreenObject()
    {
        _screenObject = new GameObject("FlatVideoScreen");
        _screenObject.transform.SetParent(_parentTransform, false);
        _screenObject.layer = LayerMask.NameToLayer("VirtualObjects");

        _meshFilter = _screenObject.AddComponent<MeshFilter>();
        _meshRenderer = _screenObject.AddComponent<MeshRenderer>();

        // Create material
        Shader shader = Shader.Find(SHADER_NAME);
        if (shader == null)
        {
            Debug.LogWarning($"[FlatProjectionRenderer] Shader '{SHADER_NAME}' not found, using fallback");
            shader = Shader.Find(FALLBACK_SHADER);
        }

        _material = new Material(shader);
        _material.SetFloat("_Curvature", 0);
        _material.SetFloat("_Brightness", 1);
        _material.SetFloat("_Contrast", 1);
        _material.SetFloat("_Saturation", 1);
        _material.SetFloat("_StereoMode", 0);
        _material.SetFloat("_UseNV12", 0);

        _meshRenderer.material = _material;
        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;

        // Generate initial flat mesh
        GenerateFlatMesh();
    }

    private void GenerateFlatMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "FlatVideoMesh";

        // Simple quad
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(0.5f, -0.5f, 0),
            new Vector3(0.5f, 0.5f, 0),
            new Vector3(-0.5f, 0.5f, 0)
        };

        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };

        mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        _meshFilter.mesh = mesh;
    }

    private void GenerateCurvedMesh(float curvature)
    {
        if (curvature <= 0.01f)
        {
            GenerateFlatMesh();
            return;
        }

        Mesh mesh = new Mesh();
        mesh.name = "CurvedVideoMesh";

        int segments = CURVED_SEGMENTS;
        int vertexCount = (segments + 1) * 2;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[segments * 6];

        // Curve parameters
        float curveAngle = Mathf.Lerp(0, 120f, curvature); // Max 120 degrees
        float angleRad = curveAngle * Mathf.Deg2Rad;
        float radius = 0.5f / Mathf.Sin(angleRad * 0.5f);
        float centerZ = -radius * Mathf.Cos(angleRad * 0.5f);

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float u = t;

            // Calculate position on arc
            float angle = Mathf.Lerp(-angleRad * 0.5f, angleRad * 0.5f, t);
            float x = radius * Mathf.Sin(angle);
            float z = radius * Mathf.Cos(angle) + centerZ;

            // Bottom vertex
            vertices[i * 2] = new Vector3(x, -0.5f, z);
            uvs[i * 2] = new Vector2(u, 0);

            // Top vertex
            vertices[i * 2 + 1] = new Vector3(x, 0.5f, z);
            uvs[i * 2 + 1] = new Vector2(u, 1);
        }

        // Generate triangles
        for (int i = 0; i < segments; i++)
        {
            int baseIndex = i * 6;
            int vertBase = i * 2;

            triangles[baseIndex] = vertBase;
            triangles[baseIndex + 1] = vertBase + 3;
            triangles[baseIndex + 2] = vertBase + 1;

            triangles[baseIndex + 3] = vertBase;
            triangles[baseIndex + 4] = vertBase + 2;
            triangles[baseIndex + 5] = vertBase + 3;
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        _meshFilter.mesh = mesh;
    }

    private void RegenerateMesh(float curvature)
    {
        if (curvature <= 0.01f)
        {
            GenerateFlatMesh();
        }
        else
        {
            GenerateCurvedMesh(curvature);
        }
    }

    private void UpdateScreenAspect()
    {
        if (_screenObject == null) return;

        float aspect = _resolution.y > 0 ? (float)_resolution.x / _resolution.y : 16f / 9f;

        // Base height of 1 meter, adjust width by aspect ratio
        float baseHeight = 1.0f * _currentSettings.Scale;
        float baseWidth = baseHeight * aspect;

        _screenObject.transform.localScale = new Vector3(baseWidth, baseHeight, 1f);
    }
    #endregion

    #region Unity Lifecycle
    private void OnDestroy()
    {
        Dispose();
    }
    #endregion
}
