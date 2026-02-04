using UnityEngine;
using System;

/// <summary>
/// Manages image projection rendering for VR.
/// Supports Flat, 180°, 360°, and stereoscopic image display.
/// </summary>
public class VRImageProjectionSystem : MonoBehaviour
{
    #region Events
    public event Action<ImageProjectionType> OnProjectionChanged;
    #endregion

    #region Properties
    public ImageProjectionType CurrentProjection { get; private set; } = ImageProjectionType.Flat;
    public bool IsVisible { get; private set; }
    #endregion

    #region Settings
    [Header("Flat Screen Settings")]
    [SerializeField] private float _defaultDistance = 2f;
    [SerializeField] private float _defaultScale = 1.5f;
    [SerializeField] private float _curvature = 0.1f;
    
    [Header("Rendering")]
    [SerializeField] private Material _flatMaterial;
    [SerializeField] private Material _domeMaterial;
    [SerializeField] private Material _sphereMaterial;
    [SerializeField] private Material _stereoMaterial;
    #endregion

    #region Private Fields
    private Transform _cameraRig;
    private GameObject _projectionObject;
    private MeshRenderer _renderer;
    private Material _currentMaterial;
    
    // Flat screen mesh
    private GameObject _flatScreen;
    private MeshFilter _flatMeshFilter;
    
    // 180° dome
    private GameObject _dome180;
    
    // 360° sphere
    private GameObject _sphere360;
    
    private float _currentAlpha = 1f;
    private Vector2 _panOffset = Vector2.zero;
    private float _scale = 1f;
    private float _distance;
    private bool _isInitialized;
    #endregion

    #region Initialization
    /// <summary>
    /// Initialize the projection system
    /// </summary>
    public void Initialize(Transform cameraRig = null)
    {
        if (_isInitialized) return;

        _cameraRig = cameraRig ?? Camera.main?.transform;
        _distance = _defaultDistance;
        _scale = _defaultScale;

        CreateProjectionObjects();
        CreateMaterials();
        
        // Start hidden
        HideAllProjections();
        
        _isInitialized = true;
        Debug.Log("[VRImageProjectionSystem] Initialized");
    }

    private void CreateProjectionObjects()
    {
        // Container
        _projectionObject = new GameObject("ImageProjection");
        _projectionObject.transform.SetParent(transform);

        // Flat screen (curved quad)
        _flatScreen = CreateFlatScreen();
        _flatScreen.transform.SetParent(_projectionObject.transform);

        // 180° dome
        _dome180 = CreateDome180();
        _dome180.transform.SetParent(_projectionObject.transform);

        // 360° sphere
        _sphere360 = CreateSphere360();
        _sphere360.transform.SetParent(_projectionObject.transform);
    }

    private GameObject CreateFlatScreen()
    {
        GameObject screen = new GameObject("FlatScreen");
        
        _flatMeshFilter = screen.AddComponent<MeshFilter>();
        _flatMeshFilter.mesh = CreateCurvedQuadMesh(_curvature);
        
        var renderer = screen.AddComponent<MeshRenderer>();
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        
        return screen;
    }

    private GameObject CreateDome180()
    {
        GameObject dome = new GameObject("Dome180");
        
        var filter = dome.AddComponent<MeshFilter>();
        filter.mesh = CreateDomeMesh(180f, 64, 32);
        
        var renderer = dome.AddComponent<MeshRenderer>();
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        
        // Scale for immersive experience
        dome.transform.localScale = Vector3.one * 50f;
        
        return dome;
    }

    private GameObject CreateSphere360()
    {
        GameObject sphere = new GameObject("Sphere360");
        
        var filter = sphere.AddComponent<MeshFilter>();
        filter.mesh = CreateSphereMesh(64, 32, true); // inside-out
        
        var renderer = sphere.AddComponent<MeshRenderer>();
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        
        // Scale for immersive experience
        sphere.transform.localScale = Vector3.one * 50f;
        
        return sphere;
    }

