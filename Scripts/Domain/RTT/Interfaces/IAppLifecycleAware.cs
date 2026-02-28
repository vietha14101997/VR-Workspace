using System.Collections.Generic;

namespace VRWorkspace.Domain.RTT
{
    /// <summary>
    /// UI lifecycle hooks for apps that need transition coordination.
    /// Split from IDataBindable for Interface Segregation.
    /// </summary>
    public interface IAppLifecycleAware
    {
        void ShowLoadingSpinner();
        void HideLoadingSpinner();
        void OnAppShown();
    }
}
