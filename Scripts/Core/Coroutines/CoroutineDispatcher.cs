using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace VRWorkspace.Core.Coroutines
{
    /// <summary>
    /// Abstract base class for coroutine dispatchers, modeled after Kotlin's CoroutineDispatcher.
    /// Each dispatcher determines which thread or thread pool a block of code runs on.
    ///
    /// Usage:
    ///   await Dispatchers.IO.RunAsync(async () => await DownloadFileAsync(), ct);
    ///   var result = await Dispatchers.Default.RunAsync(() => ComputeHash(data), ct);
    /// </summary>
    public abstract class CoroutineDispatcher
    {
        /// <summary>
        /// Dispatcher name for logging/debugging.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Run an async block on this dispatcher's thread/pool.
        /// </summary>
        public abstract UniTask RunAsync(Func<UniTask> block, CancellationToken ct = default);

        /// <summary>
        /// Run an async block that returns a value on this dispatcher's thread/pool.
        /// </summary>
        public abstract UniTask<T> RunAsync<T>(Func<UniTask<T>> block, CancellationToken ct = default);

        /// <summary>
        /// Run a synchronous action on this dispatcher's thread/pool.
        /// Ideal for CPU-bound or blocking I/O work.
        /// </summary>
        public abstract UniTask RunAsync(Action block, CancellationToken ct = default);

        /// <summary>
        /// Run a synchronous function that returns a value on this dispatcher's thread/pool.
        /// Ideal for CPU-bound computations.
        /// </summary>
        public abstract UniTask<T> RunAsync<T>(Func<T> block, CancellationToken ct = default);

        public override string ToString() => $"Dispatcher[{Name}]";
    }
}
