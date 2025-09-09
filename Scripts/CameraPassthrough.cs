using UnityEngine;
using UnityEngine.Android;

/// <summary>
/// Simple, robust camera passthrough quad for Mobile VR.
/// - Picks the back camera when available.
/// - Scales/rotates the quad to fully cover the camera frustum without stretching.
/// - Avoids per-frame material allocations; uses sharedMaterial when safe.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class CameraPassthrough : MonoBehaviour
{
    [Header("Bind")]
    [Tooltip("Reference to the rendering camera (usually Main Camera). If null, will auto-detect.")]
    public Camera cam;

    [Header("Framing")]
    [Tooltip("Z distance from the camera. -1 = near farClipPlane.")]
    public float distance = -1f;

    [Header("WebCam")]
    [Tooltip("Requested webcam FPS")]
    public int requestedFPS = 60;
    [Tooltip("Requested webcam width")]
    public int requestedWidth = 1920;
    [Tooltip("Requested webcam height")]
    public int requestedHeight = 1080;

    private WebCamTexture _tex;
    private MeshRenderer _mr;
    private int _selectedDeviceIndex = -1;

    void Awake()
    {
        _mr = GetComponent<MeshRenderer>();
        if (!cam) cam = GetComponentInParent<Camera>();
        // Ensure we have a visible, simple material
        if (_mr.sharedMaterial == null)
            _mr.sharedMaterial = new Material(Shader.Find("Unlit/Texture"));
    }

    void OnEnable() { StartCam(); }
    void OnDisable() { StopCam(); }

    void LateUpdate()
    {
        if (!cam) return;

        // Choose distance
        float d = (distance > 0f) ? distance : Mathf.Max(0.01f, cam.farClipPlane - 1f);
        // Match frustum size at distance d
        float halfHeight = d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float h = 2f * halfHeight;
        float w = h * cam.aspect;

        // Compute target aspect to *cover* frustum
        float texAspect = 1f;
        if (_tex != null && _tex.width > 16 && _tex.height > 16)
        {
            bool rot90 = (_tex.videoRotationAngle % 180) != 0;
            texAspect = rot90 ? (float)_tex.height / _tex.width : (float)_tex.width / _tex.height;
        }
        float frustumAspect = w / h;
        float targetW, targetH;
        if (texAspect > frustumAspect) { targetH = h; targetW = targetH * texAspect; }
        else { targetW = w; targetH = targetW / texAspect; }

        // Mirror/rotation from device
        float xFlip = (_tex != null && _tex.videoVerticallyMirrored) ? -1f : 1f;
        float zRot = (_tex != null) ? -_tex.videoRotationAngle : 0f;

        // Apply transform
        transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, d), Quaternion.Euler(0f, 0f, zRot));

        transform.localScale = new Vector3(targetW * xFlip, targetH, 1f);
    }

    // ------------------------- Camera start/stop -------------------------
    void StartCam()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            // Note: subsequent enable will start once permission is granted
        }
#endif
        var devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0) { Debug.LogWarning("[CameraPassthrough] No camera devices."); return; }

        // Prefer back camera
        _selectedDeviceIndex = 0;
        for (int i = 0; i < devices.Length; i++)
            if (!devices[i].isFrontFacing) { _selectedDeviceIndex = i; break; }

        // Reuse existing material, avoid per-frame material access
        var mat = GetComponent<MeshRenderer>().sharedMaterial != null ? GetComponent<MeshRenderer>().sharedMaterial : (GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Texture")));

        // Create and start the webcam
        var devName = devices[_selectedDeviceIndex].name;
        _tex = new WebCamTexture(devName, requestedWidth, requestedHeight, requestedFPS);
        mat.mainTexture = _tex;
        _tex.Play();
    }

    void StopCam()
    {
        if (_tex != null)
        {
            if (_tex.isPlaying) _tex.Stop();
#if UNITY_EDITOR
            DestroyImmediate(_tex);
#else
            Destroy(_tex);
#endif
            _tex = null;
            var mr = GetComponent<MeshRenderer>();
            if (mr && mr.sharedMaterial) mr.sharedMaterial.mainTexture = null;
        }
    }
}