    private void CreateMaterials()
    {
        // Create materials if not assigned
        if (_flatMaterial == null)
        {
            _flatMaterial = new Material(Shader.Find("Unlit/Texture"));
            _flatMaterial.name = "ImageFlat";
        }

        if (_domeMaterial == null)
        {
            // Try to find the video dome shader, fallback to unlit
            var shader = Shader.Find("Custom/Video180Dome") ?? Shader.Find("Skybox/Panoramic") ?? Shader.Find("Unlit/Texture");
            _domeMaterial = new Material(shader);
            _domeMaterial.name = "ImageDome";
        }

        if (_sphereMaterial == null)
        {
            var shader = Shader.Find("Custom/Video360Sphere") ?? Shader.Find("Skybox/Panoramic") ?? Shader.Find("Unlit/Texture");
            _sphereMaterial = new Material(shader);
            _sphereMaterial.name = "ImageSphere";
        }

        if (_stereoMaterial == null)
        {
            var shader = Shader.Find("Custom/VideoStereoscopic") ?? Shader.Find("Unlit/Texture");
            _stereoMaterial = new Material(shader);
            _stereoMaterial.name = "ImageStereo";
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// Set projection type
    /// </summary>
    public void SetProjectionType(ImageProjectionType projection)
    {
        if (CurrentProjection == projection) return;

        CurrentProjection = projection;
        HideAllProjections();
        
        switch (projection)
        {
            case ImageProjectionType.Flat:
            case ImageProjectionType.FlatStereoSBS:
            case ImageProjectionType.FlatStereoOU:
                _flatScreen.SetActive(true);
                SetupFlatScreen(projection);
                break;
                
            case ImageProjectionType.Dome180:
            case ImageProjectionType.Dome180Stereo:
                _dome180.SetActive(true);
                SetupDome(projection);
                break;
                
            case ImageProjectionType.Sphere360:
            case ImageProjectionType.Sphere360Stereo:
                _sphere360.SetActive(true);
                SetupSphere(projection);
                break;
        }

        UpdatePosition();
        OnProjectionChanged?.Invoke(projection);
        Debug.Log($"[VRImageProjectionSystem] Projection set to {projection}");
    }

    /// <summary>
    /// Set image texture
    /// </summary>
    public void SetTexture(Texture2D texture)
    {
        if (_currentMaterial != null && texture != null)
        {
            _currentMaterial.mainTexture = texture;
            
            // Update aspect ratio for flat screen
            if (CurrentProjection == ImageProjectionType.Flat ||
                CurrentProjection == ImageProjectionType.FlatStereoSBS ||
                CurrentProjection == ImageProjectionType.FlatStereoOU)
            {
                UpdateFlatScreenAspect(texture.width, texture.height);
            }
        }
    }

    /// <summary>
    /// Set scale (zoom)
    /// </summary>
    public void SetScale(float scale)
    {
        _scale = Mathf.Clamp(scale, 0.5f, 4f);
        UpdatePosition();
    }

    /// <summary>
    /// Set distance from camera (flat projection only)
    /// </summary>
    public void SetDistance(float distance)
    {
        _distance = Mathf.Clamp(distance, 1f, 10f);
        UpdatePosition();
    }

    /// <summary>
    /// Set pan offset (for zoomed in viewing)
    /// </summary>
    public void SetPanOffset(Vector2 offset)
    {
        _panOffset = offset;
        UpdatePosition();
    }

    /// <summary>
    /// Set alpha for fade transitions
    /// </summary>
    public void SetAlpha(float alpha)
    {
        _currentAlpha = Mathf.Clamp01(alpha);
        
        if (_currentMaterial != null)
        {
            Color color = _currentMaterial.color;
            color.a = _currentAlpha;
            _currentMaterial.color = color;
        }
    }

    /// <summary>
    /// Show projection
    /// </summary>
    public void Show()
    {
        IsVisible = true;
        _projectionObject?.SetActive(true);
    }

    /// <summary>
    /// Hide projection
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
        _projectionObject?.SetActive(false);
    }

    /// <summary>
    /// Recenter view
    /// </summary>
    public void RecenterView()
    {
        if (_cameraRig != null && _projectionObject != null)
        {
            // For 180/360, rotate to face camera direction
            if (CurrentProjection != ImageProjectionType.Flat)
            {
                Vector3 forward = _cameraRig.forward;
                forward.y = 0;
                if (forward.sqrMagnitude > 0.001f)
                {
                    _projectionObject.transform.rotation = Quaternion.LookRotation(forward);
                }
            }
        }
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        if (_flatScreen != null) Destroy(_flatScreen);
        if (_dome180 != null) Destroy(_dome180);
        if (_sphere360 != null) Destroy(_sphere360);
        if (_projectionObject != null) Destroy(_projectionObject);
        
        // Destroy created materials
        if (_flatMaterial != null) Destroy(_flatMaterial);
        if (_domeMaterial != null) Destroy(_domeMaterial);
        if (_sphereMaterial != null) Destroy(_sphereMaterial);
        if (_stereoMaterial != null) Destroy(_stereoMaterial);
    }
    #endregion

    #region Private Methods
    private void HideAllProjections()
    {
        _flatScreen?.SetActive(false);
        _dome180?.SetActive(false);
        _sphere360?.SetActive(false);
    }

    private void SetupFlatScreen(ImageProjectionType projection)
    {
        var renderer = _flatScreen.GetComponent<MeshRenderer>();
        
        if (projection == ImageProjectionType.FlatStereoSBS || projection == ImageProjectionType.FlatStereoOU)
        {
            _currentMaterial = new Material(_stereoMaterial);
            _currentMaterial.SetFloat("_Layout", projection == ImageProjectionType.FlatStereoSBS ? 0 : 1);
        }
        else
        {
            _currentMaterial = new Material(_flatMaterial);
        }
        
        renderer.material = _currentMaterial;
    }

    private void SetupDome(ImageProjectionType projection)
    {
        var renderer = _dome180.GetComponent<MeshRenderer>();
        
        if (projection == ImageProjectionType.Dome180Stereo)
        {
            _currentMaterial = new Material(_stereoMaterial);
        }
        else
        {
            _currentMaterial = new Material(_domeMaterial);
        }
        
        renderer.material = _currentMaterial;
    }

    private void SetupSphere(ImageProjectionType projection)
    {
        var renderer = _sphere360.GetComponent<MeshRenderer>();
        
        if (projection == ImageProjectionType.Sphere360Stereo)
        {
            _currentMaterial = new Material(_stereoMaterial);
        }
        else
        {
            _currentMaterial = new Material(_sphereMaterial);
        }
        
        renderer.material = _currentMaterial;
    }

    private void UpdatePosition()
    {
        if (_cameraRig == null || _projectionObject == null) return;

        // For flat screen, position in front of camera
        if (CurrentProjection == ImageProjectionType.Flat ||
            CurrentProjection == ImageProjectionType.FlatStereoSBS ||
            CurrentProjection == ImageProjectionType.FlatStereoOU)
        {
            Vector3 cameraPos = _cameraRig.position;
            Vector3 forward = _cameraRig.forward;
            forward.y = 0;
            forward.Normalize();

            Vector3 right = _cameraRig.right;
            Vector3 up = Vector3.up;

            _flatScreen.transform.position = cameraPos + forward * _distance 
                + right * _panOffset.x * _scale 
                + up * _panOffset.y * _scale;
            _flatScreen.transform.rotation = Quaternion.LookRotation(-forward);
            _flatScreen.transform.localScale = Vector3.one * _scale * _defaultScale;
        }
        else
        {
            // For 180/360, center on camera
            _projectionObject.transform.position = _cameraRig.position;
        }
    }

    private void UpdateFlatScreenAspect(int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        float aspect = (float)width / height;
        
        // Adjust scale to maintain aspect ratio
        Vector3 scale = _flatScreen.transform.localScale;
        scale.x = scale.y * aspect;
        _flatScreen.transform.localScale = scale;
    }

    private Mesh CreateCurvedQuadMesh(float curvature)
    {
        int segments = 32;
        Mesh mesh = new Mesh();
        mesh.name = "CurvedQuad";

        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        Vector2[] uvs = new Vector2[(segments + 1) * 2];
        int[] triangles = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = (t - 0.5f) * curvature * Mathf.PI;
            float x = Mathf.Sin(angle);
            float z = Mathf.Cos(angle) - 1f;

            vertices[i] = new Vector3(x, -0.5f, z);
            vertices[i + segments + 1] = new Vector3(x, 0.5f, z);
            
            uvs[i] = new Vector2(t, 0);
            uvs[i + segments + 1] = new Vector2(t, 1);
        }

        for (int i = 0; i < segments; i++)
        {
            int ti = i * 6;
            triangles[ti] = i;
            triangles[ti + 1] = i + segments + 1;
            triangles[ti + 2] = i + 1;
            triangles[ti + 3] = i + 1;
            triangles[ti + 4] = i + segments + 1;
            triangles[ti + 5] = i + segments + 2;
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        return mesh;
    }

    private Mesh CreateDomeMesh(float fov, int longitudeSegments, int latitudeSegments)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Dome180";

        // Create half-sphere mesh
        int vertCount = (longitudeSegments + 1) * (latitudeSegments + 1);
        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];

