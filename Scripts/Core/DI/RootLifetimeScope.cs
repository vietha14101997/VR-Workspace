using VContainer;
using VContainer.Unity;
using VRWorkspace.Core;
using VRWorkspace.Core.Coroutines;

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

            // Application-wide CoroutineScope (Kotlin-style structured concurrency)
            builder.Register<CoroutineScope>(resolver =>
                new CoroutineScope(Dispatchers.Main), Lifetime.Singleton);
        }
    }
}
