using VContainer;
using VContainer.Unity;
using VRWorkspace.Core;

namespace VRWorkspace.DI
{
    /// <summary>
    /// Root VContainer scope. Registers core cross-cutting services.
    /// Attach this to a GameObject in the startup scene with DontDestroyOnLoad.
    /// </summary>
    public class RootLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // Core services
            builder.Register<IMainThreadDispatcher>(resolver =>
                MainThreadDispatcher.Instance, Lifetime.Singleton);
        }
    }
}
