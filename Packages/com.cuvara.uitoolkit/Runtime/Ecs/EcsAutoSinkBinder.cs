namespace Cuvara.UIToolkit.Ecs
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Cuvara.UIToolkit.Flow;
    using Unity.Entities;

    /// <summary>
    /// Automatically binds <see cref="IViewModelSink{TViewModel}"/> interfaces on a presenter
    /// to their matching <see cref="EcsViewModelBridge{TComponent,TViewModel}"/> when the
    /// screen activates, and unbinds when it deactivates.
    /// </summary>
    /// <remarks>
    /// <para><b>The author writes zero registration code.</b> Implement
    /// <c>IViewModelSink&lt;T&gt;</c> on a presenter and the bridge is found and wired
    /// automatically. No <c>EcsSinkRegistration.Bind</c>, no scope wiring, no activation
    /// hook — just the interface.</para>
    ///
    /// <para><b>Binding on activate, not on open.</b> A suspended screen (covered by another)
    /// stops receiving pushes — no invisible work, structurally, rather than by convention.
    /// Resume re-binds, and the bridge's catch-up pass ensures the sink sees current state.</para>
    ///
    /// <para><b>Bridge discovery.</b> For each <c>IViewModelSink&lt;TViewModel&gt;</c> on the
    /// presenter, the binder scans the <see cref="World"/>'s managed systems for any
    /// <c>EcsViewModelBridge&lt;TComponent, TViewModel&gt;</c> with a matching
    /// <c>TViewModel</c>. This is a one-time reflection cost per presenter TYPE, cached for
    /// the session.</para>
    ///
    /// <para><b>Wire it once:</b></para>
    /// <code>
    /// var binder = new EcsAutoSinkBinder(World.DefaultGameObjectInjectionWorld);
    /// binder.Attach(navigator);
    /// // on scene teardown: binder.Detach();
    /// </code>
    /// </remarks>
    public sealed class EcsAutoSinkBinder : IDisposable
    {
        private readonly World world;

        /// <summary>Active sink bindings keyed by presenter instance.</summary>
        private readonly Dictionary<IUIToolkitScreenPresenter, List<IDisposable>> activeSinks = new();

        /// <summary>Cached per-type interface metadata so reflection runs once per type.</summary>
        private readonly Dictionary<Type, SinkInterfaceInfo[]> typeCache = new();

        private IScreenNavigator navigator;

        public EcsAutoSinkBinder(World world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>Subscribes to the navigator's activation events.</summary>
        public void Attach(IScreenNavigator nav)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            if (this.navigator != null) throw new InvalidOperationException("Already attached to a navigator.");

            this.navigator = nav;
            this.navigator.ScreenActivated += this.OnScreenActivated;
            this.navigator.ScreenDeactivated += this.OnScreenDeactivated;
        }

        /// <summary>Unsubscribes from the navigator and unbinds all active sinks.</summary>
        public void Detach()
        {
            if (this.navigator == null) return;

            this.navigator.ScreenActivated -= this.OnScreenActivated;
            this.navigator.ScreenDeactivated -= this.OnScreenDeactivated;
            this.navigator = null;

            foreach (var pair in this.activeSinks)
                foreach (var reg in pair.Value)
                    reg.Dispose();

            this.activeSinks.Clear();
        }

        public void Dispose() { this.Detach(); }

        private void OnScreenActivated(IUIToolkitScreenPresenter presenter)
        {
            if (presenter == null) return;

            var infos = this.GetSinkInterfaces(presenter.GetType());
            if (infos.Length == 0) return;

            var registrations = new List<IDisposable>(infos.Length);

            foreach (var info in infos)
            {
                var bridge = this.FindBridge(info.ViewModelType);
                if (bridge == null) continue;

                var registration = this.BindSink(bridge, presenter, info);
                if (registration != null) registrations.Add(registration);
            }

            if (registrations.Count > 0)
                this.activeSinks[presenter] = registrations;
        }

        private void OnScreenDeactivated(IUIToolkitScreenPresenter presenter)
        {
            if (presenter == null) return;

            if (!this.activeSinks.Remove(presenter, out var registrations)) return;

            foreach (var reg in registrations) reg.Dispose();
        }

        private SinkInterfaceInfo[] GetSinkInterfaces(Type presenterType)
        {
            if (this.typeCache.TryGetValue(presenterType, out var cached)) return cached;

            var interfaces = presenterType.GetInterfaces();
            var results = new List<SinkInterfaceInfo>();

            foreach (var iface in interfaces)
            {
                if (!iface.IsGenericType) continue;
                if (iface.GetGenericTypeDefinition() != typeof(IViewModelSink<>)) continue;

                var viewModelType = iface.GetGenericArguments()[0];
                results.Add(new SinkInterfaceInfo(iface, viewModelType));
            }

            var array = results.ToArray();
            this.typeCache[presenterType] = array;
            return array;
        }

        /// <summary>
        /// Finds an <c>EcsViewModelBridge&lt;TComponent, TViewModel&gt;</c> in the world
        /// whose <c>TViewModel</c> matches.
        /// </summary>
        private ComponentSystemBase FindBridge(Type viewModelType)
        {
            if (this.world == null || !this.world.IsCreated) return null;

            foreach (var system in this.world.Systems)
            {
                if (system == null) continue;

                var systemType = system.GetType();

                // Walk the inheritance chain to find EcsViewModelBridge<,>
                var current = systemType;
                while (current != null)
                {
                    if (current.IsGenericType &&
                        current.GetGenericTypeDefinition() == typeof(EcsViewModelBridge<,>))
                    {
                        var args = current.GetGenericArguments();
                        if (args[1] == viewModelType) return system;
                    }

                    current = current.BaseType;
                }
            }

            return null;
        }

        /// <summary>
        /// Calls <c>AddSink</c> on the bridge with the presenter as the sink, reflectively,
        /// and returns a registration that calls <c>RemoveSink</c> on dispose.
        /// </summary>
        private IDisposable BindSink(ComponentSystemBase bridge, object presenter, SinkInterfaceInfo info)
        {
            // bridge is EcsViewModelBridge<TComponent, TViewModel>
            // We need to call bridge.AddSink(presenter) and return something that calls RemoveSink
            var bridgeType = bridge.GetType();
            var addMethod = bridgeType.GetMethod("AddSink", BindingFlags.Public | BindingFlags.Instance);
            var removeMethod = bridgeType.GetMethod("RemoveSink", BindingFlags.Public | BindingFlags.Instance);

            if (addMethod == null || removeMethod == null) return null;

            addMethod.Invoke(bridge, new[] { presenter });

            return new SinkUnbinder(bridge, removeMethod, presenter);
        }

        private readonly struct SinkInterfaceInfo
        {
            public readonly Type InterfaceType;
            public readonly Type ViewModelType;

            public SinkInterfaceInfo(Type interfaceType, Type viewModelType)
            {
                this.InterfaceType = interfaceType;
                this.ViewModelType = viewModelType;
            }
        }

        private sealed class SinkUnbinder : IDisposable
        {
            private ComponentSystemBase bridge;
            private MethodInfo removeMethod;
            private object sink;

            public SinkUnbinder(ComponentSystemBase bridge, MethodInfo removeMethod, object sink)
            {
                this.bridge = bridge;
                this.removeMethod = removeMethod;
                this.sink = sink;
            }

            public void Dispose()
            {
                if (this.bridge == null) return;

                try { this.removeMethod.Invoke(this.bridge, new[] { this.sink }); }
                catch { /* bridge may be destroyed on world teardown */ }

                this.bridge = null;
                this.removeMethod = null;
                this.sink = null;
            }
        }
    }
}
