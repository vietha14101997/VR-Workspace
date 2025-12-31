using UnityEngine;

/// <summary>
/// Represents a running app instance with its own frame and controller.
/// Used by RTTManager to track active applications.
/// </summary>
public class RTTAppInstance
{
    /// <summary>
    /// Unique identifier for this app (e.g., "remote", "browser", "media")
    /// </summary>
    public string AppId { get; set; }

    /// <summary>
    /// The RTTMenuFrame containing this app's UI
    /// </summary>
    public RTTMenuFrame Frame { get; set; }

    /// <summary>
    /// The controller managing this app's logic (e.g., RTTRemoteMenuController)
    /// </summary>
    public MonoBehaviour Controller { get; set; }

    /// <summary>
    /// Which slot in Section2_Apps this app occupies (1-3, 0 is reserved for Home)
    /// </summary>
    public int TaskbarSlotIndex { get; set; }

    /// <summary>
    /// Whether this app's frame is currently visible
    /// </summary>
    public bool IsVisible { get; set; }

    /// <summary>
    /// The root GameObject of the menu content inside the frame
    /// </summary>
    public GameObject MenuContent { get; set; }

    /// <summary>
    /// Icon sprite for taskbar display
    /// </summary>
    public Sprite Icon { get; set; }

    public RTTAppInstance(string appId)
    {
        AppId = appId;
        IsVisible = false;
        TaskbarSlotIndex = -1;
    }
}
