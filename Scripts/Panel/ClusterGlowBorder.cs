using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generates a glowing border mesh that traces the outer perimeter of a panel cluster.
/// Attach as child of WorldPanelClusterRig.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class ClusterGlowBorder : MonoBehaviour
{
    [Header("Glow Colors")]
    [ColorUsage(true, true)]
    public Color colorA = new Color(0.3f, 1f, 1f, 1f);
    [ColorUsage(true, true)]
    public Color colorB = new Color(1f, 0.4f, 1f, 1f);

    [Header("Border Size")]
    [Tooltip("Half-width of the border strip in meters")]
    [Range(0.005f, 0.1f)]
    public float borderWidth = 0.015f;

    [Tooltip("Total glow spread width in meters")]
    [Range(0.01f, 0.2f)]
    public float glowSpread = 0.05f;

    [Header("Corner Settings")]
    [Tooltip("Radius for rounded corners at the 4 outer corners")]
    [Range(0f, 0.1f)]
    public float cornerRadius = 0.03f;

    [Tooltip("Number of segments for corner rounding")]
    [Range(2, 8)]
    public int cornerSegments = 4;

    [Header("Glow Intensity")]
    [Range(0.5f, 3f)]
    public float layer1Alpha = 1.5f;
    [Range(0f, 2f)]
    public float layer2Alpha = 1.0f;
    [Range(0f, 1f)]
    public float layer3Alpha = 0.5f;
    [Range(0f, 0.5f)]
    public float layer4Alpha = 0.2f;

    [Header("Visual")]
    [Range(0.5f, 2f)]
    public float brightness = 1.0f;

    // Components
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Material _material;
    private Mesh _mesh;

    // Cached panel data
    private List<PanelCorners> _panelCorners = new List<PanelCorners>();

    private struct PanelCorners
    {
        public Vector3 topLeft;
        public Vector3 topRight;
        public Vector3 bottomLeft;
        public Vector3 bottomRight;
        public Vector3 forward; // Panel facing direction
    }

    void Awake()
    {
        EnsureComponents();
    }

    void OnEnable()
    {
        EnsureComponents();
    }

    void EnsureComponents()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = "ClusterBorderMesh";
            _meshFilter.sharedMesh = _mesh;
        }

        EnsureMaterial();
    }

    void EnsureMaterial()
    {
        if (_material == null)
        {
            Shader shader = Shader.Find("Custom/ClusterBorderGlow");
            if (shader != null)
            {
                _material = new Material(shader);
                _material.name = "ClusterBorderGlowMat";
            }
            else
            {
                Debug.LogWarning("[ClusterGlowBorder] ClusterBorderGlow shader not found!");
                _material = new Material(Shader.Find("Sprites/Default"));
            }
        }

        if (_meshRenderer.sharedMaterial != _material)
            _meshRenderer.sharedMaterial = _material;

        // Set layer for VR rendering
        int vrLayer = LayerMask.NameToLayer("VirtualObjects");
        if (vrLayer != -1)
            gameObject.layer = vrLayer;
    }

    /// <summary>
    /// Update the border mesh based on the provided panels.
    /// Call this after panels are repositioned.
    /// </summary>
    public void UpdateBorder(List<WorldPanelPlus> panels)
    {
        if (panels == null || panels.Count == 0)
        {
            ClearMesh();
            return;
        }

        EnsureComponents();

        // Collect corner positions for all panels
        CollectPanelCorners(panels);

        // Compute the outer perimeter points
        List<Vector3> perimeter = ComputePerimeter();

        if (perimeter.Count < 3)
        {
            ClearMesh();
            return;
        }

        // Generate the mesh strip
        GenerateBorderMesh(perimeter);

        // Update material properties
        UpdateMaterialProperties();
    }

    void CollectPanelCorners(List<WorldPanelPlus> panels)
    {
        _panelCorners.Clear();

        foreach (var panel in panels)
        {
            if (panel == null) continue;

            float halfW = panel.width * 0.5f;
            float halfH = panel.height * 0.5f;

            Transform t = panel.transform;
            Vector3 right = t.right;
            Vector3 up = t.up;
            Vector3 center = t.position;

            var corners = new PanelCorners
            {
                topLeft = center - right * halfW + up * halfH,
                topRight = center + right * halfW + up * halfH,
                bottomLeft = center - right * halfW - up * halfH,
                bottomRight = center + right * halfW - up * halfH,
                forward = t.forward
            };

            _panelCorners.Add(corners);
        }
    }

    List<Vector3> ComputePerimeter()
    {
        List<Vector3> points = new List<Vector3>();

        if (_panelCorners.Count == 0) return points;

        int n = _panelCorners.Count;
        var first = _panelCorners[0];
        var last = _panelCorners[n - 1];

        // === TOP-LEFT CORNER (rounded) ===
        AddRoundedCorner(points, first.topLeft, -first.forward,
            (first.topRight - first.topLeft).normalized,    // direction after (along top edge)
            (first.topLeft - first.bottomLeft).normalized); // direction before (from left edge)

        // === TOP EDGE (left to right) ===
        for (int i = 0; i < n; i++)
        {
            var panel = _panelCorners[i];

            // Add top-right of this panel
            points.Add(panel.topRight);

            // If not the last panel, add the junction point (top-left of next panel)
            if (i < n - 1)
            {
                var nextPanel = _panelCorners[i + 1];
                points.Add(nextPanel.topLeft);
            }
        }

        // === TOP-RIGHT CORNER (rounded) ===
        AddRoundedCorner(points, last.topRight, -last.forward,
            (last.bottomRight - last.topRight).normalized,  // direction after (down right edge)
            (last.topRight - last.topLeft).normalized);     // direction before (from top edge)

        // === RIGHT EDGE of last panel ===
        points.Add(last.bottomRight);

        // === BOTTOM-RIGHT CORNER (rounded) ===
        AddRoundedCorner(points, last.bottomRight, -last.forward,
            (last.bottomLeft - last.bottomRight).normalized,   // direction after (along bottom edge)
            (last.bottomRight - last.topRight).normalized);    // direction before (from right edge)

        // === BOTTOM EDGE (right to left) ===
        for (int i = n - 1; i >= 0; i--)
        {
            var panel = _panelCorners[i];

            // Add bottom-left of this panel
            points.Add(panel.bottomLeft);

            // If not the first panel, add the junction point (bottom-right of previous panel)
            if (i > 0)
            {
                var prevPanel = _panelCorners[i - 1];
                points.Add(prevPanel.bottomRight);
            }
        }

        // === BOTTOM-LEFT CORNER (rounded) ===
        AddRoundedCorner(points, first.bottomLeft, -first.forward,
            (first.topLeft - first.bottomLeft).normalized,     // direction after (up left edge)
            (first.bottomLeft - first.bottomRight).normalized); // direction before (from bottom edge)

        // === LEFT EDGE of first panel ===
        // Loop will close back to first point automatically in mesh generation

        return points;
    }

    void AddRoundedCorner(List<Vector3> points, Vector3 corner, Vector3 normal, Vector3 dirAfter, Vector3 dirBefore)
    {
        if (cornerRadius <= 0.001f || cornerSegments < 2)
        {
            points.Add(corner);
            return;
        }

        // Normalize directions
        Vector3 d1 = dirBefore.normalized;
        Vector3 d2 = dirAfter.normalized;

        // Calculate corner center (inset from corner point)
        Vector3 cornerCenter = corner - d1 * cornerRadius - d2 * cornerRadius;

        // Generate arc points
        for (int i = 0; i <= cornerSegments; i++)
        {
            float t = (float)i / cornerSegments;
            // Interpolate between the two directions
            Vector3 dir = Vector3.Slerp(-d1, d2, t).normalized;
            Vector3 pt = cornerCenter + dir * cornerRadius;
            points.Add(pt);
        }
    }

    void GenerateBorderMesh(List<Vector3> perimeter)
    {
        int pointCount = perimeter.Count;
        if (pointCount < 3) return;

        // Calculate total perimeter length for UV mapping
        float totalLength = 0f;
        List<float> segmentLengths = new List<float>();

        for (int i = 0; i < pointCount; i++)
        {
            int next = (i + 1) % pointCount;
            float len = Vector3.Distance(perimeter[i], perimeter[next]);
            segmentLengths.Add(len);
            totalLength += len;
        }

        if (totalLength < 0.001f) return;

        // Calculate centroid for determining outward direction
        Vector3 centroid = Vector3.zero;
        foreach (var p in perimeter)
            centroid += p;
        centroid /= pointCount;

        // Generate vertices and UVs
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Color> colors = new List<Color>();
        List<int> triangles = new List<int>();

        float accumulatedLength = 0f;

        for (int i = 0; i < pointCount; i++)
        {
            Vector3 current = perimeter[i];
            int prev = (i - 1 + pointCount) % pointCount;
            int next = (i + 1) % pointCount;

            // Calculate tangent (average of incoming and outgoing directions)
            Vector3 dirIn = (current - perimeter[prev]).normalized;
            Vector3 dirOut = (perimeter[next] - current).normalized;

            // Handle zero-length segments
            if (dirIn.sqrMagnitude < 0.0001f) dirIn = dirOut;
            if (dirOut.sqrMagnitude < 0.0001f) dirOut = dirIn;

            Vector3 tangent = (dirIn + dirOut);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = dirOut;
            tangent.Normalize();

            // Calculate normal perpendicular to tangent
            // For a mostly-horizontal perimeter, use cross with up
            // For vertical sections, cross with forward
            Vector3 normal;

            // Primary method: cross with up to get horizontal-ish normal
            normal = Vector3.Cross(Vector3.up, tangent).normalized;

            // If tangent is too vertical, use forward instead
            if (normal.sqrMagnitude < 0.5f)
            {
                normal = Vector3.Cross(Vector3.forward, tangent).normalized;
            }

            // Ensure normal points outward (away from centroid)
            Vector3 toOutward = (current - centroid).normalized;
            if (Vector3.Dot(normal, toOutward) < 0)
                normal = -normal;

            // Create inner and outer vertices
            // Inner = toward center, Outer = away from center
            Vector3 innerVert = current - normal * borderWidth;
            Vector3 outerVert = current + normal * glowSpread;

            // Convert to local space
            innerVert = transform.InverseTransformPoint(innerVert);
            outerVert = transform.InverseTransformPoint(outerVert);

            vertices.Add(innerVert);
            vertices.Add(outerVert);

            // UV: x = position along perimeter (0-1), y = inner(0) to outer(1)
            float u = totalLength > 0 ? accumulatedLength / totalLength : 0;
            uvs.Add(new Vector2(u, 0f)); // inner
            uvs.Add(new Vector2(u, 1f)); // outer

            // Vertex colors - alpha channel for corner boost
            float cornerFactor = 0f;
            colors.Add(new Color(1, 1, 1, cornerFactor));
            colors.Add(new Color(1, 1, 1, cornerFactor));

            accumulatedLength += segmentLengths[i];
        }

        // Generate triangles (quad strip) - double-sided
        for (int i = 0; i < pointCount; i++)
        {
            int baseIdx = i * 2;
            int nextBaseIdx = ((i + 1) % pointCount) * 2;

            // Front face
            triangles.Add(baseIdx);
            triangles.Add(baseIdx + 1);
            triangles.Add(nextBaseIdx);

            triangles.Add(baseIdx + 1);
            triangles.Add(nextBaseIdx + 1);
            triangles.Add(nextBaseIdx);

            // Back face (reverse winding)
            triangles.Add(baseIdx);
            triangles.Add(nextBaseIdx);
            triangles.Add(baseIdx + 1);

            triangles.Add(baseIdx + 1);
            triangles.Add(nextBaseIdx);
            triangles.Add(nextBaseIdx + 1);
        }

        // Apply to mesh
        _mesh.Clear();
        _mesh.SetVertices(vertices);
        _mesh.SetUVs(0, uvs);
        _mesh.SetColors(colors);
        _mesh.SetTriangles(triangles, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    void UpdateMaterialProperties()
    {
        if (_material == null) return;

        _material.SetColor("_ColorA", colorA);
        _material.SetColor("_ColorB", colorB);
        _material.SetFloat("_BorderWidth", 0.15f); // UV space width
        _material.SetFloat("_GlowWidth", 0.5f);    // UV space glow
        _material.SetFloat("_Layer1Alpha", layer1Alpha);
        _material.SetFloat("_Layer2Alpha", layer2Alpha);
        _material.SetFloat("_Layer3Alpha", layer3Alpha);
        _material.SetFloat("_Layer4Alpha", layer4Alpha);
        _material.SetFloat("_Brightness", brightness);
    }

    void ClearMesh()
    {
        if (_mesh != null)
            _mesh.Clear();
    }

    void OnDestroy()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
        }

        if (_material != null)
        {
            if (Application.isPlaying)
                Destroy(_material);
            else
                DestroyImmediate(_material);
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Trigger update when properties change in editor
        if (_material != null)
        {
            UpdateMaterialProperties();
        }
    }
#endif
}
