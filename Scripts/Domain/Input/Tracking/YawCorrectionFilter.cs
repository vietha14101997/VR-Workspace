using UnityEngine;

namespace VRWorkspace.Domain.Input.Tracking
{
    public readonly struct YawCorrectionSettings
    {
        public readonly float StationaryAngularSpeed;
        public readonly float StationaryDuration;
        public readonly float HeadingTimeConstant;
        public readonly float RequiredValidationRotation;
        public readonly float MaxCorrectionInnovation;
        public readonly float MaxCorrectionSpeed;
        public readonly float MaxCorrection;

        public YawCorrectionSettings(
            float stationaryAngularSpeed,
            float stationaryDuration,
            float headingTimeConstant,
            float requiredValidationRotation,
            float maxCorrectionInnovation,
            float maxCorrectionSpeed,
            float maxCorrection)
        {
            StationaryAngularSpeed = Mathf.Max(0.01f, stationaryAngularSpeed);
            StationaryDuration = Mathf.Max(0f, stationaryDuration);
            HeadingTimeConstant = Mathf.Max(0.01f, headingTimeConstant);
            RequiredValidationRotation = Mathf.Max(1f, requiredValidationRotation);
            MaxCorrectionInnovation = Mathf.Max(0.1f, maxCorrectionInnovation);
            MaxCorrectionSpeed = Mathf.Max(0.01f, maxCorrectionSpeed);
            MaxCorrection = Mathf.Clamp(maxCorrection, 1f, 45f);
        }
    }

    /// <summary>
    /// Low-frequency yaw correction estimator. A heading source is trusted only after
    /// it follows a real head turn, preventing an unavailable or axis-mismatched compass
    /// from rotating the VR world.
    /// </summary>
    public sealed class YawCorrectionFilter
    {
        private readonly YawCorrectionSettings _settings;

        private bool _hasReference;
        private bool _hasPreviousSample;
        private bool _headingTrusted;
        private float _filteredHeading;
        private float _referenceOffset;
        private float _targetCorrection;
        private float _appliedCorrection;
        private float _stationaryTime;
        private float _previousRawYaw;
        private float _previousHeading;
        private float _validationRawDelta;
        private float _validationHeadingDelta;

        public YawCorrectionFilter(YawCorrectionSettings settings)
        {
            _settings = settings;
        }

        public bool IsHeadingTrusted => _headingTrusted;
        public float TargetCorrection => _targetCorrection;
        public float AppliedCorrection => _appliedCorrection;

        public void Reset(float preservedCorrection = 0f)
        {
            _appliedCorrection = Mathf.Clamp(
                preservedCorrection,
                -_settings.MaxCorrection,
                _settings.MaxCorrection);
            _targetCorrection = _appliedCorrection;
            _hasReference = false;
            _hasPreviousSample = false;
            _headingTrusted = false;
            _stationaryTime = 0f;
            ResetValidationWindow();
        }

        public void InvalidateSample()
        {
            _hasPreviousSample = false;
            _headingTrusted = false;
            _stationaryTime = 0f;
            ResetValidationWindow();
        }

        public float Step(
            float rawYaw,
            float heading,
            float angularSpeed,
            bool headingValid,
            float deltaTime)
        {
            if (deltaTime <= 0f || !IsFinite(rawYaw) || !IsFinite(angularSpeed))
                return _appliedCorrection;

            if (!headingValid || !IsFinite(heading))
            {
                InvalidateSample();
                return _appliedCorrection;
            }

            heading = NormalizeAngle(heading);
            bool moving = angularSpeed > _settings.StationaryAngularSpeed;

            if (!_hasReference)
            {
                _filteredHeading = heading;
                _referenceOffset = Mathf.DeltaAngle(
                    heading,
                    rawYaw + _appliedCorrection);
                _hasReference = true;
            }
            else
            {
                float alpha = 1f - Mathf.Exp(-deltaTime / _settings.HeadingTimeConstant);
                _filteredHeading = NormalizeAngle(
                    _filteredHeading + Mathf.DeltaAngle(_filteredHeading, heading) * alpha);
            }

            if (_hasPreviousSample)
            {
                float rawDelta = Mathf.DeltaAngle(_previousRawYaw, rawYaw);
                float headingDelta = Mathf.DeltaAngle(_previousHeading, heading);

                if (moving)
                {
                    _stationaryTime = 0f;
                    _validationRawDelta += rawDelta;
                    _validationHeadingDelta += headingDelta;
                    ValidateHeadingMotionIfReady();
                }
                else
                {
                    _stationaryTime += deltaTime;
                }
            }

            if (!moving && _headingTrusted &&
                _stationaryTime >= _settings.StationaryDuration)
            {
                float expectedCorrectedYaw = NormalizeAngle(
                    _filteredHeading + _referenceOffset);
                float candidate = Mathf.Clamp(
                    Mathf.DeltaAngle(rawYaw, expectedCorrectedYaw),
                    -_settings.MaxCorrection,
                    _settings.MaxCorrection);
                float innovation = Mathf.Abs(
                    Mathf.DeltaAngle(_targetCorrection, candidate));

                if (innovation <= _settings.MaxCorrectionInnovation)
                {
                    _targetCorrection = candidate;
                }
                else
                {
                    // A sudden residual is more likely magnetic interference than gyro drift.
                    _headingTrusted = false;
                    ResetValidationWindow();
                }
            }

            if (!moving)
            {
                _appliedCorrection = Mathf.MoveTowards(
                    _appliedCorrection,
                    _targetCorrection,
                    _settings.MaxCorrectionSpeed * deltaTime);
            }

            _previousRawYaw = rawYaw;
            _previousHeading = heading;
            _hasPreviousSample = true;
            return _appliedCorrection;
        }

        private void ValidateHeadingMotionIfReady()
        {
            if (Mathf.Abs(_validationRawDelta) < _settings.RequiredValidationRotation)
                return;

            float tolerance = Mathf.Max(3f, Mathf.Abs(_validationRawDelta) * 0.35f);
            float disagreement = Mathf.Abs(
                Mathf.DeltaAngle(_validationRawDelta, _validationHeadingDelta));
            _headingTrusted = disagreement <= tolerance;
            ResetValidationWindow();
        }

        private void ResetValidationWindow()
        {
            _validationRawDelta = 0f;
            _validationHeadingDelta = 0f;
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            return angle < 0f ? angle + 360f : angle;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class HeadTrackingMath
    {
        public static bool TryGetYaw(Quaternion rotation, out float yaw)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector2 horizontal = new Vector2(forward.x, forward.z);
            // Yaw becomes numerically unstable when the view approaches vertical.
            if (horizontal.sqrMagnitude < 0.01f)
            {
                yaw = 0f;
                return false;
            }

            yaw = Mathf.Atan2(horizontal.x, horizontal.y) * Mathf.Rad2Deg;
            if (yaw < 0f) yaw += 360f;
            return true;
        }

        public static float AngularSpeed(Quaternion previous, Quaternion current, float deltaTime)
        {
            if (deltaTime <= 0f) return 0f;
            return Quaternion.Angle(previous, current) / deltaTime;
        }
    }
}
