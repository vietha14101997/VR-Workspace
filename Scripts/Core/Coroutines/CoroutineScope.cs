using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace VRWorkspace.Core.Coroutines
{
    /// <summary>
    /// Lifecycle-bound coroutine scope, modeled after Kotlin's CoroutineScope.
    ///
    /// Provides structured concurrency: all coroutines launched within this scope
    /// are automatically cancelled when the scope is disposed.
    ///
    /// Thread safety: This class is designed for single-thread ownership (typically
    /// the Unity main thread). Launch/Async/Dispose should be called from the same
    /// thread. The CancellationToken itself is thread-safe for cross-thread signaling.
    ///
    /// When injected via VContainer as a singleton, do NOT call Dispose() manually.
    /// VContainer manages the lifecycle and will dispose it on LifetimeScope destruction.
    ///
    /// Usage:
    ///   var scope = new CoroutineScope(Dispatchers.Main);
    ///
    ///   // Fire-and-forget with exception safety
    ///   scope.Launch(async ct => {
    ///       var data = await Dispatchers.IO.RunAsync(
    ///           async () => await LoadDataAsync(), ct);
    ///       UpdateUI(data);
    ///   });
    ///
    ///   // Auto-cancels everything on dispose
    ///   scope.Dispose();
    /// </summary>
    public class CoroutineScope : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly CoroutineDispatcher _defaultDispatcher;
        private bool _disposed;

        /// <summary>
        /// Cancellation token tied to this scope's lifetime.
        /// Cancelled when scope is disposed.
        /// </summary>
        public CancellationToken Token => _cts.Token;

        /// <summary>
        /// The default dispatcher for this scope.
        /// </summary>
        public CoroutineDispatcher DefaultDispatcher => _defaultDispatcher;

        /// <summary>
        /// Whether the scope has been disposed/cancelled.
        /// </summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// Create a new scope with a default dispatcher.
        /// </summary>
        public CoroutineScope(CoroutineDispatcher defaultDispatcher = null)
        {
            _defaultDispatcher = defaultDispatcher ?? Dispatchers.Main;
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Create a child scope linked to a parent cancellation token.
        /// Cancelled when either parent or this scope is disposed.
        /// </summary>
        public CoroutineScope(CancellationToken parentToken, CoroutineDispatcher defaultDispatcher = null)
        {
            _defaultDispatcher = defaultDispatcher ?? Dispatchers.Main;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        }

        /// <summary>
        /// Launch a coroutine in this scope (fire-and-forget with exception safety).
        /// Equivalent to Kotlin's launch { }.
        ///
        /// Exceptions are logged to Debug.LogException instead of being silently swallowed.
        /// The coroutine is automatically cancelled when the scope is disposed.
        /// </summary>
        public void Launch(Func<CancellationToken, UniTask> block, CoroutineDispatcher dispatcher = null)
        {
            if (_disposed) return;

            var ct = _cts.Token;
            var target = dispatcher ?? _defaultDispatcher;

            LaunchInternal(target, block, ct).Forget();
        }

        /// <summary>
        /// Launch an async coroutine and return its result.
        /// Equivalent to Kotlin's async { }.
        ///
        /// Unlike Launch which swallows exceptions (logging them), Async propagates
        /// exceptions to the caller via the returned UniTask.
        /// </summary>
        public UniTask<T> Async<T>(Func<CancellationToken, UniTask<T>> block, CoroutineDispatcher dispatcher = null)
        {
            if (_disposed)
                return UniTask.FromException<T>(new ObjectDisposedException(nameof(CoroutineScope)));

            var ct = _cts.Token;
            var target = dispatcher ?? _defaultDispatcher;

            return target.RunAsync(async () => await block(ct), ct);
        }

        /// <summary>
        /// Switch to a different dispatcher, run the block, then return.
        /// Equivalent to Kotlin's withContext(dispatcher) { }.
        ///
        /// Usage:
        ///   var bytes = await CoroutineScope.WithContext(Dispatchers.IO,
        ///       async () => await File.ReadAllBytesAsync(path), ct);
        /// </summary>
        public static UniTask<T> WithContext<T>(
            CoroutineDispatcher dispatcher,
            Func<UniTask<T>> block,
            CancellationToken ct = default)
        {
            return dispatcher.RunAsync(block, ct);
        }

        /// <summary>
        /// Switch to a different dispatcher, run the block, then return.
        /// Void overload for side-effect operations.
        /// </summary>
        public static UniTask WithContext(
            CoroutineDispatcher dispatcher,
            Func<UniTask> block,
            CancellationToken ct = default)
        {
            return dispatcher.RunAsync(block, ct);
        }

        /// <summary>
        /// Switch to a different dispatcher, run a synchronous block, then return.
        /// Ideal for CPU-bound work that doesn't need async.
        /// </summary>
        public static UniTask<T> WithContext<T>(
            CoroutineDispatcher dispatcher,
            Func<T> block,
            CancellationToken ct = default)
        {
            return dispatcher.RunAsync(block, ct);
        }

        /// <summary>
        /// Switch to a different dispatcher, run a synchronous action, then return.
        /// </summary>
        public static UniTask WithContext(
            CoroutineDispatcher dispatcher,
            Action block,
            CancellationToken ct = default)
        {
            return dispatcher.RunAsync(block, ct);
        }

        /// <summary>
        /// Cancel all coroutines and release resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException) { }

            _cts.Dispose();
        }

        private static async UniTask LaunchInternal(
            CoroutineDispatcher dispatcher,
            Func<CancellationToken, UniTask> block,
            CancellationToken ct)
        {
            try
            {
                await dispatcher.RunAsync(async () => await block(ct), ct);
            }
            catch (OperationCanceledException)
            {
                // Expected when scope is disposed - do not log
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
