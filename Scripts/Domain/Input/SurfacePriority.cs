namespace VRWorkspace.Domain.Input
{
    /// <summary>
    /// Standard priority values used by VCS surface registration.
    /// Higher number = higher priority (used to resolve overlap, initial cursor placement).
    /// </summary>
    public static class SurfacePriority
    {
        /// <summary>Lowest. Side-panels and decorative surfaces.</summary>
        public const int Decorator = 50;

        /// <summary>Standard RTT panels (Main, Pagination, Taskbar).</summary>
        public const int Standard = 100;

        /// <summary>Slightly above standard. Side panels tied to Main Menu.</summary>
        public const int SidePanel = 110;

        /// <summary>Higher than standard. Modal surfaces (Keyboard).</summary>
        public const int Modal = 150;

        /// <summary>Highest. Popups, dialogs (click-outside-to-close).</summary>
        public const int Popup = 200;

        /// <summary>Fallback when priority unspecified.</summary>
        public const int Default = Standard;
    }
}
