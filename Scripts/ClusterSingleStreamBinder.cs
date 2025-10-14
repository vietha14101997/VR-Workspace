using UnityEngine;
using System.Net.Http;
using System.Threading.Tasks;

public class ClusterSingleStreamBinder : MonoBehaviour
{
    [Header("Server Configuration")]
    public string serverBase = "http://192.168.1.3:8288";
    public WorldPanelClusterRig rig;
    public PCStreamClient singleClientPrefab; // để trống: sẽ add PCStreamClient runtime lên rig.center

    [Header("Stream Processing Components")]
    public ClusterStreamDecoder decoder;
    public ClusterStreamDistributor distributor;

    // PCStreamClient tham chiếu để thiết lập decoder
    private PCStreamClient _streamClient;

    void Awake()
    {
        if (!rig) rig = GetComponent<WorldPanelClusterRig>();

        // Tự động tạo các component nếu cần
        EnsureComponentsExist();
    }

    void EnsureComponentsExist()
    {
        // Tạo decoder nếu chưa có
        if (decoder == null)
            decoder = GetComponent<ClusterStreamDecoder>() ?? gameObject.AddComponent<ClusterStreamDecoder>();

        // Tạo distributor nếu chưa có
        if (distributor == null)
            distributor = GetComponent<ClusterStreamDistributor>() ?? gameObject.AddComponent<ClusterStreamDistributor>();

        // Cấu hình distributor
        distributor.clusterRig = rig;
        distributor.decoder = decoder;
        distributor.debugLog = true;
    }

    async void Start()
    {
        if (!rig) return;
        rig.BuildOrRebuild();

        // Lấy mid của màn ảo (server trả cùng mid cho 3 panel)
        var (L, C, R) = await FetchCluster(serverBase + "/api/cluster");
        int mid = C >= 0 ? C : (L >= 0 ? L : R);

        // Tạo 1 client (gắn lên panel center)
        var host = rig.center ? rig.center.gameObject : gameObject;
        var client = singleClientPrefab ? Instantiate(singleClientPrefab, host.transform)
                                        : host.AddComponent<PCStreamClient>();

        client.worldPanel = rig.center; // panel trung tâm sẽ Apply() và giữ material instance
        client.signalUrl = $"{serverBase.Replace("http", "ws")}/signal?mid={mid}&fps=60&kbps=8000&zerolat=1";

        // Lưu tham chiếu client
        _streamClient = client;

        // Hook decoder với client
        StartCoroutine(SetupDecoderWithClient(client));
    }

    System.Collections.IEnumerator SetupDecoderWithClient(PCStreamClient client)
    {
        // Đợi client có texture
        while (client == null || rig.center == null || rig.center.contentTexture == null)
        {
            yield return null;
        }

        // Thiết lập decoder với texture gốc
        decoder.SetSourceTexture(rig.center.contentTexture);

        Debug.Log("[ClusterSingleStreamBinder] Decoder setup completed with source texture");
    }

    void Update()
    {
        // Cập nhật decoder với texture mới từ client
        UpdateDecoderWithLatestTexture();
    }

    /// <summary>
    /// Cập nhật decoder với texture mới nhất từ PCStreamClient
    /// </summary>
    void UpdateDecoderWithLatestTexture()
    {
        if (_streamClient != null && rig.center != null && rig.center.contentTexture != null)
        {
            // Cập nhật decoder với texture mới
            decoder.SetSourceTexture(rig.center.contentTexture);
        }
    }

    async Task<(int left, int center, int right)> FetchCluster(string url)
    {
        using var http = new HttpClient();
        var s = await http.GetStringAsync(url);
        int Get(string key)
        {
            var tag = $"\"{key}\"";
            int i = s.IndexOf(tag); if (i < 0) return -1;
            i = s.IndexOf(':', i) + 1;
            int j = s.IndexOfAny(new[] { ',', '}' }, i);
            var sub = s.Substring(i, j - i).Trim();
            int.TryParse(sub, out var v); return v;
        }
        return (Get("left"), Get("center"), Get("right"));
    }
}
