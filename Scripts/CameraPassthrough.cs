using UnityEngine;
using UnityEngine.Android;

[RequireComponent(typeof(MeshRenderer))]
public class CameraPassthrough : MonoBehaviour
{
    public Camera cam;           // kéo Main Camera vào
    public float distance = -1f; // -1 = tự đặt gần far clip
    public int requestedFPS = 30;

    WebCamTexture _tex;
    MeshRenderer _mr;

    void Awake() { _mr = GetComponent<MeshRenderer>(); if (!cam) cam = GetComponentInParent<Camera>(); }
    void OnEnable() { StartCam(); }
    void OnDisable() { StopCam(); }

    void LateUpdate()
    {
        if (!cam) return;
        float d = (distance > 0f) ? distance : (cam.farClipPlane - 1f);
        float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * cam.aspect;

        // aspect “cover” để không méo + xử lý xoay/mirror
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

        float xFlip = (_tex != null && _tex.videoVerticallyMirrored) ? -1f : 1f;
        float zRot = (_tex != null) ? -_tex.videoRotationAngle : 0f;

        transform.localPosition = new Vector3(0, 0, d);
        transform.localRotation = Quaternion.Euler(0, 0, zRot);
        transform.localScale = new Vector3(targetW * xFlip, targetH, 1f);
    }

    void StartCam()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            Permission.RequestUserPermission(Permission.Camera);
#endif
        var devs = WebCamTexture.devices;
        if (devs.Length == 0) { Debug.LogWarning("No camera devices"); return; }

        // ưu tiên camera sau
        int idx = 0; for (int i = 0; i < devs.Length; i++) if (!devs[i].isFrontFacing) { idx = i; break; }

        _tex = new WebCamTexture(devs[idx].name, Screen.width, Screen.height, requestedFPS);
        _mr.material.mainTexture = _tex;
        _mr.material.renderQueue = 1999; // vẽ sớm
        _tex.Play();
    }

    void StopCam()
    {
        if (_tex != null) { if (_tex.isPlaying) _tex.Stop(); Destroy(_tex); _tex = null; }
    }
}
