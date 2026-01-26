using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

public class VrQuality : MonoBehaviour
{
    void Awake()
    {
        QualitySettings.antiAliasing = 8;

        // Force maximum anisotropic filtering for VR sharpness
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;

        var display = XRGeneralSettings.Instance?.Manager?.activeLoader
            ?.GetLoadedSubsystem<XRDisplaySubsystem>();

        if (display != null)
            display.scaleOfAllRenderTargets = 1.5f; // thử 1.3 / 1.4 / 1.5 / 1.6
    }
}