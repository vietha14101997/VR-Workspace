using System;

namespace VRWorkspace.Core.Coroutines
{
    /// <summary>
    /// Static entry point for coroutine dispatchers, modeled after Kotlin's Dispatchers object.
    ///
    /// Three dispatchers for different workload types:
    ///
    /// - Dispatchers.Main   -> Unity main thread. Use for UI updates, Unity API calls.
    /// - Dispatchers.IO     -> High-concurrency thread pool. Use for network, file I/O, WebSocket.
    /// - Dispatchers.Default -> CPU-bound thread pool. Use for computation, parsing, decoding.
    ///
    /// Prefer these over legacy MainThreadDispatcher for new code.
    ///
    /// Usage:
    ///   var data = await Dispatchers.IO.RunAsync(async () => await FetchDataAsync(), ct);
    ///   await Dispatchers.Main.RunAsync(() => label.text = data, ct);
    /// </summary>
    public static class Dispatchers
    {
        /// <summary>
        /// Unity main thread dispatcher. All Unity API calls must use this.
        /// Equivalent to Kotlin's Dispatchers.Main.
        /// </summary>
        public static readonly CoroutineDispatcher Main = new MainCoroutineDispatcher();

        /// <summary>
        /// I/O-optimized thread pool (high concurrency, 64+ threads).
        /// Use for file I/O, network calls, WebSocket, database operations.
        /// Equivalent to Kotlin's Dispatchers.IO.
        /// </summary>
        public static readonly CoroutineDispatcher IO =
            new ThreadPoolCoroutineDispatcher("IO", Math.Max(64, Environment.ProcessorCount * 4));

        /// <summary>
        /// CPU-optimized thread pool (processor-count threads).
        /// Use for computation, parsing, image decoding, hashing.
        /// Equivalent to Kotlin's Dispatchers.Default.
        /// </summary>
        public static readonly CoroutineDispatcher Default =
            new ThreadPoolCoroutineDispatcher("Default", Math.Max(2, Environment.ProcessorCount));
    }
}
