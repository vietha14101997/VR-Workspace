using System;
using NUnit.Framework;
using UnityEngine;
using VRWorkspace.Domain.Input;

namespace VRWorkspace.Tests.EditMode.Input
{
    public class VirtualCursorSpaceTests
    {
        private VirtualCursorSpace _vcs;

        [SetUp]
        public void SetUp()
        {
            VirtualCursorSpace.ResetStaticState();
            _vcs = VirtualCursorSpace.GetOrCreate();
            _vcs.Surfaces.Clear();
        }

        private static VirtualSurface MakeSurface(Guid? id = null, Vector2? center = null,
            Vector2? size = null, EdgePolicy edges = EdgePolicy.All, int priority = 100)
        {
            return new VirtualSurface(
                id ?? Guid.NewGuid(),
                center ?? Vector2.zero,
                size ?? new Vector2(1.6f, 0.9f),
                edges,
                priority);
        }

        // ----- Registration -----

        [Test]
        public void Register_Surface_IsIdempotentForSameId()
        {
            var id = Guid.NewGuid();
            _vcs.RegisterSurface(new VirtualSurface(id, Vector2.zero, new Vector2(1.6f, 0.9f), EdgePolicy.All));
            _vcs.RegisterSurface(new VirtualSurface(id, new Vector2(99, 99), new Vector2(1f, 1f), EdgePolicy.Up));

            Assert.AreEqual(1, _vcs.Surfaces.Count, "Register should be idempotent on same id.");
            Assert.IsTrue(_vcs.Surfaces.TryGet(id, out var s));
            Assert.AreEqual(new Vector2(99, 99), s.Center, "Second register should update center (latest wins).");
        }

        [Test]
        public void RegisterSurface_FiresEvent()
        {
            int count = 0;
            VirtualSurface captured = null;
            _vcs.OnSurfaceRegistered += s => { count++; captured = s; };
            var s = MakeSurface();
            _vcs.RegisterSurface(s);
            Assert.AreEqual(1, count);
            Assert.AreSame(s, captured);
        }

        [Test]
        public void UnregisterSurface_ReturnsTrueOnExisting()
        {
            var id = Guid.NewGuid();
            _vcs.RegisterSurface(new VirtualSurface(id, Vector2.zero, new Vector2(1f, 1f), EdgePolicy.All));
            Assert.IsTrue(_vcs.UnregisterSurface(id));
            Assert.IsFalse(_vcs.UnregisterSurface(id));
        }

        // ----- Visibility toggling -----

        [Test]
        public void Visibility_Toggle_FiresEvent()
        {
            var id = Guid.NewGuid();
            _vcs.RegisterSurface(new VirtualSurface(id, Vector2.zero, new Vector2(1f, 1f), EdgePolicy.All));

            bool? seen = null;
            _vcs.OnSurfaceVisibilityChanged += (sid, vis) => { if (sid == id) seen = vis; };
            _vcs.NotifySurfaceVisibilityChanged(id, false);
            Assert.AreEqual(false, seen);
            _vcs.NotifySurfaceVisibilityChanged(id, true);
            Assert.AreEqual(true, seen);
        }

        [Test]
        public void GetHighestPriorityVisible_PicksTop()
        {
            var low = MakeSurface(priority: 50);
            var high = MakeSurface(priority: 200);
            var hidden = MakeSurface(priority: 999);
            _vcs.RegisterSurface(low);
            _vcs.RegisterSurface(high);
            _vcs.RegisterSurface(hidden);
            _vcs.NotifySurfaceVisibilityChanged(hidden.SurfaceId, false);

            var top = _vcs.Surfaces.GetHighestPriorityVisible();
            Assert.AreSame(high, top);
        }

        // ----- Cursor mutation -----

        [Test]
        public void MoveCursor_UpdatesUVByDelta()
        {
            var id = Guid.NewGuid();
            _vcs.RegisterSurface(new VirtualSurface(id, Vector2.zero, new Vector2(2f, 2f), EdgePolicy.None));
            _vcs.SnapCursorTo(id); // binds cursor to surface at center UV (0.5, 0.5)
            _vcs.MoveCursor(new Vector2(0.1f, -0.1f));
            Assert.AreEqual(0.6f, _vcs.Cursor.UV.x, 1e-3f);
            Assert.AreEqual(0.4f, _vcs.Cursor.UV.y, 1e-3f);
            Assert.AreEqual(id, _vcs.Cursor.SurfaceId);
        }

        [Test]
        public void MoveCursor_ClampsInsideSurface()
        {
            var id = Guid.NewGuid();
            _vcs.RegisterSurface(new VirtualSurface(id, Vector2.zero, new Vector2(2f, 2f), EdgePolicy.None));
            _vcs.SnapCursorTo(id);
            _vcs.MoveCursor(new Vector2(2f, 2f));
            // EdgePolicy.None means no traversal; cursor should clamp at (1, 1) on the surface.
            Assert.AreEqual(1f, _vcs.Cursor.UV.x, 1e-3f);
            Assert.AreEqual(1f, _vcs.Cursor.UV.y, 1e-3f);
        }

        // ----- Edge traversal (the core vector-projection algorithm) -----

