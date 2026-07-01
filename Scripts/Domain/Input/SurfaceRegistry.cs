using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VRWorkspace.Domain.Input
{
    /// <summary>
    /// In-memory registry of all surfaces currently known to the VCS.
    /// Idempotent registration: re-adding a <see cref="Guid"/> updates the surface
    /// instead of throwing — this matches RealityWorld RTT lifecycle where a panel
    /// may release and recreate its RenderTexture (e.g. <c>RTTMobileKeyboard</c>).
    ///
    /// Plain C# — no Unity dependencies on the storage side, so it's EditMode-testable.
    /// </summary>
    public sealed class SurfaceRegistry
    {
        private readonly Dictionary<Guid, VirtualSurface> _surfaces = new();

        /// <summary>Number of registered surfaces (visible + hidden combined).</summary>
        public int Count => _surfaces.Count;

        /// <summary>Insert or replace a surface by its <see cref="VirtualSurface.SurfaceId"/>.</summary>
        public void Register(VirtualSurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            _surfaces[surface.SurfaceId] = surface;
        }

        /// <summary>Remove a surface by id. Returns true if it existed.</summary>
        public bool Unregister(Guid id) => _surfaces.Remove(id);

        /// <summary>Lookup surface by id. Returns false if not registered.</summary>
        public bool TryGet(Guid id, out VirtualSurface surface)
            => _surfaces.TryGetValue(id, out surface);

        /// <summary>Update only the <see cref="VirtualSurface.IsVisible"/> flag for an existing surface.</summary>
        public bool SetVisibility(Guid id, bool isVisible)
        {
            if (!_surfaces.TryGetValue(id, out var s)) return false;
            s.IsVisible = isVisible;
            return true;
        }

        /// <summary>Get all currently-visible surfaces, ordered by priority desc.</summary>
        public IReadOnlyList<VirtualSurface> GetVisibleOrderedByPriority()
        {
            return _surfaces.Values
                .Where(s => s.IsVisible)
                .OrderByDescending(s => s.Priority)
                .ToList();
        }

        /// <summary>Highest-priority visible surface, or null if none visible.</summary>
        public VirtualSurface GetHighestPriorityVisible()
        {
            VirtualSurface best = null;
            foreach (var s in _surfaces.Values)
            {
                if (!s.IsVisible) continue;
                if (best == null || s.Priority > best.Priority) best = s;
            }
            return best;
        }

        /// <summary>Enumerate all surfaces (visible + hidden) — read-only.</summary>
        public IEnumerable<VirtualSurface> All => _surfaces.Values;

        /// <summary>Reset storage. Mainly for tests.</summary>
        public void Clear() => _surfaces.Clear();
    }
}
