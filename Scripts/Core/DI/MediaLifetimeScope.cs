using VContainer;
using VContainer.Unity;

namespace VRWorkspace.DI
{
    /// <summary>
    /// Media feature scope. Registers media library, playlist, scanning services.
    /// Child of RTTLifetimeScope.
    /// </summary>
    public class MediaLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // Phase 3 will populate this with:
            // builder.Register<IMediaLibraryService, MediaLibraryService>(Lifetime.Singleton);
            // builder.Register<IMediaPlaylistService, MediaPlaylistService>(Lifetime.Singleton);
            // builder.Register<MediaLibraryScanner>(Lifetime.Singleton);
            // builder.Register<MediaCacheService>(Lifetime.Singleton);
        }
    }
}
