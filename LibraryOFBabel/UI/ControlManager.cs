
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOFBabel.UI
{
    /// <summary>
    /// Optional interface controls can implement to receive lifecycle callbacks
    /// when they are embedded or removed by the <see cref="ControlManager"/>.
    /// </summary>
    public interface IEmbeddableControl
    {
        void OnAttached();
        void OnDetached();
    }

    /// <summary>
    /// Convenience base class you can inherit for embeddable controls.
    /// </summary>
    public abstract class EmbeddableUserControl : UserControl, IEmbeddableControl
    {
        protected EmbeddableUserControl() { }

        public virtual void OnAttached() { }

        public virtual void OnDetached() { }
    }

    /// <summary>
    /// Arguments provided when the active control is swapped.
    /// </summary>
    public sealed class ControlSwappedEventArgs : EventArgs
    {
        public string? PreviousKey { get; }
        public Control? PreviousControl { get; }
        public string CurrentKey { get; }
        public Control CurrentControl { get; }

        public ControlSwappedEventArgs(string? previousKey, Control? previousControl, string currentKey, Control currentControl)
        {
            PreviousKey = previousKey;
            PreviousControl = previousControl;
            CurrentKey = currentKey ?? throw new ArgumentNullException(nameof(currentKey));
            CurrentControl = currentControl ?? throw new ArgumentNullException(nameof(currentControl));
        }
    }

    /// <summary>
    /// Manages registering, lazy-creating, embedding and swapping UserControls inside a host <see cref="Panel"/>.
    /// Encapsulates lifecycle and ownership rules and exposes events for polymorphic integration.
    /// </summary>
    public sealed class ControlManager : IDisposable
    {
        private readonly Panel hostPanel;
        private readonly ToolTip? sharedToolTip;
        private readonly Dictionary<string, Func<Control>> factories = new();
        private readonly Dictionary<string, Control> instances = new();
        private readonly HashSet<string> ownedInstances = new(); // keys we should dispose
        private string? currentKey;
        private bool disposed;

        public event EventHandler<ControlSwappedEventArgs>? ControlSwapped;

        public ControlManager(Panel hostPanel, ToolTip? sharedToolTip = null)
        {
            this.hostPanel = hostPanel ?? throw new ArgumentNullException(nameof(hostPanel));
            this.sharedToolTip = sharedToolTip;
        }

        /// <summary>
        /// Read-only snapshot of registered keys.
        /// </summary>
        public IReadOnlyCollection<string> RegisteredKeys => factories.Keys.ToList().AsReadOnly();

        /// <summary>
        /// Currently visible control key (or null).
        /// </summary>
        public string? CurrentKey => currentKey;

        /// <summary>
        /// Currently visible control instance (or null).
        /// </summary>
        public Control? CurrentControl => currentKey != null && instances.TryGetValue(currentKey, out var c) ? c : null;

        /// <summary>
        /// Register an existing control under <paramref name="key"/>. If <paramref name="takeOwnership"/>
        /// is true the manager will dispose it when unregistered or disposed.
        /// </summary>
        public void RegisterControl(string key, Control control, bool takeOwnership = false)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key required", nameof(key));
            if (control is null) throw new ArgumentNullException(nameof(control));
            ThrowIfDisposed();
            if (factories.ContainsKey(key)) throw new InvalidOperationException($"Key '{key}' already registered.");

            factories[key] = () => control;
            instances[key] = control;
            if (takeOwnership) ownedInstances.Add(key);
        }

        /// <summary>
        /// Register a factory that will be invoked the first time the control is shown.
        /// If <paramref name="ownsCreatedInstance"/> is true the manager will dispose the created instance.
        /// </summary>
        public void RegisterFactory(string key, Func<Control> factory, bool ownsCreatedInstance = true)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key required", nameof(key));
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            ThrowIfDisposed();
            if (factories.ContainsKey(key)) throw new InvalidOperationException($"Key '{key}' already registered.");

            factories[key] = factory;
            if (ownsCreatedInstance) ownedInstances.Add(key);
        }

        /// <summary>
        /// Show the control registered under <paramref name="key"/>. Returns false if the key is unknown.
        /// This method marshals to the host's UI thread if necessary.
        /// </summary>
        public bool Show(string key, bool bringToFront = true)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key required", nameof(key));
            ThrowIfDisposed();

            if (hostPanel.InvokeRequired)
            {
                return (bool)hostPanel.Invoke(new Func<bool>(() => ShowInternal(key, bringToFront)));
            }
            return ShowInternal(key, bringToFront);
        }

        private bool ShowInternal(string key, bool bringToFront)
        {
            if (!factories.TryGetValue(key, out var factory)) return false;

            var control = instances.TryGetValue(key, out var existing) ? existing : CreateInstance(key, factory);

            // call OnDetached on previous
            Control? prevControl = null;
            string? prevKey = currentKey;
            if (prevKey != null && instances.TryGetValue(prevKey, out prevControl))
            {
                if (prevControl is IEmbeddableControl embPrev) embPrev.OnDetached();
            }

            hostPanel.SuspendLayout();
            try
            {
                hostPanel.Controls.Clear();
                control.Dock = DockStyle.Fill;
                hostPanel.Controls.Add(control);
                if (bringToFront) control.BringToFront();
            }
            finally
            {
                hostPanel.ResumeLayout(true);
            }

            if (control is IEmbeddableControl embNew) embNew.OnAttached();

            currentKey = key;
            ControlSwapped?.Invoke(this, new ControlSwappedEventArgs(prevKey, prevControl, key, control));
            return true;
        }

        private Control CreateInstance(string key, Func<Control> factory)
        {
            var ctrl = factory() ?? throw new InvalidOperationException($"Factory for '{key}' returned null.");
            instances[key] = ctrl;
            return ctrl;
        }

        /// <summary>
        /// Try to return an already-created instance for the given key without changing the visible control.
        /// Returns the instance or null if it hasn't been created/registered.
        /// This does not alter currently visible control.
        /// </summary>
        public Control? GetInstance(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key required", nameof(key));
            ThrowIfDisposed();

            if (hostPanel.InvokeRequired)
            {
                return (Control?)hostPanel.Invoke(new Func<Control?>(() => instances.TryGetValue(key, out var c) ? c : null));
            }

            return instances.TryGetValue(key, out var ctrl) ? ctrl : null;
        }

        /// <summary>
        /// Unregisters the control/factory associated with <paramref name="key"/>.
        /// If the control instance is owned by the manager it will be disposed.
        /// Returns true when something was removed.
        /// </summary>
        public bool Unregister(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key required", nameof(key));
            ThrowIfDisposed();

            if (hostPanel.InvokeRequired)
            {
                return (bool)hostPanel.Invoke(new Func<bool>(() => UnregisterInternal(key)));
            }
            return UnregisterInternal(key);
        }

        private bool UnregisterInternal(string key)
        {
            var removed = false;

            if (instances.TryGetValue(key, out var ctrl))
            {
                if (ctrl.Parent == hostPanel)
                {
                    if (ctrl is IEmbeddableControl emb) emb.OnDetached();
                    hostPanel.Controls.Remove(ctrl);
                }

                if (ownedInstances.Contains(key))
                {
                    try { ctrl.Dispose(); } catch { /* swallow disposal exceptions */ }
                    ownedInstances.Remove(key);
                }

                instances.Remove(key);
                removed = true;
            }

            if (factories.Remove(key)) removed = true;

            if (currentKey == key) currentKey = null;

            return removed;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ControlManager));
        }

        /// <summary>
        /// Dispose manager and any owned control instances.
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            if (hostPanel.InvokeRequired)
            {
                hostPanel.Invoke(new Action(DisposeInternal));
            }
            else
            {
                DisposeInternal();
            }
            disposed = true;
            GC.SuppressFinalize(this);
        }

        private void DisposeInternal()
        {
            hostPanel.Controls.Clear();

            foreach (var kv in instances.ToList())
            {
                var key = kv.Key;
                var ctrl = kv.Value;
                if (ownedInstances.Contains(key))
                {
                    try { ctrl.Dispose(); } catch { /* ignore */ }
                }
            }

            factories.Clear();
            instances.Clear();
            ownedInstances.Clear();
            currentKey = null;
        }
    }
}