using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace VRWorkspace.Core
{
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
                display.scaleOfAllRenderTargets = 1.2f; // Cardboard-friendly: 1.2 balances quality vs FPS
        }
    }
}
