using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// RTTMenu - Container for RTTMenuFrame instances.
/// Provides a pivot point for RTTTaskbar to follow.
/// Structure:
///   VirtualObjects
///     ├── RTTMenu (this container)
///     │     ├── RTTMenuFrame_main
///     │     └── (other RTTMenuFrames for apps)
///     └── RTTTaskbar (follows RTTMenu)
/// </summary>
public class RTTMenu : MonoBehaviour
{
    #region Static Instance
    private static RTTMenu _instance;
    public static RTTMenu Instance => _instance;
    #endregion

    #region Private Fields
    private RTTMenuFrame _mainFrame;
    private List<RTTMenuFrame> _frames = new List<RTTMenuFrame>();
    private WorldPanelClusterRig _clusterRig;
    #endregion

    #region Properties
    /// <summary>
    /// The main (primary) RTTMenuFrame showing the main menu.
    /// </summary>
    public RTTMenuFrame MainFrame => _mainFrame;

    /// <summary>
    /// The WorldPanelClusterRig for remote streaming (if active).
    /// </summary>
    public WorldPanelClusterRig ClusterRig => _clusterRig;

    /// <summary>
    /// The currently active (enabled) RTTMenuFrame.
    /// Returns the first active frame found, or main frame as fallback.
    /// </summary>
    public RTTMenuFrame ActiveFrame => GetActiveFrame();

    /// <summary>
    /// All RTTMenuFrames in this container.
    /// </summary>
    public IReadOnlyList<RTTMenuFrame> Frames => _frames.AsReadOnly();

    /// <summary>
    /// Number of frames in this container.
    /// </summary>
    public int FrameCount => _frames.Count;
    #endregion

    #region Lifecycle
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[RTTMenu] Another instance exists, destroying this one");
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Create and add the main RTTMenuFrame.
    /// </summary>
    public RTTMenuFrame CreateMainFrame(float widthMeters, float heightMeters, float logicalWidth = 1920f)
    {
        if (_mainFrame != null)
        {
            Debug.LogWarning("[RTTMenu] Main frame already exists");
            return _mainFrame;
        }

        _mainFrame = RTTMenuFrame.Create(
            parent: transform,
            widthMeters: widthMeters,
            heightMeters: heightMeters,
            logicalWidthPixels: logicalWidth,
            name: "RTTMenuFrame_main"
        );

        _mainFrame.SetAsPrimaryFrame();
        _frames.Add(_mainFrame);

        Debug.Log("[RTTMenu] Created main frame");
        return _mainFrame;
    }

    /// <summary>
    /// Create and add an app RTTMenuFrame.
    /// </summary>
    public RTTMenuFrame CreateAppFrame(string appId, float widthMeters, float heightMeters, float logicalWidth = 1920f)
    {
        var frame = RTTMenuFrame.Create(
            parent: transform,
            widthMeters: widthMeters,
            heightMeters: heightMeters,
            logicalWidthPixels: logicalWidth,
            name: $"RTTMenuFrame_{appId}"
        );

        // Position at same location as main frame
        if (_mainFrame != null)
        {
            frame.transform.localPosition = _mainFrame.transform.localPosition;
            frame.transform.localRotation = _mainFrame.transform.localRotation;
        }

        _frames.Add(frame);
        Debug.Log($"[RTTMenu] Created app frame: {appId}");
        return frame;
    }

    /// <summary>
    /// Add an existing RTTMenuFrame to this container.
    /// </summary>
    public void AddFrame(RTTMenuFrame frame)
    {
        if (frame == null) return;

        if (!_frames.Contains(frame))
        {
            frame.transform.SetParent(transform, true);
            _frames.Add(frame);
        }
    }

    /// <summary>
    /// Remove a frame from this container (does not destroy it).
    /// </summary>
    public void RemoveFrame(RTTMenuFrame frame)
    {
        if (frame == null) return;
        _frames.Remove(frame);

        if (frame == _mainFrame)
            _mainFrame = null;
    }

    /// <summary>
    /// Destroy and remove an app frame.
    /// </summary>
    public void DestroyFrame(RTTMenuFrame frame)
    {
        if (frame == null) return;
        if (frame == _mainFrame)
        {
            Debug.LogWarning("[RTTMenu] Cannot destroy main frame");
            return;
        }

        RemoveFrame(frame);
        Destroy(frame.gameObject);
    }

    /// <summary>
    /// Get the world size based on active content (ClusterRig or frame dimensions).
    /// Used by RTTMiniFrame for position tracking.
    /// Priority: ClusterRig > ActiveFrame
    /// </summary>
    public Vector2 GetWorldSize()
    {
        // Check ClusterRig first (streaming mode)
        if (_clusterRig != null && _clusterRig.gameObject.activeInHierarchy)
        {
            return _clusterRig.GetWorldSize();
        }

        var activeFrame = GetActiveFrame();
        if (activeFrame != null)
        {
            return new Vector2(activeFrame.PanelWidth, activeFrame.PanelHeight);
        }
        // Default fallback
        return new Vector2(1.6f, 0.9f);
    }

    /// <summary>
    /// Get the currently active (enabled) RTTMenuFrame.
    /// </summary>
    private RTTMenuFrame GetActiveFrame()
    {
        // Find the first active frame
        foreach (var frame in _frames)
        {
            if (frame != null && frame.gameObject.activeInHierarchy)
                return frame;
        }
        // Fallback to main frame
        return _mainFrame;
    }

    /// <summary>
    /// Find a frame by app ID.
    /// </summary>
    public RTTMenuFrame FindFrame(string appId)
    {
        string targetName = $"RTTMenuFrame_{appId}";
        foreach (var frame in _frames)
        {
            if (frame != null && frame.gameObject.name == targetName)
                return frame;
        }
        return null;
    }

    /// <summary>
    /// Set the WorldPanelClusterRig for remote streaming.
    /// Parents the rig inside RTTMenu and enables parent-origin positioning.
    /// </summary>
    public void SetClusterRig(WorldPanelClusterRig rig)
    {
        _clusterRig = rig;
        if (rig != null)
        {
            // Parent into RTTMenu
            rig.transform.SetParent(transform, false);
            rig.transform.localPosition = Vector3.zero;
            rig.transform.localRotation = Quaternion.identity;

            // Enable parent-origin positioning
            rig.useParentOrigin = true;

            Debug.Log("[RTTMenu] SetClusterRig: ClusterRig parented with useParentOrigin=true");
        }
    }

    /// <summary>
    /// Clear the ClusterRig reference.
    /// Does not destroy the rig - caller is responsible for cleanup.
    /// </summary>
    public void ClearClusterRig()
    {
        _clusterRig = null;
        Debug.Log("[RTTMenu] ClearClusterRig: ClusterRig reference cleared");
    }
    #endregion

    #region Static Factory
    /// <summary>
    /// Create an RTTMenu container.
    /// </summary>
    public static RTTMenu Create(Transform parent, string name = "RTTMenu")
    {
        GameObject menuObj = new GameObject(name);
        menuObj.transform.SetParent(parent, false);
        menuObj.transform.localPosition = Vector3.zero;
        menuObj.transform.localRotation = Quaternion.identity;

        RTTMenu menu = menuObj.AddComponent<RTTMenu>();
        return menu;
    }
    #endregion
}
