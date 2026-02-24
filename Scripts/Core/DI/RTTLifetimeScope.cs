using VContainer;
using VContainer.Unity;
using VRWorkspace.Domain.RTT;

namespace VRWorkspace.DI
{
    /// <summary>
    /// RTT system scope. Registers panel management, theme, app lifecycle services.
    /// Child of RootLifetimeScope.
    /// </summary>
    public class RTTLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // RTT services will be registered here as they are extracted
            // Phase 3 will populate this with:
            // builder.Register<IRTTThemeProvider, RTTThemeManager>(Lifetime.Singleton);
            // builder.Register<IRTTAppContentFactory, RemoteAppContentFactory>(Lifetime.Singleton);
            // builder.Register<IRTTAppContentFactory, MediaAppContentFactory>(Lifetime.Singleton);
            // builder.Register<IRTTAppContentFactory, FilesAppContentFactory>(Lifetime.Singleton);
        }
    }
}
