using UnityEngine;
using System.Net.Http;
using System.Threading.Tasks;

public class ClusterAutoBinder : MonoBehaviour
{
    public string serverBase = "http://192.168.1.12:8288";
    public WorldPanelClusterRig rig;

    async void Start()
    {
        if (!rig) rig = GetComponent<WorldPanelClusterRig>();
        if (!rig) return;

        // Dựng 3 panel ngay từ đầu
        rig.BuildOrRebuild();  // tạo left/center/right và link neighbors

        var (L, C, R) = await FetchCluster(serverBase + "/api/cluster");
        BindOne(rig.center, C);
        BindOne(rig.left, L);
        BindOne(rig.right, R);
    }

    async Task<(int left, int center, int right)> FetchCluster(string url)
    {
        using var http = new HttpClient();
        var s = await http.GetStringAsync(url);
        // quick-n-dirty parse
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

    void BindOne(WorldPanelPlus panel, int mid)
    {
        if (!panel || mid < 0) return;
        var client = panel.gameObject.AddComponent<PCStreamClient>();
        client.worldPanel = panel;
        client.signalUrl = $"{serverBase.Replace("http", "ws")}/signal?mid={mid}&fps=60&kbps=6000&zerolat=1";
    }
}
