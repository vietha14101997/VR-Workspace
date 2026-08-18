using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_ANDROID || UNITY_EDITOR
using UnityEngine.InputSystem.Android;
#endif
using VRWorkspace.Domain.Input.Tracking;

namespace VRWorkspace.VRInput
{
    /// <summary>
    /// Owns Cardboard tracking-origin correction. The TrackedPoseDriver remains the
    /// sole owner of the camera's local pose; this component only rotates its parent.
    /// </summary>
    public sealed class CardboardTrackingController : MonoBehaviour
    {
        [Header("Tracking")]
        [SerializeField] private Camera trackedCamera;
        [SerializeField] private bool automaticYawCorrection = true;

        [Header("Heading Quality")]
        [SerializeField, Range(5f, 50f)] private float minMagneticField = 20f;
        [SerializeField, Range(50f, 150f)] private float maxMagneticField = 80f;
        [SerializeField, Range(5f, 45f)] private float requiredValidationRotation = 12f;

        [Header("Correction Limits")]
        [SerializeField, Range(0.1f, 5f)] private float stationaryAngularSpeed = 1.5f;
        [SerializeField, Range(0.25f, 5f)] private float stationaryDuration = 1.5f;
        [SerializeField, Range(0.05f, 2f)] private float headingTimeConstant = 0.5f;
        [SerializeField, Range(1f, 15f)] private float maxCorrectionInnovation = 8f;
        [SerializeField, Range(0.05f, 1f)] private float maxCorrectionSpeed = 0.25f;
        [SerializeField, Range(5f, 45f)] private float maxCorrection = 20f;

        private Quaternion _baseLocalRotation;
        private Quaternion _previousRawRotation;
        private YawCorrectionFilter _filter;
        private ScreenOrientation _screenOrientation;
        private bool _hasPreviousRawRotation;
        private bool _isRecentering;
        private bool _started;
        private float _recenterStationaryTime;
        private AttitudeSensor _headingSensor;
        private MagneticFieldSensor _magneticSensor;
        private readonly List<Object> _recenterOwners = new List<Object>();

        public static CardboardTrackingController Instance { get; private set; }
        public bool IsHeadingTrusted => _filter != null && _filter.IsHeadingTrusted;
        public bool IsRecentering => _isRecentering;
        public bool IsRecenterSettled => !_isRecentering || _recenterStationaryTime >= 0.25f;
        public float AppliedYawCorrection => _filter?.AppliedCorrection ?? 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticInstance()
        {
            Instance = null;
        }

        private void Awake()
        {
            Instance = this;
            _baseLocalRotation = transform.localRotation;
            _screenOrientation = Screen.orientation;
            RebuildFilter();
        }

        private void Start()
        {
            _started = true;
            if (trackedCamera == null) trackedCamera = Camera.main;
            if (trackedCamera == null)
            {
                Debug.LogError("[CardboardTrackingController] Main camera not found.", this);
                enabled = false;
                return;
            }

            if (trackedCamera.transform.parent != transform)
            {
                Debug.LogError(
                    "[CardboardTrackingController] Controller must be on the tracked camera parent.",
                    this);
                enabled = false;
                return;
            }

            if (automaticYawCorrection) EnableHeadingSensors();
        }

        private void LateUpdate()
        {
            if (trackedCamera == null) return;

            CleanupRecenterOwners();

            Quaternion rawRotation = trackedCamera.transform.localRotation;
            float deltaTime = Time.unscaledDeltaTime;

            if (_isRecentering)
            {
                ApplyCorrection(_filter.AppliedCorrection);
                UpdateRecenterStability(rawRotation);
                _previousRawRotation = rawRotation;
                _hasPreviousRawRotation = true;
                return;
            }

            if (_screenOrientation != Screen.orientation)
            {
                _screenOrientation = Screen.orientation;
                InvalidateHeadingReference();
            }

            if (!_hasPreviousRawRotation || deltaTime <= 0f)
            {
                _previousRawRotation = rawRotation;
                _hasPreviousRawRotation = true;
                return;
            }

            float angularSpeed = HeadTrackingMath.AngularSpeed(
                _previousRawRotation,
                rawRotation,
                deltaTime);

            if (automaticYawCorrection && HeadTrackingMath.TryGetYaw(rawRotation, out float rawYaw))
            {
                bool headingValid = TryReadHeading(out float heading);
                float correction = _filter.Step(
                    rawYaw,
                    heading,
                    angularSpeed,
                    headingValid,
                    deltaTime);
                ApplyCorrection(correction);
            }
            else if (automaticYawCorrection)
            {
                _filter.InvalidateSample();
            }

            _previousRawRotation = rawRotation;
        }

        public bool BeginManualRecenter(Object owner)
        {
            if (owner == null || _recenterOwners.Contains(owner))
                return false;

            _recenterOwners.Add(owner);
            if (_isRecentering) return true;

            _isRecentering = true;
            _recenterStationaryTime = 0f;
            _hasPreviousRawRotation = false;
            float preservedCorrection = _filter.AppliedCorrection;
            _filter.Reset(preservedCorrection);
            ApplyCorrection(preservedCorrection);
            return true;
        }

