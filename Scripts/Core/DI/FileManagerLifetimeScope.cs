using VContainer;
using VContainer.Unity;

namespace VRWorkspace.DI
{
    /// <summary>
    /// File Manager feature scope. Registers file operations, metadata, thumbnail services.
    /// Child of RTTLifetimeScope.
    /// </summary>
    public class FileManagerLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // Phase 3 will populate this with:
            // builder.Register<IFileOperationService, FileOperationService>(Lifetime.Singleton);
            // builder.Register<IFileMetadataService, FileMetadataService>(Lifetime.Singleton);
            // builder.Register<IFileThumbnailService, FileThumbnailService>(Lifetime.Singleton);
        }
    }
}
