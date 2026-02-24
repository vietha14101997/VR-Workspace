namespace VRWorkspace.Domain.RTT
{
    /// <summary>
    /// State caching for apps that support it (File Manager, Media Library).
    /// Split from IDataBindable for Interface Segregation.
    /// </summary>
    public interface IAppStateCacheable
    {
        bool SupportsStateCaching { get; }
        bool TryRestoreCachedState();
        void CacheCurrentState();
        object GetPreparedDataBuffer();
        void BindPreparedData(object dataBuffer);
    }
}
