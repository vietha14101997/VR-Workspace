using System.Collections;
using UnityEngine;

namespace VRWorkspace.Bootstrap
{
    /// <summary>
    /// Gate that signals when the VR camera + tracking system has settled and is
    /// ready for UI initialization. Used at app startup to ensure VirtualObjects
    /// spawn at a position that matches the user's actual viewing direction
    /// (not the placeholder pose during XR initialization / splash fade-out).
    ///
    /// Signals checked:
    ///   1. Camera.main exists and is enabled + active
    ///   2. Brief settle frames (allow XR tracking to produce a valid pose)
    ///   3. N consecutive stable frames (camera position delta under threshold)
    /// </summary>
    public static class VRReadyGate
    {
        private const float STABILITY_DISTANCE_THRESHOLD_M = 0.05f;
        private const float STABILITY_ANGLE_THRESHOLD_DEG = 0.5f;
        private const int REQUIRED_STABLE_FRAMES = 5;
        private const int INITIAL_SETTLE_FRAMES = 3;

        public static IEnumerator WaitUntilReady(MonoBehaviour host)
        {
            if (host == null) yield break;

            Camera cam = null;
            while (host != null)
            {
                cam = Camera.main;
                if (cam != null && cam.enabled && cam.gameObject.activeInHierarchy)
                    break;
                yield return null;
            }
            if (host == null) yield break;

            for (int i = 0; i < INITIAL_SETTLE_FRAMES && host != null; i++)
                yield return null;
            if (host == null) yield break;

            int stableFrames = 0;
            Vector3 lastPos = cam.transform.position;
            Quaternion lastRotation = cam.transform.rotation;
            while (host != null && stableFrames < REQUIRED_STABLE_FRAMES)
            {
                yield return null;
                Camera current = Camera.main;
                if (current == null || !current.enabled)
                {
                    stableFrames = 0;
                    continue;
                }

                Vector3 curPos = current.transform.position;
                float dist = Vector3.Distance(curPos, lastPos);
                Quaternion curRotation = current.transform.rotation;
                float angle = Quaternion.Angle(curRotation, lastRotation);
                if (dist < STABILITY_DISTANCE_THRESHOLD_M &&
                    angle < STABILITY_ANGLE_THRESHOLD_DEG)
                    stableFrames++;
                else
                    stableFrames = 0;
                lastPos = curPos;
                lastRotation = curRotation;
            }

            yield return null;
        }
    }
}
