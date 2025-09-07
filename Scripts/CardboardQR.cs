using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using Google.XR.Cardboard;
#endif

public class CardboardQR : MonoBehaviour
{
    [SerializeField] float holdToRescan = 1.2f;
    float holdTimer = 0f;

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Api.HasDeviceParams()) Api.ScanDeviceParams();
#endif
    }
    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Api.HasNewDeviceParams()) Api.ReloadDeviceParams();
        if (Api.IsTriggerPressed)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= holdToRescan){ Api.ScanDeviceParams(); holdTimer = 0f; }
        } else holdTimer = 0f;
#endif
    }
    void OnApplicationPause(bool pause)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!pause) Api.ReloadDeviceParams();
#endif
    }
}
