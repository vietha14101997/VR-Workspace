using UnityEngine;

namespace VRWorkspace.Domain.Input.Geometry
{
    /// <summary>
    /// Result of a 2D ray-vs-AABB intersection in the virtual coordinate system.
    /// <see cref="EntryUV"/> ∈ [0,1]² indicates where the ray entered the target surface
    /// in its own local frame — naturally rejects the staircase mismatch (Decision 4)
    /// because a hit whose UV is outside [0,1] means the ray pierced the surface outside
    /// its visible bounds.
    /// </summary>
    public readonly struct RayBoxHit2D
    {
        public readonly bool DidHit;
        public readonly float Distance;     // ray t (positive only)
        public readonly Vector2 WorldPoint; // hit point in meters
        public readonly Vector2 EntryUV;    // UV ∈ [0,1]², or out-of-range if miss

        public RayBoxHit2D(bool didHit, float distance, Vector2 worldPoint, Vector2 entryUV)
        {
            DidHit = didHit;
            Distance = distance;
            WorldPoint = worldPoint;
            EntryUV = entryUV;
        }

        public static RayBoxHit2D Miss => new RayBoxHit2D(false, float.PositiveInfinity, Vector2.zero, new Vector2(-1f, -1f));
    }

    /// <summary>
    /// Pure 2D slab-test for ray vs axis-aligned bounding box in meters.
    /// Unit tests in <c>Assets/VR-Workspace/Tests/EditMode/Input/</c>.
    ///
    /// Staircase constraint (Decision 4) is enforced automatically by the UV-range check
    /// at the call site (cursor at Main-bottom X=±0.75 projected at Taskbar produces an
    /// entryUV whose X lies outside [0,1], so the caller rejects it).
    /// </summary>
    public static class RayBoxIntersect2D
    {
        /// <summary>
        /// Intersect a ray with an AABB.
        /// </summary>
        /// <param name="origin">Ray origin in meters (e.g. cursor world-point at edge).</param>
        /// <param name="direction">Ray direction (any non-zero length; need not be normalized).</param>
        /// <param name="bounds">Target AABB in meters.</param>
        /// <param name="bufferZone">
        /// Optional inward shrink in the range [0, 0.1] = 0..10% of each dimension.
        /// When &gt; 0 the effective rect tested is smaller by that fraction on every side,
        /// providing the "sticky edge" anti-jitter behavior (Decision 3).
        /// </param>
        public static RayBoxHit2D Intersect(
            Vector2 origin,
            Vector2 direction,
            Rect bounds,
            float bufferZone = 0f)
        {
            Rect originalBounds = bounds;

            // Shrink bounds by bufferZone (sticky edge) to define hit detection bounds
            Rect hitBounds = bounds;
            if (bufferZone > 0f)
            {
                float bx = bounds.width  * bufferZone;
                float by = bounds.height * bufferZone;
                hitBounds = new Rect(
                    bounds.xMin + bx,
                    bounds.yMin + by,
                    Mathf.Max(0f, bounds.width  - 2f * bx),
                    Mathf.Max(0f, bounds.height - 2f * by));
            }

            // Normalize direction lazily. dx/dy of 0 would cause div-by-zero later.
            if (direction.sqrMagnitude < 1e-12f) return RayBoxHit2D.Miss;
            Vector2 d = direction.normalized;

            // Slab test (Kay-Kajiya). Compute t intervals for each axis.
            // t ranges where the ray is inside the slab of hitBounds.
            float tMinHit = float.NegativeInfinity;
            float tMaxHit = float.PositiveInfinity;

            if (Mathf.Abs(d.x) < 1e-8f)
            {
                // Ray parallel to Y axis. If origin outside X slab, miss.
                if (origin.x < hitBounds.xMin || origin.x > hitBounds.xMax) return RayBoxHit2D.Miss;
            }
            else
            {
                float inv = 1f / d.x;
                float t1 = (hitBounds.xMin - origin.x) * inv;
                float t2 = (hitBounds.xMax - origin.x) * inv;
                if (t1 > t2) (t1, t2) = (t2, t1);
                if (t1 > tMinHit) tMinHit = t1;
                if (t2 < tMaxHit) tMaxHit = t2;
                if (tMinHit > tMaxHit) return RayBoxHit2D.Miss;
            }

            if (Mathf.Abs(d.y) < 1e-8f)
            {
                if (origin.y < hitBounds.yMin || origin.y > hitBounds.yMax) return RayBoxHit2D.Miss;
            }
            else
            {
                float inv = 1f / d.y;
                float t1 = (hitBounds.yMin - origin.y) * inv;
                float t2 = (hitBounds.yMax - origin.y) * inv;
                if (t1 > t2) (t1, t2) = (t2, t1);
                if (t1 > tMinHit) tMinHit = t1;
                if (t2 < tMaxHit) tMaxHit = t2;
                if (tMinHit > tMaxHit) return RayBoxHit2D.Miss;
            }

            // We hit the detection bounds! Now calculate entry point and entryUV on ORIGINAL bounds.
            float tMinOrig = float.NegativeInfinity;
            float tMaxOrig = float.PositiveInfinity;

            if (Mathf.Abs(d.x) >= 1e-8f)
            {
                float inv = 1f / d.x;
                float t1 = (originalBounds.xMin - origin.x) * inv;
                float t2 = (originalBounds.xMax - origin.x) * inv;
                if (t1 > t2) (t1, t2) = (t2, t1);
                if (t1 > tMinOrig) tMinOrig = t1;
                if (t2 < tMaxOrig) tMaxOrig = t2;
            }

            if (Mathf.Abs(d.y) >= 1e-8f)
            {
                float inv = 1f / d.y;
                float t1 = (originalBounds.yMin - origin.y) * inv;
                float t2 = (originalBounds.yMax - origin.y) * inv;
                if (t1 > t2) (t1, t2) = (t2, t1);
                if (t1 > tMinOrig) tMinOrig = t1;
                if (t2 < tMaxOrig) tMaxOrig = t2;
            }

            // Hit. Choose entry t (tMinOrig if origin is outside box, else 0).
            float tEnter = tMinOrig > 0f ? tMinOrig : 0f;
            Vector2 worldPoint = origin + d * tEnter;
            Vector2 entryUV = new Vector2(
                (worldPoint.x - originalBounds.xMin) / Mathf.Max(1e-6f, originalBounds.width),
                (worldPoint.y - originalBounds.yMin) / Mathf.Max(1e-6f, originalBounds.height));

            // Staircase rejection: an entryUV whose component is outside [0,1] means the
            // hit lies outside the surface's actual bounds.
            if (entryUV.x < -1e-4f || entryUV.x > 1f + 1e-4f ||
                entryUV.y < -1e-4f || entryUV.y > 1f + 1e-4f)
                return RayBoxHit2D.Miss;

            return new RayBoxHit2D(true, tEnter, worldPoint, entryUV);
        }
    }
}
