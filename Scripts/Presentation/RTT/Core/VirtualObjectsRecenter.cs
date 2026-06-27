using System;
using System.Collections;
using UnityEngine;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.VRInput;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Manual recenter of VirtualObjects (the world-space UI cluster) to face the camera.
    /// Shared between RTTTaskbar and RTTRemoteTaskbar so the logic lives in one place.
    /// </summary>
    public static class VirtualObjectsRecenter
    {
        /// <summary>
        /// Run the recenter flow on <paramref name="owner"/>: show the gaze-reticle progress
        /// overlay, then reposition VirtualObjects (with fallback to <paramref name="fallbackTarget"/>)
        /// to face the main camera.
        /// </summary>
        public static IEnumerator RunWithReticleProgress(
            MonoBehaviour owner,
            Transform fallbackTarget,
            Sprite icon,
            Action onComplete = null)
        {
            if (owner == null) yield break;

            VRGazeReticle reticle = VRGazeReticle.Instance;
            if (reticle == null) reticle = UnityEngine.Object.FindAnyObjectByType<VRGazeReticle>();
            if (reticle != null) reticle.EnterRecenterMode(icon);

            const float duration = 2.0f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (reticle != null)
                    reticle.UpdateRecenterProgress(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                RepositionAllVirtualObjects(cam, fallbackTarget);
            }

            if (reticle != null) reticle.ExitRecenterMode();
            onComplete?.Invoke();
        }

        /// <summary>
        /// Reposition every child of the "VirtualObjects" GameObject so that the primary
        /// RTTMenuFrame faces the camera at the same horizontal distance as before.
        /// Falls back to <paramref name="fallbackTarget"/> when VirtualObjects is missing.
        /// </summary>
        public static void RepositionAllVirtualObjects(Camera cam, Transform fallbackTarget)
        {
            if (cam == null) return;

            GameObject virtualObjectsParent = GameObject.Find("VirtualObjects");
            if (virtualObjectsParent == null)
            {
                Debug.LogWarning("[VirtualObjectsRecenter] VirtualObjects parent not found, recentering fallback target only");
                if (fallbackTarget != null) RepositionTransform(fallbackTarget, cam);
                return;
            }

            RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
            if (primary == null)
            {
                if (fallbackTarget != null) RepositionTransform(fallbackTarget, cam);
                return;
            }

            Vector3 pivotPos = primary.transform.position;
            Quaternion pivotRot = primary.transform.rotation;

            Transform parentTransform = virtualObjectsParent.transform;
            int childCount = parentTransform.childCount;
            var children = new Transform[childCount];
            var relPositions = new Vector3[childCount];
            var relRotations = new Quaternion[childCount];

            Quaternion invPivotRot = Quaternion.Inverse(pivotRot);
            for (int i = 0; i < childCount; i++)
            {
                Transform child = parentTransform.GetChild(i);
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

        /// <summary>
        /// Reposition a single transform so it sits in front of the camera at the
        /// same horizontal distance as before, looking at the camera.
        /// </summary>
        public static void RepositionTransform(Transform target, Camera cam)
        {
            if (target == null || cam == null) return;

            Vector3 camForward = cam.transform.forward;
            camForward.y = 0;
            if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
            camForward.Normalize();

            Vector3 currentPos = target.position;
            Vector3 camPos = cam.transform.position;
            float hDist = Vector2.Distance(
                new Vector2(currentPos.x, currentPos.z),
                new Vector2(camPos.x, camPos.z));

            Vector3 newPos = camPos + camForward * hDist;
            newPos.y = currentPos.y;

            target.position = newPos;
            target.rotation = Quaternion.LookRotation(camForward);
        }
    }
}
