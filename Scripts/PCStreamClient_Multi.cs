using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Unity.WebRTC;

public class PCStreamClient_Multi : MonoBehaviour
{
    public string serverHost = "192.168.0.105";
    public int port = 8288;
    public WorldPanelPlus panelPrefab;
    public Transform panelsParent;

    IEnumerator Start()
    {
        // Bản 3.0+ chỉ cần chạy coroutine update
        StartCoroutine(WebRTC.Update());

        // Fetch windows list
        var url = $"http://{serverHost}:{port}/api/windows";
        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Failed to get windows: " + req.error);
            yield break;
        }
        var json = req.downloadHandler.text;
        var list = JsonUtility.FromJson<WindowList>("{\"items\":" + json + "}");

        // Spawn a panel per window (limit to first 3 for demo)
        int count = Mathf.Min(3, list.items.Length);
        for (int i = 2; i < count; i++)
        {
            var p = Instantiate(panelPrefab, panelsParent);
            p.width = 1.2f; p.height = 0.68f;
            p.Apply();

            var go = new GameObject("PCStreamClient");
            go.transform.SetParent(p.transform, false);
            var c = go.AddComponent<PCStreamClient>();
            c.signalUrl = $"ws://{serverHost}:{port}/signal?wid={list.items[i].id}&fps=60&kbps=12000&crf=20&zerolat=1";
            c.worldPanel = p;
        }
    }

    [System.Serializable] public class WindowInfo { public int id; public string title; }
    [System.Serializable] public class WindowList { public WindowInfo[] items; }
}
