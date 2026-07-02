using System;
using UnityEngine;
using VRWorkspace.Domain.Input.Geometry;

namespace VRWorkspace.Domain.Input
{
    /// <summary>
    /// Singleton arbiter for Virtual Cursor Space (VCS).
    /// Owns the surface registry + cursor state; mediates edge traversal and click dispatch.
    ///
    /// Singleton lifecycle follows the project convention
    /// (see <c>VRGazeReticle.ResetStaticInstance</c>, <c>RTTRaycastManager.ResetStaticState</c>):
    /// one static instance per Play session, reset via <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c>.
    ///
    /// Unit: meters (Decision 7). All bounds, centers, and UV spans are in the same
    /// coordinate space as <see cref="RTTCanvasBase.worldWidth"/>/<see cref="RTTCanvasBase.worldHeight"/>.
    /// </summary>
    public sealed class VirtualCursorSpace
    {
        // ----- Singleton -----
        private static VirtualCursorSpace _instance;
        public static VirtualCursorSpace Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStaticState()
        {
            _instance = null;
        }

        public static VirtualCursorSpace GetOrCreate()
        {
            if (_instance == null) _instance = new VirtualCursorSpace();
            return _instance;
        }

        // ----- Storage -----
        private readonly SurfaceRegistry _surfaces = new();
        public SurfaceRegistry Surfaces => _surfaces;
        public InputConfig Config = InputConfig.Default;

        // ----- Cursor state -----
        public CursorState Cursor { get; private set; } = CursorState.Inactive(InputMode.Gaze);

        // ----- Events -----
        public event Action<VirtualSurface> OnSurfaceRegistered;
        public event Action<Guid>            OnSurfaceUnregistered;
        public event Action<Guid, bool>      OnSurfaceVisibilityChanged;
        public event Action<CursorState>     OnCursorMoved;
        public event Action<Guid, EdgeDirection, Vector2> OnCursorEdgeBounced; // (sourceSurfaceId, dir, attemptedWorldPoint)
        public event Action<CursorState>     OnClickDispatched;
        public event Action<InputMode>       OnInputModeChanged;

        private VirtualCursorSpace() { }

        // ----- Surface lifecycle -----

        public void RegisterSurface(VirtualSurface surface)
        {
            if (surface == null) return;
            _surfaces.Register(surface);
            OnSurfaceRegistered?.Invoke(surface);

            // If cursor was inactive and we just got a visible surface, snap to its center (Decision 1).
            if (!Cursor.SurfaceId.HasValue && surface.IsVisible && Cursor.Mode != InputMode.Gaze)
            {
                SnapCursorTo(surface.SurfaceId);
            }
        }

        public bool UnregisterSurface(Guid id)
        {
            // Capture cursor's world position BEFORE removing the surface (which drops bounds info).
            Vector2 cursorWorldPoint;
            if (Cursor.SurfaceId == id && _surfaces.TryGet(id, out var oldSurface))
            {
                cursorWorldPoint = oldSurface.Center + (Cursor.UV - new Vector2(0.5f, 0.5f)) * oldSurface.Size;
            }
            else
            {
                cursorWorldPoint = Vector2.zero;
            }

            if (_surfaces.Unregister(id))
            {
                if (Cursor.SurfaceId == id)
                {
                    var fallback = _surfaces.GetHighestPriorityVisible();
                    if (fallback != null)
                    {
                        var edgeUV = PickNearestEdgeUV(fallback, cursorWorldPoint);
                        SetCursor(fallback.SurfaceId, edgeUV);
                    }
                    else
                    {
                        SetCursor(null, Vector2.zero);
                    }
                }
                OnSurfaceUnregistered?.Invoke(id);
                return true;
            }
            return false;
        }

        public void NotifySurfaceVisibilityChanged(Guid id, bool isVisible)
        {
            if (!_surfaces.SetVisibility(id, isVisible)) return;
            OnSurfaceVisibilityChanged?.Invoke(id, isVisible);
        }

        // ----- Cursor mutation -----

