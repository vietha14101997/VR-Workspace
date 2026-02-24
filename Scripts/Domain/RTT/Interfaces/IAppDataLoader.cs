using System;

namespace VRWorkspace.Domain.RTT
{
    /// <summary>
    /// Core data loading contract. All apps implement this.
    /// Split from IDataBindable for Interface Segregation.
    /// </summary>
    public interface IAppDataLoader
    {
        bool IsDataReady { get; }
        bool IsPreparingData { get; }
        void PrepareDataAsync();
        void BindCachedDataOrEmpty();
        void OnBackgroundDataReady();
        event Action OnDataPrepared;
    }
}
