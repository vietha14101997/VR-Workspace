namespace VRWorkspace.Domain.Input
{
    /// <summary>
    /// Flags for which edges a surface allows cursor traversal through.
    /// Default per-surface-type (Decision 2):
    /// Main/Side/Pagination/Taskbar = All,
    /// Popup = None,
    /// Keyboard = Up only.
    /// </summary>
    [System.Flags]
    public enum EdgePolicy
    {
        None  = 0,
        Left  = 1 << 0,
        Right = 1 << 1,
        Up    = 1 << 2,
        Down  = 1 << 3,
        All   = Left | Right | Up | Down
    }

    /// <summary>
    /// Cardinal edge direction a cursor attempts to traverse.
    /// </summary>
    public enum EdgeDirection
    {
        Left,
        Right,
        Up,
        Down
    }
}
