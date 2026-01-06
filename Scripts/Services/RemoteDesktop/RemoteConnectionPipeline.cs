using UnityEngine;

/// <summary>
/// Manages ClusterRig creation and lifecycle.
/// Streaming is handled by ConnectionViewModel → PhaseProtocolClient.
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
    /// Stop streaming and cleanup.
    /// Called when menu is closed to ensure clean state.
    /// </summary>
    public void StopStreaming()
    {
        // Streaming is managed by ConnectionViewModel, nothing to do here
        // This method kept for API compatibility
        OnConnectionStatusChanged?.Invoke(false);
    }
    #endregion
}
