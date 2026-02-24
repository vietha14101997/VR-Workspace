using UnityEngine;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Handles VR recenter and startup camera-follow logic.
    /// Extracted from RTTManager for Single Responsibility.
    /// </summary>
    public class RTTRecenterController
    {
        private bool _startupCameraFollowActive;
        private float _startupFollowStartTime;
        private GameObject _cachedVirtualObjects;
        private Quaternion _initialCameraRotation;
        private bool _cameraTrackingDetected;

        private const float kCameraTrackingAngleThreshold = 1.0f;
        private const float kCameraTrackingFallbackTime = 2.0f;
        private const float kMaxStartupFollowTime = 5.0f;

        public bool IsFollowActive => _startupCameraFollowActive;

        /// <summary>
        /// Start continuous camera-follow at startup.
        /// </summary>
        public void StartStartupCameraFollow()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                Google.XR.Cardboard.Api.Recenter();
                Debug.Log("[RTTRecenterController] Cardboard API Recenter called");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RTTRecenterController] Cardboard Recenter failed: {e.Message}");
            }
#endif
            Camera cam = Camera.main;
            _initialCameraRotation = cam != null ? cam.transform.rotation : Quaternion.identity;
            _cameraTrackingDetected = false;
            _startupCameraFollowActive = true;
            _startupFollowStartTime = Time.time;
            Debug.Log("[RTTRecenterController] Startup camera-follow started");
        }

        /// <summary>
        /// Called from LateUpdate. Returns true when follow should stop.
        /// </summary>
        public bool UpdateCameraFollow(bool mainMenuInitialized)
        {
            if (!_startupCameraFollowActive) return false;

            Camera cam = Camera.main;
            if (cam == null) return false;

            float elapsed = Time.time - _startupFollowStartTime;

            // Detect camera tracking activation
            if (!_cameraTrackingDetected)
            {
                float angleDiff = Quaternion.Angle(_initialCameraRotation, cam.transform.rotation);
                if (angleDiff > kCameraTrackingAngleThreshold)
                {
                    _cameraTrackingDetected = true;
                    Debug.Log($"[RTTRecenterController] Camera tracking detected (delta: {angleDiff:F1}°)");
                }
                else if (elapsed >= kCameraTrackingFallbackTime)
                {
                    _cameraTrackingDetected = true;
                    Debug.Log("[RTTRecenterController] Camera tracking assumed active (fallback timeout)");
                }
            }

            bool shouldUnlock = (_cameraTrackingDetected && mainMenuInitialized)
                             || elapsed >= kMaxStartupFollowTime;

            if (shouldUnlock)
            {
                _startupCameraFollowActive = false;
                RepositionToFaceCamera();
                VirtualObjectsZoomController.Instance?.OnRecenter();
                Debug.Log($"[RTTRecenterController] Startup camera-follow ended after {elapsed:F1}s");
                return true; // Signal: follow complete
            }

            RepositionToFaceCamera();
            return false;
        }

        /// <summary>
        /// Perform instant recenter without animation.
        /// </summary>
        public void PerformInstantRecenter()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                Google.XR.Cardboard.Api.Recenter();
                Debug.Log("[RTTRecenterController] Cardboard API Recenter called");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RTTRecenterController] Cardboard Recenter failed: {e.Message}");
            }
#endif
            RepositionToFaceCamera();
            Debug.Log("[RTTRecenterController] Instant recenter completed");
            VirtualObjectsZoomController.Instance?.OnRecenter();
        }

        /// <summary>
        /// Reposition VirtualObjects children to face camera (horizontal lock).
        /// </summary>
        public void RepositionToFaceCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            if (_cachedVirtualObjects == null)
                _cachedVirtualObjects = GameObject.Find("VirtualObjects");
            if (_cachedVirtualObjects == null) return;

            RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
            if (primary == null) return;

            Vector3 pivotPos = primary.transform.position;
            Quaternion pivotRot = primary.transform.rotation;

            var virtualObjectsTransform = _cachedVirtualObjects.transform;
            int childCount = virtualObjectsTransform.childCount;
            var children = new Transform[childCount];
            var relPositions = new Vector3[childCount];
            var relRotations = new Quaternion[childCount];

            Quaternion invPivotRot = Quaternion.Inverse(pivotRot);
            for (int i = 0; i < childCount; i++)
            {
                Transform child = virtualObjectsTransform.GetChild(i);
                children[i] = child;
                relPositions[i] = invPivotRot * (child.position - pivotPos);
                relRotations[i] = invPivotRot * child.rotation;
            }

            Vector3 camForward = cam.transform.forward;
            camForward.y = 0;
            if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
            camForward.Normalize();

            Vector3 camPos = cam.transform.position;
            float hDist = Vector2.Distance(
                new Vector2(pivotPos.x, pivotPos.z),
                new Vector2(camPos.x, camPos.z));

            Vector3 newPivotPos = camPos + camForward * hDist;
            newPivotPos.y = pivotPos.y;
            Quaternion newPivotRot = Quaternion.LookRotation(camForward);

            for (int i = 0; i < childCount; i++)
            {
                children[i].position = newPivotPos + newPivotRot * relPositions[i];
                children[i].rotation = newPivotRot * relRotations[i];
            }
        }
    }
}