        float fovRad = fov * Mathf.Deg2Rad;
        int idx = 0;

        for (int lat = 0; lat <= latitudeSegments; lat++)
        {
            float v = (float)lat / latitudeSegments;
            float theta = v * Mathf.PI * 0.5f; // 0 to 90 degrees

            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                float u = (float)lon / longitudeSegments;
                float phi = (u - 0.5f) * fovRad;

                float x = Mathf.Sin(phi) * Mathf.Cos(theta);
                float y = Mathf.Sin(theta);
                float z = Mathf.Cos(phi) * Mathf.Cos(theta);

                // Inside-out (normals pointing inward)
                vertices[idx] = new Vector3(-x, y, z);
                uvs[idx] = new Vector2(u, v);
                idx++;
            }
        }

        int[] triangles = new int[longitudeSegments * latitudeSegments * 6];
        int triIdx = 0;

        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int current = lat * (longitudeSegments + 1) + lon;
                int next = current + longitudeSegments + 1;

                triangles[triIdx++] = current;
                triangles[triIdx++] = current + 1;
                triangles[triIdx++] = next;

                triangles[triIdx++] = next;
                triangles[triIdx++] = current + 1;
                triangles[triIdx++] = next + 1;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        return mesh;
    }

    private Mesh CreateSphereMesh(int longitudeSegments, int latitudeSegments, bool insideOut)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Sphere360";

        int vertCount = (longitudeSegments + 1) * (latitudeSegments + 1);
        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];

        int idx = 0;
        for (int lat = 0; lat <= latitudeSegments; lat++)
        {
            float v = (float)lat / latitudeSegments;
            float theta = v * Mathf.PI;

            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                float u = (float)lon / longitudeSegments;
                float phi = u * 2f * Mathf.PI;

                float x = Mathf.Sin(theta) * Mathf.Cos(phi);
                float y = Mathf.Cos(theta);
                float z = Mathf.Sin(theta) * Mathf.Sin(phi);

                if (insideOut)
                {
                    vertices[idx] = new Vector3(-x, y, z);
                    uvs[idx] = new Vector2(1f - u, v);
                }
                else
                {
                    vertices[idx] = new Vector3(x, y, z);
                    uvs[idx] = new Vector2(u, v);
                }
                idx++;
            }
        }

        int[] triangles = new int[longitudeSegments * latitudeSegments * 6];
        int triIdx = 0;

        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int current = lat * (longitudeSegments + 1) + lon;
                int next = current + longitudeSegments + 1;

                if (insideOut)
                {
                    triangles[triIdx++] = current;
                    triangles[triIdx++] = current + 1;
                    triangles[triIdx++] = next;

                    triangles[triIdx++] = next;
                    triangles[triIdx++] = current + 1;
                    triangles[triIdx++] = next + 1;
                }
                else
                {
                    triangles[triIdx++] = current;
                    triangles[triIdx++] = next;
                    triangles[triIdx++] = current + 1;

                    triangles[triIdx++] = next;
                    triangles[triIdx++] = next + 1;
                    triangles[triIdx++] = current + 1;
                }
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        return mesh;
    }
    #endregion

    #region Unity Lifecycle
    private void LateUpdate()
    {
        // Keep flat screen facing camera
        if (IsVisible && _cameraRig != null && 
            (CurrentProjection == ImageProjectionType.Flat ||
             CurrentProjection == ImageProjectionType.FlatStereoSBS ||
             CurrentProjection == ImageProjectionType.FlatStereoOU))
        {
            UpdatePosition();
        }
    }

    private void OnDestroy()
    {
        Dispose();
    }
    #endregion
}
