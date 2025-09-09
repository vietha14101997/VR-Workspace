using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using Google.XR.Cardboard;
#endif

public class CardboardQR : MonoBehaviour
{
    [SerializeField] private float holdToRescan = 1.2f;

#if UNITY_ANDROID && !UNITY_EDITOR
    private float _holdTimer;

    private void Start()
    {
        if (!Api.HasDeviceParams())
            Api.ScanDeviceParams();
    }

    private void Update()
    {
        // Apply new params if user has updated the QR profile in another flow
        if (Api.HasNewDeviceParams())
            Api.ReloadDeviceParams();

        // Hold trigger to rescan
        if (Api.IsTriggerPressed)
        {
            _holdTimer += Time.deltaTime;
            if (_holdTimer >= holdToRescan)
            {
                Api.ScanDeviceParams();
                _holdTimer = 0f;
            }
        }
        else
        {
            _holdTimer = 0f;
        }
    }

    private void OnApplicationPause(bool pause)
    {
        // On resume, ensure parameters are applied
        if (!pause)
            Api.ReloadDeviceParams();
    }
#else
    // Non-Android/editor stub to avoid platform compilation issues
    private void Start() { }
    private void Update() { }
    private void OnApplicationPause(bool pause) { }
#endif
}
