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

    private WorldPanelPlus _worldPanel;
    private Transform _parentTransform;
    private Vector2Int _resolution = new Vector2Int(1920, 1080);
    private DisplaySettings _currentSettings = DisplaySettings.Default;

    // Curvature tracking
    private float _currentCurvature = 0f;
    private Mesh _curvedMesh;

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
        if (_worldPanel == null) return;

        _worldPanel.contentTexture = texture;
        _worldPanel.Apply();
        
        Debug.Log($"[FlatProjectionRenderer] SetTexture: {(texture != null ? $"{texture.width}x{texture.height}" : "null")}");

        // Update resolution for aspect ratio calculations
        if (texture != null)
        {
            _resolution = new Vector2Int(texture.width, texture.height);
            UpdateScreenAspect();
        }
    }

    public void SetTextureNV12(Texture2D yPlane, Texture2D uvPlane)
    {
        // WorldPanelPlus standard shader doesn't support NV12 out of the box with edge feathering.
        // For now, we fallback to setting the Y plane as content (grayscale) or would need a custom shader.
        // Assuming Windows platform where direct texture is mostly used.
        // If NV12 is strict requirement, we would need to swap the WorldPanelPlus material shader here.
        
        if (_worldPanel == null) return;
        
        _worldPanel.contentTexture = yPlane; // Fallback
        _worldPanel.Apply();

        if (yPlane != null)
        {
            _resolution = new Vector2Int(yPlane.width, yPlane.height);
            UpdateScreenAspect();
        }
    }

    public void SetStereoMode(StereoMode mode)
    {
        if (_worldPanel == null) return;

        _worldPanel.stereoMode = mode;
        _worldPanel.Apply();
    }

    public void UpdateDisplay(DisplaySettings settings)
    {
        _currentSettings = settings;

        if (_worldPanel == null) return;

        // Position screen at local origin - parent transform handles world positioning
        // Apply small Z offset based on distance setting for fine-tuning
        Vector3 localPos = new Vector3(
            settings.PositionOffset.x,
            settings.PositionOffset.y,
            settings.Distance // Use distance as Z offset from parent
        );
        _worldPanel.transform.localPosition = localPos;
        
        // Keep facing away from parent origin (toward viewer)
        _worldPanel.transform.localRotation = settings.RotationOffset;

        // Detect curvature change and rebuild mesh if needed
        if (Mathf.Abs(settings.Curvature - _currentCurvature) > 0.001f)
        {
            _currentCurvature = settings.Curvature;
            RebuildMesh();
        }

        // Update size based on scale and aspect ratio
        UpdateScreenAspect();
    }

    public void Show()
    {
        if (_worldPanel != null)
        {
            _worldPanel.gameObject.SetActive(true);
            UpdateDisplay(_currentSettings); // Ensure position is updated
        }
        _isActive = true;
    }

    public void Hide()
    {
        if (_worldPanel != null)
        {
            _worldPanel.gameObject.SetActive(false);
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
        if (_curvedMesh != null)
        {
            Destroy(_curvedMesh);
            _curvedMesh = null;
        }

        if (_worldPanel != null)
        {
            Destroy(_worldPanel.gameObject);
            _worldPanel = null;
        }

        _isInitialized = false;
        _isActive = false;
    }
    #endregion

    #region Private Methods
    private void CreateScreenObject()
    {
        GameObject go = new GameObject("FlatVideoScreen");
        go.transform.SetParent(_parentTransform, false);
        
        // Set layer - use Default if VirtualObjects doesn't exist
        int layer = LayerMask.NameToLayer("VirtualObjects");
        if (layer < 0) layer = 0;
        go.layer = layer;

        // Add WorldPanelPlus
        _worldPanel = go.AddComponent<WorldPanelPlus>();
        
        // Configure WorldPanelPlus defaults
        _worldPanel.useBoardEdgeFeather = true;
        _worldPanel.boardEdgeWidthUV = 0.02f;
        _worldPanel.boardCornerRadius = 0.03f;
        _worldPanel.boardEdgeColor = Color.black; // Dark border looks good for video
        _worldPanel.panelTint = Color.white;
        _worldPanel.enableSharpening = true; // Enable sharpening for video
        _worldPanel.sharpnessStrength = 0.5f;
        _worldPanel.anisoLevel = 16;
        _worldPanel.mipMapBias = -0.5f;
        _worldPanel.cursorEnable = false; // No cursor for video projection
        
        // Initialize
        _worldPanel.Rebuild();

        // Remove BoxCollider — video screen is a projection, not an interactable entity.
        // This prevents the board from blocking raycasts to the controls panel behind it.
        if (_worldPanel.board != null)
        {
            var col = _worldPanel.board.GetComponent<BoxCollider>();
            if (col != null) Destroy(col);
        }
    }

    private void UpdateScreenAspect()
    {
        if (_worldPanel == null) return;

        float resX = _resolution.x;
        float resY = _resolution.y;

        // Adjust resolution for aspect ratio calculation based on stereo mode
        if (_worldPanel.stereoMode == StereoMode.SideBySide)
        {
            resX /= 2f;
        }
        else if (_worldPanel.stereoMode == StereoMode.OverUnder)
        {
            resY /= 2f;
        }

        float aspect = resY > 0 ? resX / resY : 16f / 9f;

        // Base height of 1 meter, adjust width by aspect ratio
        float baseHeight = 1.0f * _currentSettings.Scale;
        float baseWidth = baseHeight * aspect;

        bool changed = false;
        if (Mathf.Abs(_worldPanel.width - baseWidth) > 0.001f)
        {
            _worldPanel.width = baseWidth;
            changed = true;
        }
        if (Mathf.Abs(_worldPanel.height - baseHeight) > 0.001f)
        {
            _worldPanel.height = baseHeight;
            changed = true;
        }

        if (changed)
        {
            // Apply scale changes (WorldPanelPlus Apply handles localScale based on width/height)
            _worldPanel.Apply();

            // Regenerate curved mesh if active (arc radius depends on width)
            if (_currentCurvature > 0.001f)
            {
                ApplyCurvedMesh();
            }
        }
    }

    /// <summary>
    /// Rebuild mesh based on current curvature setting.
    /// Curvature 0 = flat quad, curvature > 0 = curved cylindrical arc.
    /// </summary>
    private void RebuildMesh()
    {
        if (_worldPanel == null || _worldPanel.board == null) return;

        if (_currentCurvature <= 0.001f)
        {
            // Flat mode: restore standard quad
            ApplyFlatQuad();
        }
        else
        {
            // Curved mode: generate cylindrical arc mesh
            ApplyCurvedMesh();
        }

        Debug.Log($"[FlatProjectionRenderer] RebuildMesh: curvature={_currentCurvature:F3}");
    }

    /// <summary>
    /// Restore WorldPanelPlus board to standard flat quad.
    /// </summary>
    private void ApplyFlatQuad()
    {
        if (_curvedMesh != null)
        {
            Destroy(_curvedMesh);
            _curvedMesh = null;
        }

        // Restore quad mesh on the board
        MeshFilter mf = _worldPanel.board.GetComponent<MeshFilter>();
        if (mf != null)
        {
            // Unity's built-in quad mesh
            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mf.sharedMesh = quadGo.GetComponent<MeshFilter>().sharedMesh;
            Destroy(quadGo);
        }
    }

    /// <summary>
    /// Generate and apply a curved cylindrical arc mesh.
    /// Uses the same algorithm as CurvedClusterMeshGenerator:
    /// vertices at (sin(θ)*R, y, cos(θ)*R - R).
    /// </summary>
    private void ApplyCurvedMesh()
    {
        float width = _worldPanel.width;
        float height = _worldPanel.height;

        if (width <= 0 || height <= 0) return;

        // Calculate arc radius from curvature parameter
        // curvature 0.25 = gentle curve, 1.0 = tight wrap
        // arcRadius = width / (2 * curvature) gives intuitive control:
        //   curvature=0.25 -> radius = 2*width (gentle)
        //   curvature=1.0  -> radius = 0.5*width (tight)
        float arcRadius = width / (2f * _currentCurvature);
        arcRadius = Mathf.Max(arcRadius, 0.3f); // Minimum radius to prevent extreme distortion

        // Arc angle: 2 * atan(width / 2 / radius) - matches CurvedClusterMeshGenerator
        float totalArcAngleRad = 2f * Mathf.Atan(width / 2f / arcRadius);

        int segX = CURVED_SEGMENTS;
        int segY = 2; // Vertical segments (minimal needed)
        int vertexCountX = segX + 1;
        int vertexCountY = segY + 1;
        int vertexCount = vertexCountX * vertexCountY;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];

        for (int y = 0; y <= segY; y++)
        {
            float vt = (float)y / segY;
            float yPos = (vt - 0.5f); // -0.5 to 0.5 (unit mesh, scaled by board localScale)

            for (int x = 0; x <= segX; x++)
            {
                float ut = (float)x / segX;

                // Arc angle centered: ut=0 -> left edge, ut=1 -> right edge
                float angle = (ut - 0.5f) * totalArcAngleRad;

                // Cylindrical coordinates (same as CurvedClusterMeshGenerator)
                // But normalized to unit mesh (-0.5 to 0.5 range) since WorldPanelPlus
                // applies width/height via board localScale
                float xPos = Mathf.Sin(angle) * arcRadius / width;
                float zPos = (Mathf.Cos(angle) * arcRadius - arcRadius) / width;

                int idx = y * vertexCountX + x;
                vertices[idx] = new Vector3(xPos, yPos, zPos);

                // Normal points toward arc center (viewer)
                normals[idx] = new Vector3(-Mathf.Sin(angle), 0f, -Mathf.Cos(angle));

                // Standard UV mapping 0-1
                uvs[idx] = new Vector2(ut, vt);
            }
        }

        // Generate triangles
        int quadCount = segX * segY;
        int[] triangles = new int[quadCount * 6];
        int triIdx = 0;

        for (int y = 0; y < segY; y++)
        {
            for (int x = 0; x < segX; x++)
            {
                int bl = y * vertexCountX + x;
                int br = bl + 1;
                int tl = bl + vertexCountX;
                int tr = tl + 1;

                // First triangle
                triangles[triIdx++] = bl;
                triangles[triIdx++] = tl;
                triangles[triIdx++] = tr;

                // Second triangle
                triangles[triIdx++] = bl;
                triangles[triIdx++] = tr;
                triangles[triIdx++] = br;
            }
        }

        // Create or update mesh
        if (_curvedMesh == null)
        {
            _curvedMesh = new Mesh();
            _curvedMesh.name = "CurvedVideoScreen";
        }
        else
        {
            _curvedMesh.Clear();
        }

        _curvedMesh.vertices = vertices;
        _curvedMesh.normals = normals;
        _curvedMesh.uv = uvs;
        _curvedMesh.triangles = triangles;
        _curvedMesh.RecalculateBounds();
        _curvedMesh.RecalculateTangents();

        // Apply to board MeshFilter
        MeshFilter mf = _worldPanel.board.GetComponent<MeshFilter>();
        if (mf != null)
        {
            mf.sharedMesh = _curvedMesh;
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
