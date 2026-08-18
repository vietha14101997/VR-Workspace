using NUnit.Framework;
using UnityEngine;
using VRWorkspace.Domain.Input.Geometry;

namespace VRWorkspace.Tests.EditMode.Input
{
    public class RayBoxIntersect2DTests
    {
        private const float Tol = 1e-3f;

        [Test]
        public void Intersect_RayFromLeft_HitsBoxAtLeftEdge()
        {
            // Ray from (-3,0) going right enters the box at its LEFT edge (x=-1) — UV.x=0, not 0.5.
            // (Renamed from earlier "_HitsBoxAtMidpoint" expectation that misread the geometry.)
            var bounds = new Rect(-1, -1, 2, 2);
            var hit = RayBoxIntersect2D.Intersect(new Vector2(-3, 0), Vector2.right, bounds);
            Assert.IsTrue(hit.DidHit);
            Assert.AreEqual(0f, hit.EntryUV.x, Tol);
            Assert.AreEqual(0.5f, hit.EntryUV.y, Tol);
        }

        [Test]
        public void Intersect_RayFromBelow_HitsBoxAtBottomEdge()
        {
            // Ray from (0,-3) going up enters at BOTTOM edge (y=-1) — UV.y=0, not 0.5.
            var bounds = new Rect(-1, -1, 2, 2);
            var hit = RayBoxIntersect2D.Intersect(new Vector2(0, -3), Vector2.up, bounds);
            Assert.IsTrue(hit.DidHit);
            Assert.AreEqual(0.5f, hit.EntryUV.x, Tol);
            Assert.AreEqual(0f, hit.EntryUV.y, Tol);
        }

        [Test]
        public void Intersect_RayPointingAway_Misses()
        {
            var bounds = new Rect(-1, -1, 2, 2);
            var hit = RayBoxIntersect2D.Intersect(new Vector2(3, 0), Vector2.right, bounds);
            Assert.IsFalse(hit.DidHit);
        }

        [Test]
        public void Intersect_Staircase_XOutOfRangeRejectsHit()
        {
            var staircaseTaskbar = new Rect(-0.7f, -2.0f, 1.4f, 0.4f);
            var exit = new Vector2(0.75f, -1.0f);
            var hit = RayBoxIntersect2D.Intersect(exit, Vector2.down, staircaseTaskbar);
            Assert.IsFalse(hit.DidHit, "X=0.75 is outside [-0.7,+0.7] taskbar range — staircase miss expected.");
        }

        [Test]
        public void Intersect_Staircase_XInRangeHits()
        {
            var staircaseTaskbar = new Rect(-0.7f, -2.0f, 1.4f, 0.4f);
            var exit = new Vector2(0.5f, -1.0f);
            var hit = RayBoxIntersect2D.Intersect(exit, Vector2.down, staircaseTaskbar);
            Assert.IsTrue(hit.DidHit);
            Assert.That(hit.EntryUV.x, Is.InRange(0f, 1f));
        }

        [Test]
        public void Intersect_StickyBufferShiftsHitPointInward()
        {
            // Buffer doesn't make already-hitting rays miss; it shrinks the hit target,
            // so the same ray from the same origin hits FURTHER from origin (smaller effective box).
            var bounds = new Rect(-1, -1, 2, 2);
            var plain    = RayBoxIntersect2D.Intersect(new Vector2(-3, 0), Vector2.right, bounds, 0f);
            var buffered = RayBoxIntersect2D.Intersect(new Vector2(-3, 0), Vector2.right, bounds, 0.1f);
            Assert.IsTrue(plain.DidHit);
            Assert.IsTrue(buffered.DidHit);
            Assert.Less(plain.Distance, buffered.Distance,
                "Sticky buffer shrinks bounds to [-0.8,0.8]² — same-origin ray should hit later (further).");
        }

        [Test]
        public void Intersect_RayOriginInsideBox_ReturnsZeroT()
        {
            var bounds = new Rect(-1, -1, 2, 2);
            var hit = RayBoxIntersect2D.Intersect(new Vector2(0, 0), Vector2.right, bounds);
            Assert.IsTrue(hit.DidHit);
            Assert.AreEqual(0f, hit.Distance, Tol);
        }

        [Test]
        public void Intersect_ParallelRay_InsideSlab_Hits()
        {
            // Ray parallel to Y, going right — origin inside X slab.
            var bounds = new Rect(-1, -1, 2, 4);
            var hit = RayBoxIntersect2D.Intersect(new Vector2(0, 0), Vector2.up, bounds);
            Assert.IsTrue(hit.DidHit);
        }

        [Test]
        public void Intersect_ParallelRay_OutsideSlab_Misses()
        {
            // Ray parallel to Y, going right — origin outside X slab.
            var bounds = new Rect(-1, -1, 2, 4);
            var hit = RayBoxIntersect2D.Intersect(new Vector2(2, 0), Vector2.up, bounds);
            Assert.IsFalse(hit.DidHit);
        }

        [Test]
        public void Intersect_ZeroDirection_Misses()
        {
            var bounds = new Rect(-1, -1, 2, 2);
            var hit = RayBoxIntersect2D.Intersect(new Vector2(-2, 0), Vector2.zero, bounds);
            Assert.IsFalse(hit.DidHit);
        }
    }
}
