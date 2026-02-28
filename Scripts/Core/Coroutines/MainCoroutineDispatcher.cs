using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace VRWorkspace.Core.Coroutines
{
    /// <summary>
    /// Dispatches work to the Unity main thread via UniTask's PlayerLoop integration.
    /// Equivalent to Kotlin's Dispatchers.Main.
    ///
    /// All Unity API calls (Texture2D, UI, MonoBehaviour, etc.) must run on this dispatcher.
    /// If already on the main thread, executes immediately without queueing.
    ///
    /// Warning: The Action/Func overloads run synchronously on the main thread.
    /// Keep them short to avoid frame stalls. Use the async Func&lt;UniTask&gt; overloads
    /// for operations that may take multiple frames.
    /// </summary>
    public sealed class MainCoroutineDispatcher : CoroutineDispatcher
    {
        public override string Name => "Main";

        public override async UniTask RunAsync(Func<UniTask> block, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await UniTask.SwitchToMainThread(ct);
            await block();
        }

        public override async UniTask<T> RunAsync<T>(Func<UniTask<T>> block, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await UniTask.SwitchToMainThread(ct);
            return await block();
        }

        public override async UniTask RunAsync(Action block, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await UniTask.SwitchToMainThread(ct);
            block();
        }

        public override async UniTask<T> RunAsync<T>(Func<T> block, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await UniTask.SwitchToMainThread(ct);
            return block();
        }
    }
}
