using UnityEngine;
#if ADAPTIVE_PERFORMANCE_AVAILABLE
using UnityEngine.AdaptivePerformance;
#endif

public class APBootstrap : MonoBehaviour
{
    [Header("Main Menu")]
    public bool spawnMainMenu = true;
    public float menuSpawnDelay = 0.5f;
    public float menuDistanceFromCamera = 1.5f;

    private MainMenuPanel _mainMenu;

    void Awake()
    {
        Application.targetFrameRate = 60;      // Cardboard/stream: giữ 60 cho ổn định
        QualitySettings.vSyncCount = 0;        // tránh vSync cản targetFrameRate
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    void Start()
    {
        if (spawnMainMenu)
        {
            Invoke(nameof(SpawnMainMenu), menuSpawnDelay);
        }

#if ADAPTIVE_PERFORMANCE_AVAILABLE
        SetupAdaptivePerformance();
#endif
    }

#if ADAPTIVE_PERFORMANCE_AVAILABLE
    void SetupAdaptivePerformance()
    {
        var ap = Holder.Instance?.AdaptivePerformance;
        if (ap == null) return;

        // Giới hạn mức boost để tránh máy nóng quá
        ap.Indexer.Settings.maxCpuPerformanceLevel = 2;
        ap.Indexer.Settings.maxGpuPerformanceLevel = 2;

        // Chỉ cho phép giảm render scale nhẹ
        var res = ap.Indexer.AdaptivePerformanceScalers.AdaptiveResolution;
        if (res != null) { res.MinScale = 0.85f; res.MaxScale = 1.0f; }

        // Không cho phép Adaptive Framerate kéo FPS xuống
        var fps = ap.Indexer.AdaptivePerformanceScalers.AdaptiveFramerate;
        if (fps != null) fps.Enabled = false;
    }
#endif

    void SpawnMainMenu()
    {
        if (_mainMenu != null) return;

        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[APBootstrap] No main camera found for MainMenu");
            return;
        }

        // Find VirtualObjects parent or create one
        var virtualObjects = GameObject.Find("VirtualObjects");
        if (virtualObjects == null)
        {
            virtualObjects = new GameObject("VirtualObjects");
            virtualObjects.layer = LayerMask.NameToLayer("VirtualObjects");
        }

        // Create MainMenu
        var menuGO = new GameObject("MainMenu");
        menuGO.transform.SetParent(virtualObjects.transform, false);
        menuGO.layer = LayerMask.NameToLayer("VirtualObjects");

        _mainMenu = menuGO.AddComponent<MainMenuPanel>();
        _mainMenu.vrCamera = cam;

        Debug.Log("[APBootstrap] MainMenu spawned");
    }

    public void ShowMainMenu()
    {
        if (_mainMenu != null) _mainMenu.Show();
        else if (spawnMainMenu) SpawnMainMenu();
    }

    public void HideMainMenu()
    {
        if (_mainMenu != null) _mainMenu.Hide();
    }

    public void ToggleMainMenu()
    {
        if (_mainMenu != null) _mainMenu.Toggle();
        else if (spawnMainMenu) SpawnMainMenu();
    }
}
