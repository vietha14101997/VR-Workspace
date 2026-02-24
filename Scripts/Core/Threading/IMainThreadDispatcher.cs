using System;
using System.Threading.Tasks;

namespace VRWorkspace.Core
{
    /// <summary>
    /// Main thread dispatcher contract. For cross-thread Unity main thread access.
    /// </summary>
    public interface IMainThreadDispatcher
    {
        void Enqueue(Action action);
        Task<T> EnqueueAsync<T>(Func<T> func);
        Task EnqueueAsync(Action action);
    }
}
