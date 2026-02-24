using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRWorkspace.Core
{
    /// <summary>
    /// [DEPRECATED] Use VContainer dependency injection instead.
    /// This class is kept for backward compatibility during migration.
    /// Inject dependencies via LifetimeScope registrations and [Inject] attributes.
    /// </summary>
    [System.Obsolete("Use VContainer dependency injection instead. See Core/DI/ for LifetimeScopes.")]
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private static readonly Dictionary<Type, Func<object>> _factories = new Dictionary<Type, Func<object>>();
        private static readonly object _lock = new object();

        /// <summary>
        /// Register a service instance.
        /// </summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            lock (_lock)
            {
                var type = typeof(T);
                if (_services.ContainsKey(type))
                {
                    Debug.LogWarning($"[ServiceLocator] Overwriting existing service: {type.Name}");
                }
                _services[type] = service;
                Debug.Log($"[ServiceLocator] Registered: {type.Name}");
            }
        }

        /// <summary>
        /// Register a factory function for lazy instantiation.
        /// </summary>
        public static void RegisterFactory<T>(Func<T> factory) where T : class
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            lock (_lock)
            {
                _factories[typeof(T)] = () => factory();
            }
        }

        /// <summary>
        /// Register a service as a specific interface type.
        /// </summary>
        public static void Register<TInterface, TImplementation>(TImplementation service)
            where TInterface : class
            where TImplementation : class, TInterface
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            lock (_lock)
            {
                _services[typeof(TInterface)] = service;
                Debug.Log($"[ServiceLocator] Registered: {typeof(TInterface).Name} -> {typeof(TImplementation).Name}");
            }
        }

        /// <summary>
        /// Get a registered service. Throws if not found.
        /// </summary>
        public static T Get<T>() where T : class
        {
            if (TryGet<T>(out var service))
            {
                return service;
            }

            throw new InvalidOperationException($"Service not registered: {typeof(T).Name}");
        }

        /// <summary>
        /// Try to get a registered service.
        /// </summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            lock (_lock)
            {
                var type = typeof(T);

                // Check if already instantiated
                if (_services.TryGetValue(type, out var obj))
                {
                    service = obj as T;
                    return service != null;
                }

                // Check for factory
                if (_factories.TryGetValue(type, out var factory))
                {
                    var instance = factory() as T;
                    if (instance != null)
                    {
                        _services[type] = instance;
                        _factories.Remove(type);
                        service = instance;
                        return true;
                    }
                }

                service = null;
                return false;
            }
        }

        /// <summary>
        /// Get or create a service. Uses factory if registered, otherwise creates with default constructor.
        /// </summary>
        public static T GetOrCreate<T>() where T : class, new()
        {
            lock (_lock)
            {
                if (TryGet<T>(out var existing))
                {
                    return existing;
                }

                var instance = new T();
                _services[typeof(T)] = instance;
                Debug.Log($"[ServiceLocator] Created: {typeof(T).Name}");
                return instance;
            }
        }

        /// <summary>
        /// Check if a service is registered.
        /// </summary>
        public static bool IsRegistered<T>() where T : class
        {
            lock (_lock)
            {
                return _services.ContainsKey(typeof(T)) || _factories.ContainsKey(typeof(T));
            }
        }

        /// <summary>
        /// Unregister a service.
        /// </summary>
        public static void Unregister<T>() where T : class
        {
            lock (_lock)
            {
                var type = typeof(T);
                if (_services.TryGetValue(type, out var service))
                {
                    _services.Remove(type);

                    // Dispose if possible
                    if (service is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[ServiceLocator] Error disposing {type.Name}: {ex.Message}");
                        }
                    }

                    Debug.Log($"[ServiceLocator] Unregistered: {type.Name}");
                }
                _factories.Remove(type);
            }
        }

        /// <summary>
        /// Clear all registered services. Disposes any IDisposable services.
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                // Dispose all disposable services
                foreach (var kvp in _services)
                {
                    if (kvp.Value is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[ServiceLocator] Error disposing {kvp.Key.Name}: {ex.Message}");
                        }
                    }
                }

                _services.Clear();
                _factories.Clear();
                Debug.Log("[ServiceLocator] Cleared all services");
            }
        }

        /// <summary>
        /// Get list of all registered service types.
        /// </summary>
        public static IReadOnlyList<Type> GetRegisteredTypes()
        {
            lock (_lock)
            {
                var types = new List<Type>();
                types.AddRange(_services.Keys);
                types.AddRange(_factories.Keys);
                return types;
            }
        }
    }

    /// <summary>
    /// [DEPRECATED] Use VContainer RootLifetimeScope instead.
    /// </summary>
    [System.Obsolete("Use VContainer RootLifetimeScope instead. See Core/DI/RootLifetimeScope.cs.")]
    public class ServiceBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad()
        {
            // MainThreadDispatcher initializes itself
            MainThreadDispatcher.Initialize();
        }

        void Awake()
        {
            // Register core services here
            // Example:
            // ServiceLocator.Register(new ConnectionViewModel());
        }

        void OnDestroy()
        {
            // Cleanup services when app closes
            ServiceLocator.Clear();
        }
    }
}
