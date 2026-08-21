namespace Cuvara.UIToolkit.Ecs
{
    using System;
    using Unity.Entities;

    /// <summary>
    /// Registers a sink with a bridge for as long as it is alive, and unregisters on
    /// <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>This exists because two lifetimes have to be joined and neither owns the
    /// other.</b> ECS systems live in a <see cref="World"/> and are created once at bootstrap.
    /// A screen's Presenter lives in a DI child scope, created when the screen opens and
    /// disposed when it closes. A Presenter that registers itself as a sink and forgets to
    /// unregister leaves the bridge pushing into a closed screen — which, because the sink
    /// keeps the Presenter alive, keeps the View alive, which keeps the visual tree alive.
    /// That is the standard UI leak, and it does not announce itself.</para>
    ///
    /// <para>Register this in a screen's child scope and let the scope dispose it. One
    /// <c>scope.Dispose()</c> then unhooks the sink, the Presenter's event subscriptions and
    /// the view together, rather than leaving each to be remembered separately:</para>
    /// <code>
    /// builder.Register&lt;HudPresenter&gt;(Lifetime.Scoped).AsSelf().AsImplementedInterfaces();
    /// builder.Register(container =&gt; EcsSinkRegistration.Bind(
    ///         container.Resolve&lt;HudBridge&gt;(),
    ///         container.Resolve&lt;HudPresenter&gt;()),
    ///     Lifetime.Scoped);
    /// </code>
    ///
    /// <para><b>Why this helper lives in <c>Runtime/Ecs/</c> and not in
    /// <c>Runtime/VContainer/</c>,</b> which is where composition helpers otherwise go: it
    /// names <see cref="EcsViewModelBridge{TComponent,TViewModel}"/>, so the assembly holding
    /// it must reference Entities. <c>Cuvara.UIToolkit.VContainer</c> is not gated on
    /// <c>CUVARA_UITOOLKIT_ENTITIES</c> and must keep compiling in a project with no Entities
    /// installed, so putting it there would make Entities a hard dependency of the whole
    /// package through the back door. This assembly is already gated; it is the correct home.
    /// Nothing here requires VContainer — it is a plain <see cref="IDisposable"/> that any
    /// container, or none, can own.</para>
    /// </remarks>
    public sealed class EcsSinkRegistration<TComponent, TViewModel> : IDisposable
        where TComponent : unmanaged, IComponentData
    {
        private readonly EcsViewModelBridge<TComponent, TViewModel> bridge;

        private IViewModelSink<TViewModel> sink;

        public EcsSinkRegistration(EcsViewModelBridge<TComponent, TViewModel> bridge, IViewModelSink<TViewModel> sink)
        {
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.sink   = sink ?? throw new ArgumentNullException(nameof(sink));

            this.bridge.AddSink(this.sink);
        }

        public void Dispose()
        {
            if (this.sink == null) return;

            this.bridge.RemoveSink(this.sink);
            this.sink = null;
        }
    }

    /// <summary>Type-inferring factory for <see cref="EcsSinkRegistration{TComponent,TViewModel}"/>.</summary>
    /// <remarks>
    /// Saves naming both type arguments at every call site, which for a bridge over a
    /// component and a ViewModel is the difference between a readable registration line and
    /// one that wraps.
    /// </remarks>
    public static class EcsSinkRegistration
    {
        public static EcsSinkRegistration<TComponent, TViewModel> Bind<TComponent, TViewModel>(
            EcsViewModelBridge<TComponent, TViewModel> bridge,
            IViewModelSink<TViewModel>                 sink)
            where TComponent : unmanaged, IComponentData
        {
            return new(bridge, sink);
        }
    }
}