        public void CompleteManualRecenter(Object owner)
        {
            if (!_isRecentering || owner == null || !_recenterOwners.Remove(owner)) return;
            if (_recenterOwners.Count == 0) FinishManualRecenter();
        }

        public void CancelManualRecenter(Object owner)
        {
            CompleteManualRecenter(owner);
        }

        private bool TryReadHeading(out float heading)
        {
            heading = 0f;
            AttitudeSensor attitudeSensor = ResolveAbsoluteHeadingSensor();
            MagneticFieldSensor magneticSensor = InputSystem.GetDevice<MagneticFieldSensor>();
            if (attitudeSensor == null || magneticSensor == null) return false;

            if (attitudeSensor != _headingSensor || magneticSensor != _magneticSensor)
            {
                _headingSensor = attitudeSensor;
                _magneticSensor = magneticSensor;
                _filter.Reset(_filter.AppliedCorrection);
                _hasPreviousRawRotation = false;
                return false;
            }

            if (!attitudeSensor.enabled) InputSystem.EnableDevice(attitudeSensor);
            if (!magneticSensor.enabled) InputSystem.EnableDevice(magneticSensor);

            Quaternion attitude = attitudeSensor.attitude.ReadValue();
            float fieldStrength = magneticSensor.magneticField.ReadValue().magnitude;
            return IsFinite(fieldStrength) &&
                   fieldStrength >= minMagneticField && fieldStrength <= maxMagneticField &&
                   HeadTrackingMath.TryGetYaw(attitude, out heading);
        }

        private void UpdateRecenterStability(Quaternion rawRotation)
        {
            float deltaTime = Time.unscaledDeltaTime;
            if (!_hasPreviousRawRotation || deltaTime <= 0f)
            {
                _recenterStationaryTime = 0f;
                return;
            }

            float angularSpeed = HeadTrackingMath.AngularSpeed(
                _previousRawRotation,
                rawRotation,
                deltaTime);
            if (angularSpeed <= stationaryAngularSpeed)
                _recenterStationaryTime += deltaTime;
            else
                _recenterStationaryTime = 0f;
        }

        private void ApplyCorrection(float correction)
        {
            transform.localRotation =
                Quaternion.AngleAxis(correction, Vector3.up) * _baseLocalRotation;
        }

        private void InvalidateHeadingReference()
        {
            _filter.Reset(_filter.AppliedCorrection);
            _hasPreviousRawRotation = false;
            if (automaticYawCorrection) EnableHeadingSensors();
        }

        private void EnableHeadingSensors()
        {
            _headingSensor = ResolveAbsoluteHeadingSensor();
            _magneticSensor = InputSystem.GetDevice<MagneticFieldSensor>();
            if (_headingSensor != null && !_headingSensor.enabled)
                InputSystem.EnableDevice(_headingSensor);
            if (_magneticSensor != null && !_magneticSensor.enabled)
                InputSystem.EnableDevice(_magneticSensor);
        }

        private static AttitudeSensor ResolveAbsoluteHeadingSensor()
        {
#if UNITY_ANDROID || UNITY_EDITOR
            return InputSystem.GetDevice<AndroidRotationVector>();
#else
            return null;
#endif
        }

        private void CleanupRecenterOwners()
        {
            if (!_isRecentering || _recenterOwners.Count == 0) return;

            for (int i = _recenterOwners.Count - 1; i >= 0; i--)
            {
                if (!IsOwnerActive(_recenterOwners[i])) _recenterOwners.RemoveAt(i);
            }

            if (_recenterOwners.Count == 0)
            {
                FinishManualRecenter();
                VRGazeReticle.Instance?.ExitRecenterMode();
            }
        }

        private void FinishManualRecenter()
        {
            _isRecentering = false;
            _recenterStationaryTime = 0f;
            _hasPreviousRawRotation = false;
            float preservedCorrection = _filter.AppliedCorrection;
            _filter.Reset(preservedCorrection);
            ApplyCorrection(preservedCorrection);
        }

        private static bool IsOwnerActive(Object owner)
        {
            if (owner == null) return false;
            if (owner is Behaviour behaviour) return behaviour.isActiveAndEnabled;
            if (owner is Component component) return component.gameObject.activeInHierarchy;
            if (owner is GameObject gameObject) return gameObject.activeInHierarchy;
            return true;
        }

        private void RebuildFilter()
        {
            _filter = new YawCorrectionFilter(new YawCorrectionSettings(
                stationaryAngularSpeed,
                stationaryDuration,
                headingTimeConstant,
                requiredValidationRotation,
                maxCorrectionInnovation,
                maxCorrectionSpeed,
                maxCorrection));
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) InvalidateHeadingReference();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) InvalidateHeadingReference();
        }

        private void OnEnable()
        {
            if (_filter == null) return;
            _filter.Reset();
            _hasPreviousRawRotation = false;
            if (_started && automaticYawCorrection) EnableHeadingSensors();
        }

        private void OnDisable()
        {
            _recenterOwners.Clear();
            _isRecentering = false;
            _recenterStationaryTime = 0f;
            _hasPreviousRawRotation = false;
            if (_filter != null) _filter.Reset();
            ApplyCorrection(0f);
            VRGazeReticle.Instance?.ExitRecenterMode();
        }

        private void OnDestroy()
        {
            _recenterOwners.Clear();
            if (Instance == this) Instance = null;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
