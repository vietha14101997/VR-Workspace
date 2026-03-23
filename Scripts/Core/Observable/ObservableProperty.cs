using System;
using System.Collections.Generic;

namespace VRWorkspace.Core
{
    /// <summary>
    /// A property that notifies subscribers when its value changes.
    /// All notifications are automatically marshalled to the Unity main thread.
    ///
    /// Usage:
    /// public ObservableProperty&lt;string&gt; Name { get; } = new();
    ///
    /// // Subscribe
    /// viewModel.Name.OnChanged += (name) => label.text = name;
    ///
    /// // Set value (from any thread)
    /// viewModel.Name.Value = "Hello";
    /// </summary>
    public class ObservableProperty<T>
    {
        private T _value;
        private readonly object _lock = new object();

        /// <summary>
        /// Event fired when the value changes. Always fires on the main thread.
        /// </summary>
        public event Action<T> OnChanged;

        /// <summary>
        /// Create with default value.
        /// </summary>
        public ObservableProperty()
        {
            _value = default;
        }

        /// <summary>
        /// Create with initial value.
        /// </summary>
        public ObservableProperty(T initialValue)
        {
            _value = initialValue;
        }

        /// <summary>
        /// Get or set the value. Setting triggers OnChanged if value differs.
        /// OnChanged always fires on the main thread.
        /// </summary>
        public T Value
        {
            get
            {
                lock (_lock)
                {
                    return _value;
                }
            }
            set
            {
                bool changed;
                T newValue;

                lock (_lock)
                {
                    // Check if value actually changed
                    if (EqualityComparer<T>.Default.Equals(_value, value))
                    {
                        return;
                    }

                    _value = value;
                    newValue = value;
                    changed = true;
                }

                if (changed)
                {
                    // Always notify on main thread
                    NotifyChanged(newValue);
                }
            }
        }

        /// <summary>
        /// Set value and force notification even if value is the same.
        /// </summary>
        public void SetAndNotify(T value)
        {
            lock (_lock)
            {
                _value = value;
            }
            NotifyChanged(value);
        }

        /// <summary>
        /// Force notification with current value.
        /// </summary>
        public void NotifyCurrent()
        {
            T current;
            lock (_lock)
            {
                current = _value;
            }
            NotifyChanged(current);
        }

        private void NotifyChanged(T value)
        {
            var handler = OnChanged;
            if (handler == null) return;

            MainThreadDispatcher.Enqueue(() => handler(value));
        }

        /// <summary>
        /// Implicit conversion to T for convenience.
        /// </summary>
        public static implicit operator T(ObservableProperty<T> prop) => prop.Value;

        public override string ToString() => Value?.ToString() ?? "null";
    }

    /// <summary>
    /// Observable property for collections that also notifies on add/remove.
    /// </summary>
    public class ObservableList<T>
    {
        private readonly List<T> _list = new List<T>();
        private readonly object _lock = new object();

        public event Action<IReadOnlyList<T>> OnChanged;
        public event Action<T> OnItemAdded;
        public event Action<T> OnItemRemoved;

        public int Count
        {
            get
            {
                lock (_lock) return _list.Count;
            }
        }

        public void Add(T item)
        {
            lock (_lock)
            {
                _list.Add(item);
            }

            MainThreadDispatcher.Enqueue(() =>
            {
                OnItemAdded?.Invoke(item);
                OnChanged?.Invoke(GetSnapshot());
            });
        }

        public bool Remove(T item)
        {
            bool removed;
            lock (_lock)
            {
                removed = _list.Remove(item);
            }

            if (removed)
            {
                MainThreadDispatcher.Enqueue(() =>
                {
                    OnItemRemoved?.Invoke(item);
                    OnChanged?.Invoke(GetSnapshot());
                });
            }

            return removed;
        }

        public void Clear()
        {
            List<T> oldItems;
            lock (_lock)
            {
                oldItems = new List<T>(_list);
                _list.Clear();
            }

            MainThreadDispatcher.Enqueue(() =>
            {
                foreach (var item in oldItems)
                {
                    OnItemRemoved?.Invoke(item);
                }
                OnChanged?.Invoke(GetSnapshot());
            });
        }

        public IReadOnlyList<T> GetSnapshot()
        {
            lock (_lock)
            {
                return new List<T>(_list);
            }
        }

        public T this[int index]
        {
            get
            {
                lock (_lock) return _list[index];
            }
        }
    }

    /// <summary>
    /// Command that can be executed and tracks its execution state.
    /// </summary>
    public class ObservableCommand
    {
        private readonly Func<System.Threading.Tasks.Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;
        private readonly object _lock = new object();

        public event Action<bool> OnCanExecuteChanged;
        public event Action OnExecuting;
        public event Action OnExecuted;
        public event Action<Exception> OnError;

        public bool IsExecuting
        {
            get { lock (_lock) return _isExecuting; }
        }

        public bool CanExecute
        {
            get
            {
                if (IsExecuting) return false;
                return _canExecute?.Invoke() ?? true;
            }
        }

        public ObservableCommand(Func<System.Threading.Tasks.Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public async System.Threading.Tasks.Task ExecuteAsync()
        {
            lock (_lock)
            {
                if (_isExecuting) return;
                if (!(_canExecute?.Invoke() ?? true)) return;
                _isExecuting = true;
            }

            MainThreadDispatcher.Enqueue(() =>
            {
                OnCanExecuteChanged?.Invoke(false);
                OnExecuting?.Invoke();
            });

            try
            {
                await _execute();

                MainThreadDispatcher.Enqueue(() => OnExecuted?.Invoke());
            }
            catch (Exception ex)
            {
                MainThreadDispatcher.Enqueue(() => OnError?.Invoke(ex));
                throw;
            }
            finally
            {
                lock (_lock)
                {
                    _isExecuting = false;
                }

                MainThreadDispatcher.Enqueue(() => OnCanExecuteChanged?.Invoke(CanExecute));
            }
        }

        public void RaiseCanExecuteChanged()
        {
            MainThreadDispatcher.Enqueue(() => OnCanExecuteChanged?.Invoke(CanExecute));
        }
    }
}
