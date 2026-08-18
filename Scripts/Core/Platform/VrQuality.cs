using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace VRWorkspace.Core
{
    public class VrQuality : MonoBehaviour
    {
        void Awake()
        {
            // MSAA: 8 → 2. On tile-based mobile GPUs (Quest/Pico) MSAA 8x roughly
            // triples fragment cost vs MSAA 2x. VR lenses already soften edges; MSAA 4+
            // produces diminishing returns while dramatically increasing heat.
            QualitySettings.antiAliasing = 2;

            // Disable global aniso force-enable. AnisotropicFiltering has only 3 values:
            // Disable / Enable / ForceEnable. Setting Disable means textures use their
            // own anisoLevel field (default 0 = no aniso). The video plane is viewed
            // head-on, so no aniso is needed — each texture controls its own.
            // ForceEnable applied aniso 16 to every texture including head-on UI panels.
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;

            var display = XRGeneralSettings.Instance?.Manager?.activeLoader
                ?.GetLoadedSubsystem<XRDisplaySubsystem>();

            // Render scale 1.2 → 0.9. 1.2 supersampling = 44% more pixels than 1.0
            // for no visible quality gain (video screen pixel ratio limited by source).
            // 0.9 reduces fillrate by ~44% — biggest single thermal win.
            if (display != null)
                display.scaleOfAllRenderTargets = 0.9f;
        }
    }
}
