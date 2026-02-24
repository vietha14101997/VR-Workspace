using UnityEngine;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// Utility class for locating the main VR or desktop camera in the scene.
    /// Centralises the camera-search logic previously duplicated across RTTPopupMenu,
    /// RTTPopupInputable, and RTTProgressPopup.
    /// </summary>
    public static class CameraFinder
    {
        /// <summary>
        /// Find the main camera, preferring known VR rig camera names before
        /// falling back to Camera.main and then any active non-UI camera.
        /// </summary>
        /// <returns>The best-match Camera, or null if none is found.</returns>
        public static Camera FindMainCamera()
        {
            string[] cameraNames = { "CenterEyeAnchor", "Main Camera", "PlayerCamera", "Camera" };
            foreach (var name in cameraNames)
            {
                GameObject camObj = GameObject.Find(name);
                if (camObj != null)
                {
                    Camera cam = camObj.GetComponent<Camera>();
                    if (cam != null && cam.gameObject.activeInHierarchy) return cam;
                }
            }

            if (Camera.main != null) return Camera.main;

            Camera[] allCameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var cam in allCameras)
            {
                if (!cam.name.Contains("UI") && cam.gameObject.activeInHierarchy)
                {
                    return cam;
                }
            }

            return null;
        }
    }
}