        [Test]
        public void Traverse_DownMain_FindsTaskbarBelow()
        {
            var mainId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            _vcs.RegisterSurface(new VirtualSurface(mainId, Vector2.zero, new Vector2(1.6f, 0.9f), EdgePolicy.All));
            _vcs.RegisterSurface(new VirtualSurface(taskId, new Vector2(0f, -1.2f), new Vector2(1.4f, 0.2f), EdgePolicy.All));

            _vcs.SnapCursorTo(mainId);
            _vcs.SetCursorUV(new Vector2(0.5f, 0.5f));

            bool traversed = _vcs.TryTraverseEdge(EdgeDirection.Down, out var target, out var entryUV);
            Assert.IsTrue(traversed);
            Assert.AreEqual(taskId, target);
            Assert.That(entryUV.x, Is.InRange(0f, 1f));
            Assert.AreEqual(0.95f, entryUV.y, 1e-3f); // 5% sticky buffer keeps entry just inside the top edge.
        }

        [Test]
        public void Traverse_StaircaseMismatch_WallBounces()
        {
            // Decision 4 — cursor at Main bottom edge X=0.75 (outside Taskbar x range ±0.7).
            var mainId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            _vcs.RegisterSurface(new VirtualSurface(mainId, Vector2.zero, new Vector2(1.6f, 0.9f), EdgePolicy.All));
            _vcs.RegisterSurface(new VirtualSurface(taskId, new Vector2(0f, -1.2f), new Vector2(1.4f, 0.2f), EdgePolicy.All));

            _vcs.SnapCursorTo(mainId);
            _vcs.SetCursorUV(new Vector2(0.95f, 0.0f)); // close to bottom-right corner

            int bounceCount = 0;
            _vcs.OnCursorEdgeBounced += (_, __, ___) => bounceCount++;
            bool traversed = _vcs.TryTraverseEdge(EdgeDirection.Down, out _, out _);

            Assert.IsFalse(traversed, "Staircase mismatch should prevent traversal.");
            Assert.AreEqual(1, bounceCount);
        }

        [Test]
        public void Traverse_SkipsHiddenSurfaces()
        {
            var mainId = Guid.NewGuid();
            var hiddenId = Guid.NewGuid();
            _vcs.RegisterSurface(new VirtualSurface(mainId, Vector2.zero, new Vector2(1.6f, 0.9f), EdgePolicy.All));
            _vcs.RegisterSurface(new VirtualSurface(hiddenId, new Vector2(0f, -1.2f), new Vector2(1.4f, 0.2f), EdgePolicy.All));
            _vcs.NotifySurfaceVisibilityChanged(hiddenId, false);

            _vcs.SnapCursorTo(mainId);
            _vcs.SetCursorUV(new Vector2(0.5f, 0.0f));

            // No visible target below → must wall-bounce
            bool traversed = _vcs.TryTraverseEdge(EdgeDirection.Down, out _, out _);
            Assert.IsFalse(traversed, "Hidden surfaces must be skipped.");
        }

        [Test]
        public void Traverse_SourceDisallowsEdge_RejectsBounce()
        {
            // Source surface explicitly disallows Down → no traversal, no edge bounce either (caller-side check).
            var mainId = Guid.NewGuid();
            _vcs.RegisterSurface(new VirtualSurface(mainId, Vector2.zero, new Vector2(1.6f, 0.9f), EdgePolicy.None));
            _vcs.SnapCursorTo(mainId);

            int bounces = 0;
            _vcs.OnCursorEdgeBounced += (_, __, ___) => bounces++;
            _vcs.TryTraverseEdge(EdgeDirection.Down, out _, out _);
            Assert.AreEqual(1, bounces, "Disallowed-edge rejection is signalled as a bounce.");
        }

        // ----- Mode switching -----

        [Test]
        public void RequestModeSwitch_SameMode_IsNoOp()
        {
            // Initial mode = Gaze. First call transitions to Mouse (fires 1 event),
            // subsequent calls are no-ops.
            int events = 0;
            _vcs.OnInputModeChanged += _ => events++;
            _vcs.RequestModeSwitch(InputMode.Mouse);
            _vcs.RequestModeSwitch(InputMode.Mouse);
            _vcs.RequestModeSwitch(InputMode.Mouse);
            Assert.AreEqual(1, events);
        }

        [Test]
        public void RequestModeSwitch_DifferentMode_FiresEvent()
        {
            int events = 0;
            InputMode? last = null;
            _vcs.OnInputModeChanged += m => { events++; last = m; };
            _vcs.RequestModeSwitch(InputMode.Mouse);
            Assert.AreEqual(1, events);
            Assert.AreEqual(InputMode.Mouse, last);
        }

        // ----- Click dispatch -----

        [Test]
        public void RaiseClick_FiresWithCurrentCursor()
        {
            var id = Guid.NewGuid();
            _vcs.RegisterSurface(new VirtualSurface(id, Vector2.zero, new Vector2(1f, 1f), EdgePolicy.All));
            _vcs.SnapCursorTo(id);

            CursorState? captured = null;
            _vcs.OnClickDispatched += c => captured = c;
            _vcs.RaiseClick();
            Assert.IsTrue(captured.HasValue);
            Assert.AreEqual(id, captured.Value.SurfaceId);
        }
    }
}
