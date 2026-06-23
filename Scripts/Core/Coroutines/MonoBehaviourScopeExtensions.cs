using System.Collections.Generic;
using UnityEngine;

namespace VRWorkspace.Core.Coroutines
{
    /// <summary>
    /// Extension methods to bind CoroutineScope to MonoBehaviour lifecycle.
    /// The scope is automatically cancelled when the MonoBehaviour is destroyed.
    ///
    /// Usage:
    ///   public class MyController : MonoBehaviour
    ///   {
    ///       private CoroutineScope _scope;
    ///
    ///       void Awake()
    ///       {
    ///           _scope = this.GetCoroutineScope();
    ///       }
    ///
    ///       void Start()
    ///       {
    ///           _scope.Launch(async ct => {
    ///               var data = await CoroutineScope.WithContext(Dispatchers.IO, async () =>
    ///                   await LoadAsync(), ct);
    ///               label.text = data;
    ///           });
    ///       }
    ///       // No need for OnDestroy cleanup - scope auto-cancels!
    ///   }
    /// </summary>
    public static class MonoBehaviourScopeExtensions
    {
        /// <summary>
        /// Get or create a CoroutineScope tied to this MonoBehaviour's lifetime.
        /// The scope uses Dispatchers.Main by default and auto-cancels on OnDestroy.
        /// Returns the same scope instance on subsequent calls.
        /// </summary>
        public static CoroutineScope GetCoroutineScope(
            this MonoBehaviour mono,
            CoroutineDispatcher defaultDispatcher = null)
        {
            var holder = mono.GetComponent<ScopeHolder>();
            if (holder == null)
            {
                holder = mono.gameObject.AddComponent<ScopeHolder>();
            }

            return holder.GetOrCreateScope(mono, defaultDispatcher);
        }
    }

    /// <summary>
    /// Hidden component that holds and manages CoroutineScope lifecycle.
    /// Automatically added by GetCoroutineScope() extension.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class ScopeHolder : MonoBehaviour
    {
        private readonly Dictionary<UnityEngine.EntityId, CoroutineScope> _scopes = new Dictionary<UnityEngine.EntityId, CoroutineScope>();

        internal CoroutineScope GetOrCreateScope(MonoBehaviour owner, CoroutineDispatcher dispatcher)
        {
            UnityEngine.EntityId id = owner.GetEntityId();

            if (_scopes.TryGetValue(id, out var existing) && !existing.IsDisposed)
                return existing;

            var scope = new CoroutineScope(dispatcher ?? Dispatchers.Main);
            _scopes[id] = scope;
            return scope;
        }

        private void OnDestroy()
        {
            foreach (var scope in _scopes.Values)
            {
                scope.Dispose();
            }
            _scopes.Clear();
        }
    }
}
