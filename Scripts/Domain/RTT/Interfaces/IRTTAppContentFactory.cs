namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Factory interface for creating app content. Replaces switch/case (OCP fix).
    /// Each app type registers its own factory.
    /// </summary>
    public interface IRTTAppContentFactory
    {
        /// <summary>The app type this factory handles.</summary>
        RTTAppRegistry.AppType AppType { get; }

        /// <summary>Create and wire up app content for the given instance.</summary>
        void CreateContent(RTTAppInstance instance, AppContentContext context);
    }

    /// <summary>
    /// Context passed to app content factories during creation.
    /// </summary>
    public class AppContentContext
    {
        public UnityEngine.Transform ParentTransform { get; set; }
        public TMPro.TMP_FontAsset Font { get; set; }
        public UnityEngine.Color PrimaryColor { get; set; }
        public UnityEngine.Color AccentColor { get; set; }
        public System.Action<string> OnCloseApp { get; set; }
    }
}
