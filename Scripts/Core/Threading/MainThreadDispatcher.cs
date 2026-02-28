using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VRWorkspace.Core
{
    /// <summary>
    /// Dispatches actions to the Unity main thread from background threads.
    /// Essential for thread-safe UI updates when receiving WebSocket/WebRTC callbacks.
    ///
    /// Usage:
    /// - MainThreadDispatcher.Enqueue(() => UpdateUI(data));
    /// - await MainThreadDispatcher.EnqueueAsync(() => GetSomething());
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour, IMainThreadDispatcher
    {
        private static MainThreadDispatcher _instance;
        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private static int _mainThreadId;
        private static bool _initialized;

        /// <summary>
        /// Check if current thread is the Unity main thread.
        /// </summary>
        public static bool IsMainThread => _initialized && Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>
        /// Get the singleton instance. Creates one if needed.
        /// </summary>
        public static MainThreadDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    Initialize();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Initialize the dispatcher. Called automatically on first use.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (_instance != null) return;

            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _initialized = true;

            var go = new GameObject("[MainThreadDispatcher]");
            _instance = go.AddComponent<MainThreadDispatcher>();
            DontDestroyOnLoad(go);

            Debug.Log($"[MainThreadDispatcher] Initialized on thread {_mainThreadId}");
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _initialized = true;
        }

        void Update()
        {
            // Process all queued actions
            int processed = 0;
            while (_queue.TryDequeue(out var action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
                processed++;

                // Limit per frame to avoid blocking
                if (processed > 100)
                {
                    Debug.LogWarning("[MainThreadDispatcher] Too many queued actions, deferring rest to next frame");
                    break;
                }
            }
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                _initialized = false;
            }
        }

        /// <summary>
        /// Enqueue an action to run on the main thread.
        /// If already on main thread, executes immediately.
        /// </summary>
        public static void Enqueue(Action action)
        {
            if (action == null) return;

            if (IsMainThread)
            {
                // Already on main thread, execute immediately
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            else
            {
                // Queue for main thread execution
                _queue.Enqueue(action);
            }
        }

        /// <summary>
        /// Enqueue an action and wait for completion.
        /// Returns a Task that completes when the action has run on main thread.
        /// </summary>
        public static Task EnqueueAsync(Action action)
        {
            if (action == null) return Task.CompletedTask;

            if (IsMainThread)
            {
                try
                {
                    action();
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
            }

            var tcs = new TaskCompletionSource<bool>();
            _queue.Enqueue(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        /// <summary>
        /// Enqueue a function and wait for its result.
        /// Returns a Task&lt;T&gt; that completes with the function's return value.
        /// </summary>
        public static Task<T> EnqueueAsync<T>(Func<T> func)
        {
            if (func == null) return Task.FromResult(default(T));

            if (IsMainThread)
            {
                try
                {
                    return Task.FromResult(func());
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }
            }

            var tcs = new TaskCompletionSource<T>();
            _queue.Enqueue(() =>
            {
                try
                {
                    var result = func();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        /// <summary>
        /// Get the number of pending actions in the queue.
        /// </summary>
        public static int PendingCount => _queue.Count;

        #region IMainThreadDispatcher (instance methods delegating to static)
        void IMainThreadDispatcher.Enqueue(Action action) => Enqueue(action);
        Task IMainThreadDispatcher.EnqueueAsync(Action action) => EnqueueAsync(action);
        Task<T> IMainThreadDispatcher.EnqueueAsync<T>(Func<T> func) => EnqueueAsync(func);
        #endregion
    }
}
