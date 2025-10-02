using UnityEngine;
using UnityEngine.Android;

[RequireComponent(typeof(MeshRenderer))]
public class CameraPassthrough : MonoBehaviour
{
    [Header("References")]
    public Camera cam;                 // Kéo BackgroundCamera_Real vào
    [Header("Placement")]
    public float distance = -1f;       // -1 = tự đặt gần far clip
    [Header("Webcam")]
    public int requestedFPS = 60;
    public int requestedWidth = 1366;
    public int requestedHeight = 768;
    public int deviceIndex = 0;        // 0 = camera mặc định

    MeshRenderer _mr;
    WebCamTexture _tex;

    void Awake()
    {
        _mr = GetComponent<MeshRenderer>();
        if (!cam) cam = GetComponentInParent<Camera>();
    }

    void OnEnable() { StartCam(); }
    void OnDisable() { StopCam(); }

    void LateUpdate()
    {
        if (!cam) return;

        // Đặt quad trước camera ở khoảng cách d
        float d = (distance > 0f) ? distance : (cam.farClipPlane - 1f);
        float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * cam.aspect;

        // Tính aspect “cover” cho texture
        float texAspect = 1f;
        if (_tex != null && _tex.width > 16 && _tex.height > 16)
        {
            bool rot90 = (_tex.videoRotationAngle == 90 || _tex.videoRotationAngle == 270);
            int tw = rot90 ? _tex.height : _tex.width;
            int th = rot90 ? _tex.width : _tex.height;
            texAspect = (th > 0) ? (tw / (float)th) : 1f;
        }

        // Scale để hình che kín khung nhìn camera (cover)
        float targetW = w;
        float targetH = w / texAspect;
        if (targetH < h)
        {
            targetH = h;
            targetW = h * texAspect;
        }

        // Xử lý mirror + rotation theo WebCamTexture
        float xFlip = 1f;
        float zRot = 0f;
        if (_tex != null)
        {
            if (_tex.videoVerticallyMirrored) xFlip = -1f;
            zRot = -_tex.videoRotationAngle; // WebCamTexture quay ngược chiều kim đồng hồ
        }

        transform.localPosition = new Vector3(0f, 0f, d);
        transform.localRotation = Quaternion.Euler(0f, 0f, zRot);
        transform.localScale = new Vector3(targetW * xFlip, targetH, 1f);
    }

    // ============== Webcam lifetime ==============

    public void StartCam()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            // Không block chờ permission; OnEnable sẽ được gọi lại khi script bật
        }
#endif
        if (_tex != null)
        {
            if (!_tex.isPlaying) _tex.Play();
            ApplyTextureToMaterial();
            return;
        }

        // Tạo vật liệu URP Unlit nếu chưa có
        if (_mr.material == null || _mr.material.shader == null ||
            _mr.material.shader.name.Contains("Standard"))
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            _mr.material = new Material(sh);
        }

        var devs = WebCamTexture.devices;
        string devName = (devs != null && devs.Length > 0)
            ? devs[Mathf.Clamp(deviceIndex, 0, devs.Length - 1)].name
            : null;

        _tex = (devName != null)
            ? new WebCamTexture(devName, requestedWidth, requestedHeight, requestedFPS)
            : new WebCamTexture(requestedWidth, requestedHeight, requestedFPS);

        _tex.filterMode = FilterMode.Bilinear;
        _tex.wrapMode = TextureWrapMode.Clamp;
        _tex.Play();

        ApplyTextureToMaterial();
    }

    public void StopCam()
    {
        if (_tex != null)
        {
            if (_tex.isPlaying) _tex.Stop();
            if (_mr != null && _mr.material != null) _mr.material.mainTexture = null;
            Destroy(_tex);
            _tex = null;
        }
    }

    void ApplyTextureToMaterial()
    {
        if (_mr != null && _mr.material != null)
        {
            _mr.material.mainTexture = _tex;
            // Nếu shader có _Cull, tắt cull để nhìn cả 2 mặt khi cần
            if (_mr.material.HasProperty("_Cull")) _mr.material.SetInt("_Cull", 0);
        }
    }
}
