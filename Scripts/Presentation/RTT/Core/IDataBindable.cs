using System;
using System.Collections.Generic;
using VRWorkspace.Domain.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Composite interface for backward compatibility.
    /// New code should depend on the split interfaces:
    /// - IAppDataLoader: core data loading
    /// - IAppLifecycleAware: UI lifecycle hooks
    /// - IAppStateCacheable: state caching
    /// </summary>
    public interface IDataBindable : IAppDataLoader, IAppLifecycleAware, IAppStateCacheable
    {
        /// <summary>
        /// Get all frames (main + side panels) for coordinated fade animation.
        /// </summary>
        List<RTTMenuFrame> GetAllFrames();
    }

}