        /// <summary>
        /// Apply a UV-space delta with automatic edge traversal.
        /// When the cursor overflows [0,1] on any axis, we attempt to traverse
        /// to an adjacent visible surface via <see cref="TryTraverseEdge"/>.
        /// The staircase constraint (Decision 4) is enforced by RayBoxIntersect2D:
        /// only regions sharing X (or Y) coordinates allow the cursor to cross.
        /// </summary>
        public void MoveCursor(Vector2 deltaUV)
        {
            if (!Cursor.SurfaceId.HasValue) return;
            if (!_surfaces.TryGet(Cursor.SurfaceId.Value, out var current)) return;

            Vector2 nextUV = Cursor.UV + deltaUV;

            // Detect overflow on each axis
            float yOverflow = nextUV.y < 0f ? -nextUV.y : (nextUV.y > 1f ? nextUV.y - 1f : 0f);
            float xOverflow = nextUV.x < 0f ? -nextUV.x : (nextUV.x > 1f ? nextUV.x - 1f : 0f);

            bool traversed = false;
            Guid targetId = default;
            Vector2 entryUV = Vector2.zero;
            EdgeDirection traversedDir = EdgeDirection.Down;

            if (yOverflow > 0f || xOverflow > 0f)
            {
                // Check dominant overflow axis first (vertical for Main↔Taskbar/Pagination)
                if (yOverflow >= xOverflow)
                {
                    EdgeDirection yDir = nextUV.y < 0f ? EdgeDirection.Down : EdgeDirection.Up;
                    if (TryTraverseEdge(yDir, out targetId, out entryUV))
                    {
                        traversed = true;
                        traversedDir = yDir;
                    }
                    else if (xOverflow > 0f)
                    {
                        EdgeDirection xDir = nextUV.x < 0f ? EdgeDirection.Left : EdgeDirection.Right;
                        if (TryTraverseEdge(xDir, out targetId, out entryUV))
                        {
                            traversed = true;
                            traversedDir = xDir;
                        }
                    }
                }
                else
                {
                    EdgeDirection xDir = nextUV.x < 0f ? EdgeDirection.Left : EdgeDirection.Right;
                    if (TryTraverseEdge(xDir, out targetId, out entryUV))
                    {
                        traversed = true;
                        traversedDir = xDir;
                    }
                    else if (yOverflow > 0f)
                    {
                        EdgeDirection yDir = nextUV.y < 0f ? EdgeDirection.Down : EdgeDirection.Up;
                        if (TryTraverseEdge(yDir, out targetId, out entryUV))
                        {
                            traversed = true;
                            traversedDir = yDir;
                        }
                    }
                }
            }

            if (traversed && _surfaces.TryGet(targetId, out var target))
            {
                // Apply the remaining overflow delta, scaled from source to target surface
                Vector2 finalUV = entryUV;

                if (traversedDir == EdgeDirection.Down || traversedDir == EdgeDirection.Up)
                {
                    // Y axis traversed: scale Y overflow + X frame delta to target coords
                    float yOver = traversedDir == EdgeDirection.Down ? nextUV.y : nextUV.y - 1f;
                    finalUV.y += (yOver * current.Size.y) / Mathf.Max(1e-4f, target.Size.y);
                    finalUV.x += (deltaUV.x * current.Size.x) / Mathf.Max(1e-4f, target.Size.x);
                }
                else
                {
                    // X axis traversed: scale X overflow + Y frame delta to target coords
                    float xOver = traversedDir == EdgeDirection.Left ? nextUV.x : nextUV.x - 1f;
                    finalUV.x += (xOver * current.Size.x) / Mathf.Max(1e-4f, target.Size.x);
                    finalUV.y += (deltaUV.y * current.Size.y) / Mathf.Max(1e-4f, target.Size.y);
                }

                finalUV.x = Mathf.Clamp01(finalUV.x);
                finalUV.y = Mathf.Clamp01(finalUV.y);
                SetCursor(targetId, finalUV);
            }
            else
            {
                // No traversal — clamp within current surface
                nextUV.x = Mathf.Clamp01(nextUV.x);
                nextUV.y = Mathf.Clamp01(nextUV.y);
                SetCursor(current.SurfaceId, nextUV);
            }
        }

        public void SetCursorUV(Vector2 uv)
        {
            if (!Cursor.SurfaceId.HasValue) return;
            SetCursor(Cursor.SurfaceId, uv);
        }

        public void SnapCursorToCenter()
        {
            var s = _surfaces.GetHighestPriorityVisible();
            if (s != null) SnapCursorTo(s.SurfaceId);
        }

