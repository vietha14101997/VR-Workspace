using UnityEngine;
using System.Text.RegularExpressions;

/// <summary>
/// Orchestrates remote desktop connection flow including ClusterRig management,
/// parameter parsing, and streaming control.
/// </summary>
public class RemoteConnectionPipeline : MonoBehaviour
{
    #region Configuration
    [Header("ClusterRig")]
    [Tooltip("Prefab for WorldPanelClusterRig (if null, will create dynamically)")]
    [SerializeField] private WorldPanelClusterRig clusterRigPrefab;

    [Tooltip("Reference to existing ClusterRig in scene (optional)")]
    [SerializeField] private WorldPanelClusterRig clusterRigInstance;
    #endregion

    #region Events
    public event System.Action<bool> OnConnectionStatusChanged;
    public event System.Action<string> OnConnectionError;
    #endregion

    #region Properties
    public WorldPanelClusterRig ClusterRigInstance => clusterRigInstance;
    public bool IsStreaming => clusterRigInstance != null &&
        clusterRigInstance.GetComponent<ClusterAutoBinder>()?.IsStreaming == true;
    #endregion

    #region Public API
    /// <summary>
    /// Get existing ClusterRig or create a new one.
    /// </summary>
    public WorldPanelClusterRig GetOrCreateClusterRig(Transform spawnParent = null)
    {
        // Use existing instance if available
        if (clusterRigInstance != null)
        {
            return clusterRigInstance;
        }

        // Instantiate from prefab if available
        if (clusterRigPrefab != null)
        {
            clusterRigInstance = Instantiate(clusterRigPrefab);
            clusterRigInstance.name = "WorldPanelClusterRig";
            return clusterRigInstance;
        }

        // Create dynamically
        GameObject rigObj = new GameObject("WorldPanelClusterRig");
        clusterRigInstance = rigObj.AddComponent<WorldPanelClusterRig>();

        // Position the rig in front of the spawn parent if provided
        if (spawnParent != null)
        {
            rigObj.transform.position = spawnParent.position + spawnParent.forward * 2f;
            rigObj.transform.rotation = spawnParent.rotation;
        }

        return clusterRigInstance;
    }

    /// <summary>
    /// Start remote desktop streaming with the specified settings.
    /// </summary>
    public void StartStreaming(string host, string port, int monitorCount,
        string resolution, string bitrate, string fps)
    {
        // Parse parameters
        var (resWidth, resHeight) = ParseResolution(resolution);
        int bitrateKbps = ParseBitrate(bitrate);
        int fpsValue = ParseFPS(fps);

        // Get or create ClusterRig
        WorldPanelClusterRig rig = GetOrCreateClusterRig();
        if (rig == null)
        {
            Debug.LogError("[RemoteConnectionPipeline] Failed to create ClusterRig!");
            OnConnectionError?.Invoke("Failed to create ClusterRig");
            return;
        }

        // Build cluster with the specified monitor count
        rig.BuildWithPanelCount(monitorCount);

        // Get or create ClusterAutoBinder
        ClusterAutoBinder binder = rig.GetComponent<ClusterAutoBinder>();
        if (binder == null)
        {
            binder = rig.gameObject.AddComponent<ClusterAutoBinder>();
        }

        // Stop existing stream if running
        if (binder.IsStreaming)
        {
            binder.StopStreaming();
        }

        // Configure binder
        binder.autoStart = false;
        binder.serverBase = $"http://{host}:{port}";
        binder.rig = rig;
        binder.monitorCount = monitorCount;
        binder.resolutionWidth = resWidth;
        binder.resolutionHeight = resHeight;
        binder.bitrateKbps = bitrateKbps;
        binder.fps = fpsValue;

        Debug.Log($"[RemoteConnectionPipeline] ClusterRig configured: {monitorCount} panels, {resWidth}x{resHeight}, {bitrateKbps}kbps, {fpsValue}fps");
        Debug.Log($"[RemoteConnectionPipeline] Server: {binder.serverBase}");

        // Start streaming
        binder.StartStreaming();
        OnConnectionStatusChanged?.Invoke(true);
    }

    /// <summary>
    /// Stop remote desktop streaming.
    /// </summary>
    public void StopStreaming()
    {
        if (clusterRigInstance == null) return;

        ClusterAutoBinder binder = clusterRigInstance.GetComponent<ClusterAutoBinder>();
        if (binder != null && binder.IsStreaming)
        {
            binder.StopStreaming();
            OnConnectionStatusChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// Setup ClusterAutoBinder with V2 protocol for phased connection flow.
    /// </summary>
    public ClusterAutoBinder SetupV2Protocol(Transform spawnParent = null)
    {
        WorldPanelClusterRig rig = GetOrCreateClusterRig(spawnParent);
        if (rig == null) return null;

        ClusterAutoBinder binder = rig.GetComponent<ClusterAutoBinder>();
        if (binder == null)
        {
            binder = rig.gameObject.AddComponent<ClusterAutoBinder>();
        }

        binder.autoStart = false;
        binder.rig = rig;
        binder.useV2Protocol = true;

        Debug.Log("[RemoteConnectionPipeline] ClusterAutoBinder setup for V2 protocol");
        return binder;
    }
    #endregion

    #region Parameter Parsing
    /// <summary>
    /// Parse resolution string (e.g., "1920 x 1080") to width and height.
    /// </summary>
    private (int width, int height) ParseResolution(string resolution)
    {
        int width = 1920, height = 1080;

        if (!string.IsNullOrEmpty(resolution))
        {
            var parts = resolution.Replace(" ", "").Split('x');
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out width);
                int.TryParse(parts[1], out height);
            }
        }

        return (width, height);
    }

    /// <summary>
    /// Parse bitrate string (e.g., "20 Mbps") to kbps.
    /// </summary>
    private int ParseBitrate(string bitrate)
    {
        int bitrateKbps = 20000;

        if (!string.IsNullOrEmpty(bitrate))
        {
            var match = Regex.Match(bitrate, @"\d+");
            if (match.Success)
            {
                bitrateKbps = int.Parse(match.Value) * 1000;
            }
        }

        return bitrateKbps;
    }

    /// <summary>
    /// Parse FPS string (e.g., "60 FPS") to integer.
    /// </summary>
    private int ParseFPS(string fps)
    {
        int fpsValue = 60;

        if (!string.IsNullOrEmpty(fps))
        {
            var match = Regex.Match(fps, @"\d+");
            if (match.Success)
            {
                fpsValue = int.Parse(match.Value);
            }
        }

        return fpsValue;
    }
    #endregion
}
