using NUnit.Framework;
using UnityEngine;
using VRWorkspace.Domain.Input.Tracking;

namespace VRWorkspace.Tests.EditMode.Input
{
    public class YawCorrectionFilterTests
    {
        private static YawCorrectionSettings CreateSettings(
            float maxInnovation = 8f,
            float maxSpeed = 1f)
        {
            return new YawCorrectionSettings(
                stationaryAngularSpeed: 1.5f,
                stationaryDuration: 0.5f,
                headingTimeConstant: 0.1f,
                requiredValidationRotation: 12f,
                maxCorrectionInnovation: maxInnovation,
                maxCorrectionSpeed: maxSpeed,
                maxCorrection: 20f);
        }

        [Test]
        public void Step_DoesNotTrustHeadingUntilItFollowsHeadTurn()
        {
            var filter = new YawCorrectionFilter(CreateSettings());

            filter.Step(0f, 100f, 0f, true, 0.1f);
            filter.Step(5f, 100f, 50f, true, 0.1f);
            filter.Step(10f, 100f, 50f, true, 0.1f);
            filter.Step(15f, 100f, 50f, true, 0.1f);

            Assert.IsFalse(filter.IsHeadingTrusted);
            Assert.AreEqual(0f, filter.AppliedCorrection, 1e-4f);
        }

        [Test]
        public void Step_MatchingHeadingBecomesTrustedAcrossAngleWrap()
        {
            var filter = new YawCorrectionFilter(CreateSettings());

            filter.Step(350f, 100f, 0f, true, 0.1f);
            filter.Step(355f, 105f, 50f, true, 0.1f);
            filter.Step(0f, 110f, 50f, true, 0.1f);
            filter.Step(5f, 115f, 50f, true, 0.1f);

            Assert.IsTrue(filter.IsHeadingTrusted);
        }

        [Test]
        public void Step_TrustedHeadingCorrectsSlowStationaryDrift()
        {
            var filter = new YawCorrectionFilter(CreateSettings());
            PrimeTrustedFilter(filter);

            float rawYaw = 15f;
            for (int i = 0; i < 20; i++)
            {
                rawYaw += 0.05f;
                filter.Step(rawYaw, 115f, 0.5f, true, 0.1f);
            }

            Assert.Less(filter.TargetCorrection, -0.5f);
            Assert.Less(filter.AppliedCorrection, -0.4f);
            Assert.GreaterOrEqual(filter.AppliedCorrection, -1.1f);
        }

        [Test]
        public void Step_SuddenHeadingResidualRevokesTrustWithoutSnap()
        {
            var filter = new YawCorrectionFilter(CreateSettings(maxInnovation: 2f));
            PrimeTrustedFilter(filter);

            for (int i = 0; i < 8; i++)
                filter.Step(15f, 145f, 0f, true, 0.1f);

            Assert.IsFalse(filter.IsHeadingTrusted);
            Assert.AreEqual(0f, filter.AppliedCorrection, 1e-4f);
        }

        [Test]
        public void Reset_PreservesAppliedCorrectionButRequiresNewValidation()
        {
            var filter = new YawCorrectionFilter(CreateSettings());

            filter.Reset(4f);
            float correction = filter.Step(20f, 120f, 0f, true, 0.1f);

            Assert.AreEqual(4f, correction, 1e-4f);
            Assert.IsFalse(filter.IsHeadingTrusted);
        }

        [Test]
        public void InvalidateSample_RevokesTrustUntilHeadingIsValidatedAgain()
        {
            var filter = new YawCorrectionFilter(CreateSettings());
            PrimeTrustedFilter(filter);

            filter.InvalidateSample();
            filter.Step(30f, 130f, 0f, true, 0.1f);

            Assert.IsFalse(filter.IsHeadingTrusted);
            Assert.AreEqual(0f, filter.AppliedCorrection, 1e-4f);
        }

        [Test]
        public void TryGetYaw_UsesProjectedForwardWithPitch()
        {
            Quaternion rotation =
                Quaternion.AngleAxis(45f, Vector3.up) *
                Quaternion.AngleAxis(80f, Vector3.right);

            bool valid = HeadTrackingMath.TryGetYaw(rotation, out float yaw);

            Assert.IsTrue(valid);
            Assert.AreEqual(45f, yaw, 1e-3f);
        }

        [Test]
        public void TryGetYaw_LookingStraightUpIsInvalid()
        {
            bool valid = HeadTrackingMath.TryGetYaw(
                Quaternion.AngleAxis(-90f, Vector3.right),
                out _);

            Assert.IsFalse(valid);
        }

        [Test]
        public void TryGetYaw_NearVerticalViewIsInvalid()
        {
            bool valid = HeadTrackingMath.TryGetYaw(
                Quaternion.AngleAxis(-88f, Vector3.right),
                out _);

            Assert.IsFalse(valid);
        }

        [Test]
        public void AngularSpeed_UsesQuaternionShortestAngle()
        {
            Quaternion current = Quaternion.AngleAxis(10f, Vector3.up);

            float speed = HeadTrackingMath.AngularSpeed(
                Quaternion.identity,
                current,
                0.5f);

            Assert.AreEqual(20f, speed, 1e-3f);
        }

        private static void PrimeTrustedFilter(YawCorrectionFilter filter)
        {
            filter.Step(0f, 100f, 0f, true, 0.1f);
            filter.Step(5f, 105f, 50f, true, 0.1f);
            filter.Step(10f, 110f, 50f, true, 0.1f);
            filter.Step(15f, 115f, 50f, true, 0.1f);
            Assert.IsTrue(filter.IsHeadingTrusted);
        }
    }
}
