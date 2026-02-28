using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace VRWorkspace.Core.Coroutines
{
    /// <summary>
    /// Thread pool dispatcher with configurable concurrency limits.
    /// Used by both Dispatchers.IO and Dispatchers.Default with different parameters.
    ///
    /// - IO: High concurrency (64+ threads) for blocking I/O operations.
    /// - Default: Low concurrency (processor-count) for CPU-bound work.
    ///
    /// The SemaphoreSlim limits how many blocks can execute concurrently,
    /// preventing thread pool starvation or CPU oversubscription.
    ///
    /// Note: The semaphore slot is held for the entire duration of the block,
    /// including any awaits within it. This is intentional throttling behavior
    /// to prevent overwhelming the system with too many concurrent operations.
    /// </summary>
    public sealed class ThreadPoolCoroutineDispatcher : CoroutineDispatcher
    {
        private readonly string _name;
        private readonly SemaphoreSlim _semaphore;

        public override string Name => _name;

        public ThreadPoolCoroutineDispatcher(string name, int maxConcurrency)
        {
            _name = name;
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        public override async UniTask RunAsync(Func<UniTask> block, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            bool acquired = false;
            try
            {
                await _semaphore.WaitAsync(ct);
                acquired = true;
                await UniTask.SwitchToThreadPool();
                ct.ThrowIfCancellationRequested();
                await block();
            }
            finally
            {
                if (acquired) _semaphore.Release();
            }
        }

        public override async UniTask<T> RunAsync<T>(Func<UniTask<T>> block, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            bool acquired = false;
            try
            {
                await _semaphore.WaitAsync(ct);
                acquired = true;
                await UniTask.SwitchToThreadPool();
                ct.ThrowIfCancellationRequested();
                return await block();
            }
            finally
            {
                if (acquired) _semaphore.Release();
            }
        }

        public override async UniTask RunAsync(Action block, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            bool acquired = false;
            try
            {
                await _semaphore.WaitAsync(ct);
                acquired = true;
                await UniTask.SwitchToThreadPool();
                ct.ThrowIfCancellationRequested();
                block();
            }
            finally
            {
                if (acquired) _semaphore.Release();
            }
        }

        public override async UniTask<T> RunAsync<T>(Func<T> block, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            bool acquired = false;
            try
            {
                await _semaphore.WaitAsync(ct);
                acquired = true;
                await UniTask.SwitchToThreadPool();
                ct.ThrowIfCancellationRequested();
                return block();
            }
            finally
            {
                if (acquired) _semaphore.Release();
            }
        }
    }
}
