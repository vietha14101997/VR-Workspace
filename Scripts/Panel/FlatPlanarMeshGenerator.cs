using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generates a flat planar mesh for seamless cluster visual.
/// Each panel face is a TRUE FLAT quad. Only fold areas between panels have curvature.
/// The mesh exactly matches board positions in WorldPanelClusterRig.
/// </summary>
public static class FlatPlanarMeshGenerator
{
    /// <summary>
    /// Generate a flat planar mesh with flat panel faces and curved fold areas.
    /// Panel faces are true flat quads matching exact board positions.
    /// Folds are small curved sections connecting adjacent panels.
    /// </summary>
    public static Mesh Generate(
        int panelCount,
        float panelWidth,
        float panelHeight,
        float arcRadius,
        int foldSegments = 6,
        int verticalSegments = 2,
        float contentMarginH = 0.04f,
        float contentMarginV = 0.045f)
    {
        if (panelCount < 1) panelCount = 1;
        if (panelCount > 3) panelCount = 3;
        if (foldSegments < 2) foldSegments = 2;
        if (verticalSegments < 1) verticalSegments = 1;

        // Board dimensions (content area after margins)
        float boardWidth = panelWidth * (1f - 2f * contentMarginH);
        float boardHeight = panelHeight * (1f - 2f * contentMarginV);
        float halfBoardWidth = boardWidth / 2f;
        float halfBoardHeight = boardHeight / 2f;

        // Yaw angle per panel (matches WorldPanelClusterRig positioning)
        float boardAngleRad = 2f * Mathf.Atan(boardWidth / 2f / arcRadius);
        float boardAngleDeg = boardAngleRad * Mathf.Rad2Deg;

        // Get panel yaw angles based on FixedThreeSlot layout
        float[] panelYaws = GetPanelYaws(panelCount, boardAngleDeg);

        // Build mesh data
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uv0 = new List<Vector2>(); // Global UV
        List<Vector2> uv1 = new List<Vector2>(); // Per-panel UV
        List<Vector2> uv2 = new List<Vector2>(); // Panel index
        List<int> triangles = new List<int>();

        // Track total width for UV normalization
        float totalWidth = panelCount * boardWidth;
        int foldCount = panelCount - 1;
        float foldWidth = 0.02f; // Small fold width in meters
        totalWidth += foldCount * foldWidth;

        float accumulatedU = 0f;

        for (int panelIdx = 0; panelIdx < panelCount; panelIdx++)
        {
            float panelYawDeg = panelYaws[panelIdx];
            float panelYawRad = panelYawDeg * Mathf.Deg2Rad;

            // Panel center position in ClusterRig local space
            // This matches PlacePanelOnArc calculation relative to ClusterRig
            float panelCenterX = Mathf.Sin(panelYawRad) * arcRadius;
            float panelCenterZ = Mathf.Cos(panelYawRad) * arcRadius - arcRadius;

            // Panel orientation vectors
            Vector3 panelRight = new Vector3(Mathf.Cos(panelYawRad), 0f, -Mathf.Sin(panelYawRad));
            Vector3 panelUp = Vector3.up;
            Vector3 panelNormal = new Vector3(-Mathf.Sin(panelYawRad), 0f, -Mathf.Cos(panelYawRad));
            Vector3 panelCenter = new Vector3(panelCenterX, 0f, panelCenterZ);

            // Add FLAT quad for this panel face
            int baseVertex = vertices.Count;
            float panelUStart = accumulatedU / totalWidth;
            float panelUEnd = (accumulatedU + boardWidth) / totalWidth;

            // Create flat quad vertices (4 corners + subdivisions for vertical segments)
            for (int vy = 0; vy <= verticalSegments; vy++)
            {
                float vt = (float)vy / verticalSegments;
                float yOffset = (vt - 0.5f) * boardHeight;

                // Left edge
                Vector3 leftPos = panelCenter + panelRight * (-halfBoardWidth) + panelUp * yOffset;
                vertices.Add(leftPos);
                normals.Add(panelNormal);
                uv0.Add(new Vector2(panelUStart, vt));
                uv1.Add(new Vector2(0f, vt));
                uv2.Add(new Vector2(0f, panelIdx));

                // Right edge
                Vector3 rightPos = panelCenter + panelRight * halfBoardWidth + panelUp * yOffset;
                vertices.Add(rightPos);
                normals.Add(panelNormal);
                uv0.Add(new Vector2(panelUEnd, vt));
                uv1.Add(new Vector2(1f, vt));
                uv2.Add(new Vector2(1f, panelIdx));
            }

            // Add triangles for panel face
            for (int vy = 0; vy < verticalSegments; vy++)
            {
                int bl = baseVertex + vy * 2;
                int br = bl + 1;
                int tl = bl + 2;
                int tr = bl + 3;

                triangles.Add(bl);
                triangles.Add(tl);
                triangles.Add(tr);

                triangles.Add(bl);
                triangles.Add(tr);
                triangles.Add(br);
            }

            accumulatedU += boardWidth;

            // Add FOLD section between this panel and next (if not last panel)
            if (panelIdx < panelCount - 1)
            {
                float nextPanelYawDeg = panelYaws[panelIdx + 1];

                // Generate curved fold mesh connecting right edge of current panel to left edge of next
                int foldBaseVertex = vertices.Count;
                float foldUStart = accumulatedU / totalWidth;
                float foldUEnd = (accumulatedU + foldWidth) / totalWidth;

                for (int vy = 0; vy <= verticalSegments; vy++)
                {
                    float vt = (float)vy / verticalSegments;
                    float yOffset = (vt - 0.5f) * boardHeight;

                    // Start edge (right edge of current panel - reuse calculation)
                    Vector3 startPos = panelCenter + panelRight * halfBoardWidth + panelUp * yOffset;

                    // End edge (left edge of next panel)
                    float nextYawRad = nextPanelYawDeg * Mathf.Deg2Rad;
                    float nextCenterX = Mathf.Sin(nextYawRad) * arcRadius;
                    float nextCenterZ = Mathf.Cos(nextYawRad) * arcRadius - arcRadius;
                    Vector3 nextRight = new Vector3(Mathf.Cos(nextYawRad), 0f, -Mathf.Sin(nextYawRad));
                    Vector3 nextCenter = new Vector3(nextCenterX, 0f, nextCenterZ);
                    Vector3 endPos = nextCenter + nextRight * (-halfBoardWidth) + panelUp * yOffset;

                    // Generate fold segments with smooth interpolation
                    for (int seg = 0; seg <= foldSegments; seg++)
                    {
                        float t = (float)seg / foldSegments;
                        float smoothT = t * t * (3f - 2f * t); // Smoothstep

                        // Interpolate position
                        Vector3 pos = Vector3.Lerp(startPos, endPos, smoothT);

                        // Interpolate normal
                        float interpYaw = Mathf.Lerp(panelYawDeg, nextPanelYawDeg, smoothT) * Mathf.Deg2Rad;
                        Vector3 normal = new Vector3(-Mathf.Sin(interpYaw), 0f, -Mathf.Cos(interpYaw));

                        // UV
                        float globalU = Mathf.Lerp(foldUStart, foldUEnd, t);
                        float blendIdx = Mathf.Lerp(panelIdx, panelIdx + 1, smoothT);

                        vertices.Add(pos);
                        normals.Add(normal);
                        uv0.Add(new Vector2(globalU, vt));
                        uv1.Add(new Vector2(t, vt)); // 0-1 across fold
                        uv2.Add(new Vector2(t, blendIdx));
                    }
                }

                // Add triangles for fold
                int foldVerticesPerRow = foldSegments + 1;
                for (int vy = 0; vy < verticalSegments; vy++)
                {
                    for (int seg = 0; seg < foldSegments; seg++)
                    {
                        int bl = foldBaseVertex + vy * foldVerticesPerRow + seg;
                        int br = bl + 1;
                        int tl = bl + foldVerticesPerRow;
                        int tr = tl + 1;

                        triangles.Add(bl);
                        triangles.Add(tl);
                        triangles.Add(tr);

                        triangles.Add(bl);
                        triangles.Add(tr);
                        triangles.Add(br);
                    }
                }

                accumulatedU += foldWidth;
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = $"FlatPlanarMesh_{panelCount}Panels";
        mesh.vertices = vertices.ToArray();
        mesh.normals = normals.ToArray();
        mesh.uv = uv0.ToArray();
        mesh.uv2 = uv1.ToArray();
        mesh.uv3 = uv2.ToArray();
        mesh.triangles = triangles.ToArray();

        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        Debug.Log($"[FlatPlanarMeshGenerator] Generated mesh: {panelCount} panels, {mesh.vertexCount} vertices, " +
            $"bounds={mesh.bounds.size}, center={mesh.bounds.center}");

        return mesh;
    }

    /// <summary>
    /// Generate mesh with glow expansion for border/background layers.
    /// Expands the panel faces outward and the fold areas accordingly.
    /// </summary>
    public static Mesh GenerateWithGlowExpansion(
        int panelCount,
        float panelWidth,
        float panelHeight,
        float arcRadius,
        float glowExpansion,
        int foldSegments = 6,
        int verticalSegments = 2,
        float contentMarginH = 0.04f,
        float contentMarginV = 0.045f)
    {
        if (panelCount < 1) panelCount = 1;
        if (panelCount > 3) panelCount = 3;
        if (foldSegments < 2) foldSegments = 2;
        if (verticalSegments < 1) verticalSegments = 1;

        // Expanded board dimensions
        float boardWidth = panelWidth * (1f - 2f * contentMarginH) + glowExpansion * 2f;
        float boardHeight = panelHeight * (1f - 2f * contentMarginV) + glowExpansion * 2f;
        float halfBoardWidth = boardWidth / 2f;
        float halfBoardHeight = boardHeight / 2f;

        // Yaw angle per panel (use original margins for positioning)
        float originalBoardWidth = panelWidth * (1f - 2f * contentMarginH);
        float boardAngleRad = 2f * Mathf.Atan(originalBoardWidth / 2f / arcRadius);
        float boardAngleDeg = boardAngleRad * Mathf.Rad2Deg;

        float[] panelYaws = GetPanelYaws(panelCount, boardAngleDeg);

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uv0 = new List<Vector2>();
        List<Vector2> uv1 = new List<Vector2>();
        List<Vector2> uv2 = new List<Vector2>();
        List<int> triangles = new List<int>();

        float totalWidth = panelCount * boardWidth;
        int foldCount = panelCount - 1;
        float foldWidth = 0.02f + glowExpansion;
        totalWidth += foldCount * foldWidth;

        float accumulatedU = 0f;

        for (int panelIdx = 0; panelIdx < panelCount; panelIdx++)
        {
            float panelYawDeg = panelYaws[panelIdx];
            float panelYawRad = panelYawDeg * Mathf.Deg2Rad;

            float panelCenterX = Mathf.Sin(panelYawRad) * arcRadius;
            float panelCenterZ = Mathf.Cos(panelYawRad) * arcRadius - arcRadius;

            Vector3 panelRight = new Vector3(Mathf.Cos(panelYawRad), 0f, -Mathf.Sin(panelYawRad));
            Vector3 panelUp = Vector3.up;
            Vector3 panelNormal = new Vector3(-Mathf.Sin(panelYawRad), 0f, -Mathf.Cos(panelYawRad));
            Vector3 panelCenter = new Vector3(panelCenterX, 0f, panelCenterZ);

            int baseVertex = vertices.Count;
            float panelUStart = accumulatedU / totalWidth;
            float panelUEnd = (accumulatedU + boardWidth) / totalWidth;

            for (int vy = 0; vy <= verticalSegments; vy++)
            {
                float vt = (float)vy / verticalSegments;
                float yOffset = (vt - 0.5f) * boardHeight;

                Vector3 leftPos = panelCenter + panelRight * (-halfBoardWidth) + panelUp * yOffset;
                vertices.Add(leftPos);
                normals.Add(panelNormal);
                uv0.Add(new Vector2(panelUStart, vt));
                uv1.Add(new Vector2(0f, vt));
                uv2.Add(new Vector2(0f, panelIdx));

                Vector3 rightPos = panelCenter + panelRight * halfBoardWidth + panelUp * yOffset;
                vertices.Add(rightPos);
                normals.Add(panelNormal);
                uv0.Add(new Vector2(panelUEnd, vt));
                uv1.Add(new Vector2(1f, vt));
                uv2.Add(new Vector2(1f, panelIdx));
            }

            for (int vy = 0; vy < verticalSegments; vy++)
            {
                int bl = baseVertex + vy * 2;
                int br = bl + 1;
                int tl = bl + 2;
                int tr = bl + 3;

                triangles.Add(bl);
                triangles.Add(tl);
                triangles.Add(tr);

                triangles.Add(bl);
                triangles.Add(tr);
                triangles.Add(br);
            }

            accumulatedU += boardWidth;

            if (panelIdx < panelCount - 1)
            {
                float nextPanelYawDeg = panelYaws[panelIdx + 1];

                int foldBaseVertex = vertices.Count;
                float foldUStart = accumulatedU / totalWidth;
                float foldUEnd = (accumulatedU + foldWidth) / totalWidth;

                for (int vy = 0; vy <= verticalSegments; vy++)
                {
                    float vt = (float)vy / verticalSegments;
                    float yOffset = (vt - 0.5f) * boardHeight;

                    Vector3 startPos = panelCenter + panelRight * halfBoardWidth + panelUp * yOffset;

                    float nextYawRad = nextPanelYawDeg * Mathf.Deg2Rad;
                    float nextCenterX = Mathf.Sin(nextYawRad) * arcRadius;
                    float nextCenterZ = Mathf.Cos(nextYawRad) * arcRadius - arcRadius;
                    Vector3 nextRight = new Vector3(Mathf.Cos(nextYawRad), 0f, -Mathf.Sin(nextYawRad));
                    Vector3 nextCenter = new Vector3(nextCenterX, 0f, nextCenterZ);
                    Vector3 endPos = nextCenter + nextRight * (-halfBoardWidth) + panelUp * yOffset;

                    for (int seg = 0; seg <= foldSegments; seg++)
                    {
                        float t = (float)seg / foldSegments;
                        float smoothT = t * t * (3f - 2f * t);

                        Vector3 pos = Vector3.Lerp(startPos, endPos, smoothT);

                        float interpYaw = Mathf.Lerp(panelYawDeg, nextPanelYawDeg, smoothT) * Mathf.Deg2Rad;
                        Vector3 normal = new Vector3(-Mathf.Sin(interpYaw), 0f, -Mathf.Cos(interpYaw));

                        float globalU = Mathf.Lerp(foldUStart, foldUEnd, t);
                        float blendIdx = Mathf.Lerp(panelIdx, panelIdx + 1, smoothT);

                        vertices.Add(pos);
                        normals.Add(normal);
                        uv0.Add(new Vector2(globalU, vt));
                        uv1.Add(new Vector2(t, vt));
                        uv2.Add(new Vector2(t, blendIdx));
                    }
                }

                int foldVerticesPerRow = foldSegments + 1;
                for (int vy = 0; vy < verticalSegments; vy++)
                {
                    for (int seg = 0; seg < foldSegments; seg++)
                    {
                        int bl = foldBaseVertex + vy * foldVerticesPerRow + seg;
                        int br = bl + 1;
                        int tl = bl + foldVerticesPerRow;
                        int tr = tl + 1;

                        triangles.Add(bl);
                        triangles.Add(tl);
                        triangles.Add(tr);

                        triangles.Add(bl);
                        triangles.Add(tr);
                        triangles.Add(br);
                    }
                }

                accumulatedU += foldWidth;
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = $"FlatPlanarMesh_{panelCount}Panels_Expanded";
        mesh.vertices = vertices.ToArray();
        mesh.normals = normals.ToArray();
        mesh.uv = uv0.ToArray();
        mesh.uv2 = uv1.ToArray();
        mesh.uv3 = uv2.ToArray();
        mesh.triangles = triangles.ToArray();

        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        return mesh;
    }

    /// <summary>
    /// Get yaw angles for panels based on FixedThreeSlot layout.
    /// </summary>
    private static float[] GetPanelYaws(int panelCount, float angleDeg)
    {
        float[] yaws = new float[panelCount];

        switch (panelCount)
        {
            case 1:
                yaws[0] = 0f;
                break;
            case 2:
                // Center (slot 0) and Right (slot 1)
                yaws[0] = 0f;
                yaws[1] = angleDeg;
                break;
            case 3:
                // Left (-1), Center (0), Right (1)
                yaws[0] = -angleDeg;
                yaws[1] = 0f;
                yaws[2] = angleDeg;
                break;
            default:
                for (int i = 0; i < panelCount; i++)
                {
                    float offset = i - (panelCount - 1) / 2f;
                    yaws[i] = offset * angleDeg;
                }
                break;
        }

        return yaws;
    }

    /// <summary>
    /// Calculate total dimensions for shader parameters.
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
        float boardWidth = panelWidth * (1f - 2f * contentMarginH);
        float boardAngleRad = 2f * Mathf.Atan(boardWidth / 2f / arcRadius);
        float boardAngleDeg = boardAngleRad * Mathf.Rad2Deg;

        float foldWidth = 0.02f;
        int foldCount = panelCount - 1;

        totalWidth = panelCount * boardWidth + foldCount * foldWidth;

        float[] yaws = GetPanelYaws(panelCount, boardAngleDeg);
        if (panelCount > 1)
        {
            totalArcAngle = Mathf.Abs(yaws[panelCount - 1] - yaws[0]) + boardAngleDeg;
        }
        else
        {
            totalArcAngle = boardAngleDeg;
        }
    }
}
