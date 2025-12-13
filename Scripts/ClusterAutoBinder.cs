using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Dynamic Combined Stream approach:
/// - Server ghép N màn hình thành 1 frame lớn (side-by-side)
/// - Client tự động kết nối tới signal URL với cấu hình cố định
/// - Tạo đúng số panel tương ứng và bind UV crop cho từng panel
/// </summary>
public class ClusterAutoBinder : MonoBehaviour
{
    public string serverBase = "http://192.168.1.9:8288";
    public WorldPanelClusterRig rig;

    [Header("Signal URL Path (appended to serverBase)")]
    [Tooltip("Signal path with query params. Example: signal?mode=cluster&monitors=2&resW=1920&resH=1080&kbps=8000&fps=30")]
    public string signalPath = "signal?mode=cluster&monitors=2&resW=1920&resH=1080&kbps=8000&fps=30";

    [Header("Layout Info (fixed defaults)")]
    [SerializeField] private int monitorCount = 2;
    [SerializeField] private int frameWidth = 3840;
    [SerializeField] private int frameHeight = 1080;
    [SerializeField] private int cellWidth = 1920;
    [SerializeField] private int cellHeight = 1080;
    [SerializeField] private int gapPixels = 0;

    private PCStreamClient _masterClient;
    private List<UVCropReceiver> _cropReceivers = new List<UVCropReceiver>();

    void Start()
    {
        if (!rig) rig = GetComponent<WorldPanelClusterRig>();
        if (!rig) return;

        Debug.Log($"[ClusterAutoBinder] Using fixed config: {monitorCount} monitors, frame={frameWidth}x{frameHeight}, cell={cellWidth}x{cellHeight}");

        // Build rig with correct number of panels
        rig.BuildWithPanelCount(monitorCount);

        // Bind panels to stream
        BindPanelsToStream();
    }

    void BindPanelsToStream()
    {
        var panels = rig.panels;
        if (panels == null || panels.Count == 0)
        {
            Debug.LogError("[ClusterAutoBinder] No panels found in rig!");
            return;
        }

        // Find center panel index (middle of the array)
        int centerIdx = panels.Count / 2;
        
        // Create master client on center panel
        _masterClient = CreateMasterClient(panels[centerIdx], centerIdx);

        // Create crop receivers for other panels
        for (int i = 0; i < panels.Count; i++)
        {
            if (i == centerIdx) continue; // Skip center (already has master client)
            CreateCropReceiver(panels[i], i);
        }

        Debug.Log($"[ClusterAutoBinder] Bound {panels.Count} panels to combined stream");
    }

    PCStreamClient CreateMasterClient(WorldPanelPlus panel, int col)
    {
        if (!panel) return null;

        var client = panel.gameObject.AddComponent<PCStreamClient>();
        client.worldPanel = panel;

        var wsBase = serverBase.Replace("http://", "ws://").Replace("https://", "wss://");
        client.signalUrl = $"{wsBase}/{signalPath}";

        client.useUVCrop = true;
        client.gridCol = col;
        client.gridRow = 0;
        client.gridCols = monitorCount;
        client.gridRows = 1;
        client.frameWidth = frameWidth;
        client.frameHeight = frameHeight;
        client.cellWidth = cellWidth;
        client.cellHeight = cellHeight;
        client.gapPixels = gapPixels;

        Debug.Log($"[ClusterAutoBinder] Master client on panel[{col}], url={client.signalUrl}");
        return client;
    }

    void CreateCropReceiver(WorldPanelPlus panel, int col)
    {
        if (!panel || _masterClient == null) return;

        var receiver = panel.gameObject.AddComponent<UVCropReceiver>();
        receiver.sourceClient = _masterClient;
        receiver.worldPanel = panel;
        receiver.gridCol = col;
        receiver.gridRow = 0;
        receiver.gridCols = monitorCount;
        receiver.gridRows = 1;
        receiver.frameWidth = frameWidth;
        receiver.frameHeight = frameHeight;
        receiver.cellWidth = cellWidth;
        receiver.cellHeight = cellHeight;
        receiver.gapPixels = gapPixels;

        _cropReceivers.Add(receiver);
        Debug.Log($"[ClusterAutoBinder] CropReceiver on panel[{col}]");
    }

    void OnDestroy()
    {
        if (_masterClient) Destroy(_masterClient);
        foreach (var r in _cropReceivers)
            if (r) Destroy(r);
        _cropReceivers.Clear();
    }
}
