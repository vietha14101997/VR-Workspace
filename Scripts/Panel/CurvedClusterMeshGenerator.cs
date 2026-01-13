using UnityEngine;

/// <summary>
/// Generates a curved arc mesh for seamless cluster visual.
/// The mesh follows a cylindrical arc, providing smooth visual appearance.
///
/// UV Channels:
/// - UV0: Global 0-1 coordinates across entire cluster (for gradient, border)
/// - UV1: Per-panel normalized coordinates (for content texture sampling)
/// - UV2.x: Fractional position within panel (0-1, for blend zone calculation)
/// - UV2.y: Panel index (for texture selection)
/// </summary>
public static class CurvedClusterMeshGenerator
{
    /// <summary>
    /// Generate a curved arc mesh spanning multiple panels
    /// </summary>
    /// <param name="panelCount">Number of content panels</param>
    /// <param name="panelWidth">Width of each panel in meters</param>
    /// <param name="panelHeight">Height of each panel in meters</param>
    /// <param name="arcRadius">Distance from camera/arc center in meters</param>
    /// <param name="segmentsPerPanel">Horizontal subdivisions per panel (12 = smooth)</param>
    /// <param name="verticalSegments">Vertical subdivisions (2-4 typical)</param>
    /// <param name="contentMarginH">Horizontal content margin ratio (0.04 = 4%)</param>
    /// <param name="contentMarginV">Vertical content margin ratio (0.045 = 4.5%)</param>
    /// <returns>Generated mesh with curved geometry and multi-channel UVs</returns>
    public static Mesh Generate(
        int panelCount,
        float panelWidth,
        float panelHeight,
        float arcRadius,
        int segmentsPerPanel = 12,
        int verticalSegments = 2,
        float contentMarginH = 0.04f,
        float contentMarginV = 0.045f,
        float gapMeters = 0f,
        float overlapMeters = 0f)
    {
        if (panelCount < 1) panelCount = 1;
        if (segmentsPerPanel < 2) segmentsPerPanel = 2;
        if (verticalSegments < 1) verticalSegments = 1;
        if (arcRadius < 0.1f) arcRadius = 0.1f;

        // Calculate board width (panel minus margins) - matches WorldPanelClusterRig positioning
        // UPDATE: Now using full panel width to ensure consistent physical size with Flat Planar mode,
        // and to prevent the cluster from appearing "short". Content margins are handled by shader masking.
        float boardWidth = panelWidth;

        // IMPORTANT: Arc angle calculation must match WorldPanelClusterRig.LayoutFromCamera()
        // Each panel occupies an arc angle of 2*atan(boardWidth/2/radius)
        // Total arc = sum of individual panel angles, NOT atan(totalWidth/2/radius)
        // Using atan(totalWidth) gives wrong result because atan(A+B) != atan(A) + atan(B)
        // Must include gap and overlap to match exact panel positions!
        float boardAngleRad = 2f * Mathf.Atan(boardWidth / 2f / arcRadius);
        float gapAngleRad = 2f * Mathf.Atan(gapMeters / 2f / arcRadius);
        float overlapAngleRad = 2f * Mathf.Atan(overlapMeters / 2f / arcRadius);
        float anglePerPanelRad = boardAngleRad + gapAngleRad - overlapAngleRad;
        float totalArcAngleRad = panelCount * anglePerPanelRad;

        int totalHorizontalSegments = panelCount * segmentsPerPanel;
        int vertexCountX = totalHorizontalSegments + 1;
        int vertexCountY = verticalSegments + 1;
        int vertexCount = vertexCountX * vertexCountY;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uv0 = new Vector2[vertexCount];  // Global UV
        Vector2[] uv1 = new Vector2[vertexCount];  // Per-panel UV
        Vector2[] uv2 = new Vector2[vertexCount];  // Panel index & blend info

        float halfHeight = panelHeight / 2f;

        for (int y = 0; y <= verticalSegments; y++)
        {
            float vt = (float)y / verticalSegments;
            float yPos = (vt - 0.5f) * panelHeight;

            for (int x = 0; x <= totalHorizontalSegments; x++)
            {
                float ut = (float)x / totalHorizontalSegments;

                // Arc angle: -half to +half (centered)
                // ut=0 -> angle=-half (left, negative X)
                // ut=1 -> angle=+half (right, positive X)
                float angle = (ut - 0.5f) * totalArcAngleRad;

                // Cylindrical coordinates in ClusterRig's local space:
                // - ClusterRig is at the CENTER of the arc (distanceFromCamera from camera)
                // - ClusterRig's +Z points AWAY from camera (toward where camera faces)
                // - The mesh should be at local Z near 0, curving slightly toward camera (-Z) at edges
                //
                // The arc center (camera position) is at local (0, 0, -arcRadius)
                // Vertex position on cylinder surface at angle θ from center:
                //   x = sin(θ) * arcRadius
                //   z = cos(θ) * arcRadius - arcRadius = arcRadius * (cos(θ) - 1)
                float xPos = Mathf.Sin(angle) * arcRadius;
                float zPos = Mathf.Cos(angle) * arcRadius - arcRadius;

                int idx = y * vertexCountX + x;
                vertices[idx] = new Vector3(xPos, yPos, zPos);

                // Normal points toward arc center (camera) which is at local (0, 0, -arcRadius)
                // From vertex position, direction to center is:
                //   center - vertex = (0, 0, -arcRadius) - (xPos, yPos, zPos)
                // Normalized, for a cylinder this simplifies to:
                normals[idx] = new Vector3(-Mathf.Sin(angle), 0f, -Mathf.Cos(angle));

                // UV0: Global coordinates (0-1 across entire cluster)
                uv0[idx] = new Vector2(ut, vt);

                // Calculate panel index and position within panel
                float panelIndexFloat = ut * panelCount;
                int panelIndex = Mathf.Clamp(Mathf.FloorToInt(panelIndexFloat), 0, panelCount - 1);
                float panelFrac = panelIndexFloat - panelIndex;

                // Handle edge case at ut=1.0
                if (x == totalHorizontalSegments)
                {
                    panelIndex = panelCount - 1;
                    panelFrac = 1f;
                }

                // UV1: Per-panel normalized coordinates (0-1 within each panel)
                uv1[idx] = new Vector2(panelFrac, vt);

                // UV2: x = fractional position, y = panel index
                uv2[idx] = new Vector2(panelFrac, panelIndex);
            }
        }

        // Generate triangles
        int quadCount = totalHorizontalSegments * verticalSegments;
        int[] triangles = new int[quadCount * 6];
        int triIdx = 0;

        for (int y = 0; y < verticalSegments; y++)
        {
            for (int x = 0; x < totalHorizontalSegments; x++)
            {
                int bl = y * vertexCountX + x;
                int br = bl + 1;
                int tl = bl + vertexCountX;
                int tr = tl + 1;

                // First triangle (bottom-left, top-left, top-right)
                triangles[triIdx++] = bl;
                triangles[triIdx++] = tl;
                triangles[triIdx++] = tr;

                // Second triangle (bottom-left, top-right, bottom-right)
                triangles[triIdx++] = bl;
                triangles[triIdx++] = tr;
                triangles[triIdx++] = br;
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = $"CurvedClusterMesh_{panelCount}Panels";
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv0;
        mesh.uv2 = uv1;
        mesh.uv3 = uv2;
        mesh.triangles = triangles;

        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        return mesh;
    }

    /// <summary>
    /// Generate mesh with glow expansion for border layer
    /// </summary>
    public static Mesh GenerateWithGlowExpansion(
        int panelCount,
        float panelWidth,
        float panelHeight,
        float arcRadius,
        float glowExpansion,
        int segmentsPerPanel = 12,
        int verticalSegments = 2,
        float contentMarginH = 0.04f,
        float contentMarginV = 0.045f,
        float gapMeters = 0f,
        float overlapMeters = 0f)
    {
        // Generate base mesh with expanded dimensions
        float expandedWidth = panelWidth + (glowExpansion * 2f / panelCount); // Distribute expansion
        float expandedHeight = panelHeight + glowExpansion * 2f;

        var mesh = Generate(
            panelCount,
            expandedWidth,
            expandedHeight,
            arcRadius,
            segmentsPerPanel,
            verticalSegments,
            contentMarginH,
            contentMarginV,
            gapMeters,
            overlapMeters
        );

        mesh.name = $"CurvedClusterMesh_{panelCount}Panels_Expanded";
        return mesh;
    }

    /// <summary>
    /// Calculate the total arc dimensions for a cluster
    /// </summary>
    public static void CalculateClusterDimensions(
        int panelCount,
        float panelWidth,
        float panelHeight,
        float arcRadius,
        float contentMarginH,
        out float totalWidth,
        out float totalArcAngle)
    {
        // Use board width (matches panel positioning in WorldPanelClusterRig)
        float boardWidth = panelWidth;
        totalWidth = panelCount * boardWidth;
        totalArcAngle = 2f * Mathf.Rad2Deg * Mathf.Atan(totalWidth / 2f / arcRadius);
    }
}
