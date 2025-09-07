using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum ViewMode { VirtualSpace, RealWorld }

public class ModeController : MonoBehaviour
{
    public Camera backgroundReal;     // BackgroundCamera_Real
    public Camera backgroundVirtual;  // BackgroundCamera_Virtual
    public GameObject virtualObjects; // luôn bật
    public GameObject virtualEnvironment; // chỉ để tiện bật/tắt nếu muốn giấu ở Real

    public ViewMode mode = ViewMode.VirtualSpace;

    void Start() => Apply();

    public void ToggleMode()
    {
        mode = (mode == ViewMode.VirtualSpace) ? ViewMode.RealWorld : ViewMode.VirtualSpace;
        Apply();
    }

    void Apply()
    {
        bool real = (mode == ViewMode.RealWorld);
        if (backgroundReal) backgroundReal.enabled = real;    // nền thật
        if (backgroundVirtual) backgroundVirtual.enabled = !real;   // nền ảo
        if (virtualObjects) virtualObjects.SetActive(true);      // luôn bật
        if (virtualEnvironment) virtualEnvironment.SetActive(!real); // tuỳ bạn muốn hiện ở Real hay không
    }
}
