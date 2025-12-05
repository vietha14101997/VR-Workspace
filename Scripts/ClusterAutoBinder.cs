using UnityEngine;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

/// <summary>
/// Dynamic Combined Stream approach:
/// - Server ghép N màn hình thành 1 frame lớn (side-by-side)
/// - Client tự động nhận diện số lượng monitors từ server
/// - Tạo đúng số panel tương ứng và bind UV crop cho từng panel
/// </summary>
public class ClusterAutoBinder : MonoBehaviour
{
    public string serverBase = "http://localhost:8288";
    public WorldPanelClusterRig rig;

    [Header("Stream Settings")]
    public int fps = 30;
    public int kbps = 4000;
    public bool zeroLatency = true;

    [Header("Layout Info (auto-fetched from server)")]
    [SerializeField] private int monitorCount = 3;
    [SerializeField] private int frameWidth = 4096;
    [SerializeField] private int frameHeight = 768;
    [SerializeField] private int cellWidth = 1364;
    [SerializeField] private int cellHeight = 768;
    [SerializeField] private int gapPixels = 1;

    private PCStreamClient _masterClient;
    private List<UVCropReceiver> _cropReceivers = new List<UVCropReceiver>();

    async void Start()
    {
        if (!rig) rig = GetComponent<WorldPanelClusterRig>();
        if (!rig) return;

        // Fetch layout from server (includes monitor count)
        var layout = await FetchLayout(serverBase + "/api/layout");
        if (layout.monitors > 0)
        {
            monitorCount = layout.monitors;
            frameWidth = layout.frameWidth;
            frameHeight = layout.frameHeight;
            cellWidth = layout.cellWidth;
            cellHeight = layout.cellHeight;
            gapPixels = layout.gap;
            
            Debug.Log($"[ClusterAutoBinder] Server config: {monitorCount} monitors, frame={frameWidth}x{frameHeight}, cell={cellWidth}x{cellHeight}");
        }
        else
        {
            Debug.LogWarning("[ClusterAutoBinder] Could not fetch layout, using defaults");
        }

        // Build rig with correct number of panels
        rig.BuildWithPanelCount(monitorCount);

        // Wait a frame for panels to be created
        await Task.Yield();

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
        client.signalUrl = $"{wsBase}/signal?mode=cluster&fps={fps}&kbps={kbps}&zerolat={(zeroLatency ? 1 : 0)}";

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

    async Task<(int monitors, int frameWidth, int frameHeight, int cellWidth, int cellHeight, int gap)> FetchLayout(string url)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = System.TimeSpan.FromSeconds(5);
            var json = await http.GetStringAsync(url);
            Debug.Log($"[ClusterAutoBinder] /api/layout: {json}");

            int Get(string key)
            {
                var tag = $"\"{key}\"";
                int i = json.IndexOf(tag);
                if (i < 0) return -1;
                i = json.IndexOf(':', i) + 1;
                int j = json.IndexOfAny(new[] { ',', '}' }, i);
                var sub = json.Substring(i, j - i).Trim();
                int.TryParse(sub, out var v);
                return v;
            }

            return (Get("monitors"), Get("frameWidth"), Get("frameHeight"), Get("cellWidth"), Get("cellHeight"), Get("gap"));
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ClusterAutoBinder] FetchLayout failed: {ex.Message}");
            return (-1, -1, -1, -1, -1, -1);
        }
    }

    void OnDestroy()
    {
        if (_masterClient) Destroy(_masterClient);
        foreach (var r in _cropReceivers)
            if (r) Destroy(r);
        _cropReceivers.Clear();
    }
}
