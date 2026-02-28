using VContainer;
using VContainer.Unity;
using VRWorkspace.ViewModels;

namespace VRWorkspace.DI
{
    /// <summary>
    /// Streaming feature scope. Registers streaming client and connection ViewModel.
    /// Child of RTTLifetimeScope.
    /// </summary>
    public class StreamingLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // ConnectionViewModel - replaces ServiceLocator.Get<ConnectionViewModel>()
            builder.Register<ConnectionViewModel>(Lifetime.Singleton);
        }
    }
}