        public void SnapCursorTo(Guid surfaceId, bool preserveWorldPosition = false)
        {
            if (preserveWorldPosition && Cursor.SurfaceId.HasValue 
                && _surfaces.TryGet(Cursor.SurfaceId.Value, out var current) 
                && _surfaces.TryGet(surfaceId, out var target))
            {
                Vector2 worldPos = current.Center + (Cursor.UV - new Vector2(0.5f, 0.5f)) * current.Size;
                float u = (worldPos.x - target.Center.x) / Mathf.Max(1e-4f, target.Size.x) + 0.5f;
                float v = (worldPos.y - target.Center.y) / Mathf.Max(1e-4f, target.Size.y) + 0.5f;
                SetCursor(surfaceId, new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v)));
            }
            else
            {
                SetCursor(surfaceId, new Vector2(0.5f, 0.5f));
            }
        }

        private void SetCursor(Guid? surfaceId, Vector2 uv)
        {
            var newState = new CursorState(surfaceId, uv, Cursor.Mode, surfaceId.HasValue);
            Cursor = newState;
            OnCursorMoved?.Invoke(newState);
        }

        // ----- Edge traversal (vector projection) -----

        /// <summary>
        /// Try to traverse from the cursor's current surface, through <paramref name="dir"/>,
        /// to a visible surface that accepts entries from that direction.
        /// Returns false → caller should keep cursor on current surface and can fire a wall bounce.
        /// </summary>
        public bool TryTraverseEdge(EdgeDirection dir, out Guid targetSurfaceId, out Vector2 entryUV)
        {
            targetSurfaceId = Guid.Empty;
            entryUV = new Vector2(0.5f, 0.5f);

            if (!Cursor.SurfaceId.HasValue) return false;
            if (!_surfaces.TryGet(Cursor.SurfaceId.Value, out var source)) return false;
            if (!source.Allows(dir))
            {
                OnCursorEdgeBounced?.Invoke(source.SurfaceId, dir, source.Center);
                return false;
            }

            Vector2 exitPoint = SourceEdgePointFromCursor(source, Cursor.UV, dir);
            Vector2 rayDir    = EdgeOutwardNormal(dir);
            float bufferZone  = Mathf.Clamp(Config.StickyEdgeBuffer, 0f, 0.1f);

            VirtualSurface bestTarget = null;
            float bestDistance = float.PositiveInfinity;
            Vector2 bestEntryUV = Vector2.zero;

            foreach (var candidate in _surfaces.All)
            {
                if (candidate.SurfaceId == source.SurfaceId) continue;
                if (!candidate.IsVisible) continue;

                // Target must explicitly allow entries from this direction.
                EdgeDirection opposite = Opposite(dir);
                if (!candidate.Allows(opposite)) continue;

                var hit = RayBoxIntersect2D.Intersect(exitPoint, rayDir, candidate.Bounds, bufferZone);
                if (!hit.DidHit) continue;

                if (hit.Distance < bestDistance)
                {
                    bestDistance = hit.Distance;
                    bestTarget   = candidate;
                    bestEntryUV  = hit.EntryUV;
                }
            }

            if (bestTarget == null)
            {
                OnCursorEdgeBounced?.Invoke(source.SurfaceId, dir, source.Center);
                return false;
            }

            targetSurfaceId = bestTarget.SurfaceId;
            entryUV = bestEntryUV;
            return true;
        }

        // ----- Mode switching -----

        public void RequestModeSwitch(InputMode newMode)
        {
            if (Cursor.Mode == newMode) return;
            Cursor = new CursorState(Cursor.SurfaceId, Cursor.UV, newMode, Cursor.IsVisible && newMode != InputMode.Gaze);
            OnInputModeChanged?.Invoke(newMode);
        }

        public InputMode Mode => Cursor.Mode;

        // ----- Click dispatch -----

        public void RaiseClick()
        {
            if (!Cursor.SurfaceId.HasValue) return;
            OnClickDispatched?.Invoke(Cursor);
        }

        // ----- Internal helpers -----

        /// <summary>
        /// Where the cursor's UV projects onto the source surface's edge in <paramref name="dir"/>.
        /// This is what makes the staircase constraint (Decision 4) work: a cursor near a corner exits
        /// from the corner, and a target whose X range doesn't cover that exit X is naturally missed.
        /// </summary>
        private static Vector2 SourceEdgePointFromCursor(VirtualSurface s, Vector2 uv, EdgeDirection dir)
        {
            Vector2 c = s.Center;
            Vector2 half = s.Size * 0.5f;
            return dir switch
            {
                EdgeDirection.Down  => new Vector2(c.x + (uv.x - 0.5f) * s.Size.x, c.y - half.y),
                EdgeDirection.Up    => new Vector2(c.x + (uv.x - 0.5f) * s.Size.x, c.y + half.y),
                EdgeDirection.Left  => new Vector2(c.x - half.x, c.y + (uv.y - 0.5f) * s.Size.y),
                EdgeDirection.Right => new Vector2(c.x + half.x, c.y + (uv.y - 0.5f) * s.Size.y),
                _ => c
            };
        }

        private static Vector2 EdgeOutwardNormal(EdgeDirection dir) => dir switch
        {
            EdgeDirection.Down  => Vector2.down,
            EdgeDirection.Up    => Vector2.up,
            EdgeDirection.Left  => Vector2.left,
            EdgeDirection.Right => Vector2.right,
            _ => Vector2.zero
        };

        private static EdgeDirection Opposite(EdgeDirection d) => d switch
        {
            EdgeDirection.Down  => EdgeDirection.Up,
            EdgeDirection.Up    => EdgeDirection.Down,
            EdgeDirection.Left  => EdgeDirection.Right,
            EdgeDirection.Right => EdgeDirection.Left,
            _ => d
        };

        private static Vector2 PickNearestEdgeUV(VirtualSurface s, Vector2 fromPoint)
        {
            // Map a world point onto the surface's nearest edge in [0,1] UV.
            var b = s.Bounds;
            float u = Mathf.InverseLerp(b.xMin, b.xMax, fromPoint.x);
            float v = Mathf.InverseLerp(b.yMin, b.yMax, fromPoint.y);
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
            return new Vector2(u, v);
        }
    }
}
