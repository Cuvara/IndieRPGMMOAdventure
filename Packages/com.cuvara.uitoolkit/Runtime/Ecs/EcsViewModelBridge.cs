namespace Cuvara.UIToolkit.Ecs
{
    using System;
    using System.Collections.Generic;
    using Unity.Collections;
    using Unity.Entities;

    /// <summary>
    /// Reads an unmanaged component out of the ECS world, converts it to a ViewModel, and
    /// pushes it to whichever sinks are registered — only when the data has changed.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the adapter, and it stops at the ViewModel.</b> It must never touch
    /// a <c>VisualElement</c>, never hold a view reference, and never know a Presenter's
    /// concrete type. If <c>.Q&lt;Label&gt;(...)</c> ever appears in this assembly, the
    /// layering has been broken: the project's UI architecture contract routes ECS through
    /// <c>adapter -&gt; Presenter -&gt; View -&gt; UI Toolkit</c>, and this class is only the
    /// first arrow.</para>
    ///
    /// <para><b>Why <see cref="SystemBase"/> and not <c>ISystem</c>, and why no Burst.</b>
    /// It holds a managed <c>List</c> of sinks and calls managed code across an interface —
    /// neither is possible in an unmanaged system, and neither can be Burst-compiled. That
    /// is not a shortcut taken for convenience: the whole point of this class is to leave
    /// the unmanaged world exactly once, on the main thread, at the last possible moment.
    /// It runs in <see cref="PresentationSystemGroup"/> because presentation is what it is
    /// doing.</para>
    ///
    /// <para><b>Change-driven, not per-frame.</b> Two mechanisms, both cheap:</para>
    /// <list type="number">
    /// <item><see cref="ComponentSystemBase.Enabled"/> is false whenever no sink is
    /// registered, so a world with no screen open pays nothing at all — not even a query
    /// evaluation.</item>
    /// <item>The query carries <c>SetChangedVersionFilter</c>, so a chunk whose component
    /// has not been written since this system last ran is skipped entirely.</item>
    /// </list>
    /// <para>Pushing every frame is precisely what the architecture contract's performance
    /// section forbids ("update on data change, not per frame"), and it is how UI Toolkit
    /// earns a reputation for being slow when the fault is the caller's.</para>
    ///
    /// <para><b>The change filter is chunk-granular and conservative.</b> It reports a chunk
    /// as changed if anything in it wrote that component type — including a write of an
    /// identical value — and it always reports changed on the first run. It is a cheap
    /// filter, not an equality check. Override <see cref="HasChanged"/> when a sink is
    /// expensive enough that value-level deduplication earns its keep; the default returns
    /// true and lets the filter do the work.</para>
    ///
    /// <para><b>Entity-to-sink mapping is by value, never by reference.</b> An
    /// <see cref="IComponentData"/> is unmanaged and cannot hold a <c>VisualElement</c>, a
    /// Presenter, or anything else managed. If a host needs to route rows to different
    /// sinks, the component carries a value key — an entity index/version pair or a stable
    /// game id — and the managed side keeps the key-to-sink map. Reaching for a managed
    /// component to dodge that is the wrong answer.</para>
    ///
    /// <para><b>Placement attribute.</b> <c>[UpdateInGroup(typeof(PresentationSystemGroup))]</c>
    /// is declared here and is inherited by concrete subclasses. Repeating it on your own
    /// subclass is harmless and makes the placement visible where someone reading that class
    /// will look for it.</para>
    /// </remarks>
    /// <typeparam name="TComponent">The unmanaged component the simulation writes.</typeparam>
    /// <typeparam name="TViewModel">A plain data type. No <c>VisualElement</c>, no <c>UIDocument</c>.</typeparam>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public abstract partial class EcsViewModelBridge<TComponent, TViewModel> : SystemBase
        where TComponent : unmanaged, IComponentData
    {
        private readonly List<IViewModelSink<TViewModel>> sinks = new();

        private EntityQuery query;

        /// <summary>The sinks currently receiving pushes.</summary>
        public IReadOnlyList<IViewModelSink<TViewModel>> Sinks => this.sinks;

        /// <summary>How many pushes this bridge has made. For tests and telemetry.</summary>
        public int PushCount { get; private set; }

        /// <summary>Turns one component's data into a ViewModel. Pure; called on the main thread.</summary>
        /// <remarks>
        /// Keep it a conversion. Anything that reads another component, mutates the world, or
        /// calls a service belongs above this layer — a Presenter asks a Service; an adapter
        /// converts.
        /// </remarks>
        protected abstract TViewModel Convert(in TComponent component);

        /// <summary>
        /// Optional value-level guard on top of the chunk-granular change filter.
        /// </summary>
        /// <remarks>
        /// Defaults to true, which means "the query already decided this is worth pushing".
        /// Override it when a push is expensive and the component is written every frame with
        /// values that often do not actually differ — a transform-derived HUD position, say.
        /// </remarks>
        protected virtual bool HasChanged(in TViewModel previous, in TViewModel current) => true;

        private TViewModel lastPushed;
        private bool       hasPushedBefore;
        private bool       catchUpPending;

        /// <summary>Starts sending ViewModels to <paramref name="sink"/>.</summary>
        /// <remarks>
        /// Registering the first sink enables the system; removing the last one disables it.
        /// Adding the same sink twice is refused rather than silently double-pushing.
        /// </remarks>
        public void AddSink(IViewModelSink<TViewModel> sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            if (this.sinks.Contains(sink)) return;

            this.sinks.Add(sink);
            this.UpdateEnabledState();

            // A sink that arrives mid-session has missed every push so far, and neither of
            // the two quiet-keeping mechanisms will correct that on its own: the chunk has
            // not been written since this system last ran, so the change filter skips it,
            // and HasChanged would compare against a value this sink has never seen. A
            // screen opened while the simulation is idle would show nothing at all until
            // something happened to touch the component.
            //
            // So the next pass runs unfiltered and unconditionally, once. Already-registered
            // sinks get one repeat push of a value they already have, which is idempotent
            // for any sane Presenter and is much the lesser problem.
            this.catchUpPending  = true;
            this.hasPushedBefore = false;
        }

        /// <summary>Stops sending ViewModels to <paramref name="sink"/>.</summary>
        public void RemoveSink(IViewModelSink<TViewModel> sink)
        {
            if (sink == null) return;

            this.sinks.Remove(sink);
            this.UpdateEnabledState();
        }

        private void UpdateEnabledState()
        {
            // The cheapest possible idle cost: a disabled system's OnUpdate is not called at
            // all, so a world with no screen open does not even evaluate the query.
            this.Enabled = this.sinks.Count > 0;
        }

        protected override void OnCreate()
        {
            this.query = this.GetEntityQuery(ComponentType.ReadOnly<TComponent>());

            // Chunk-granular: chunks untouched since the last run of THIS system are skipped.
            this.query.SetChangedVersionFilter(ComponentType.ReadOnly<TComponent>());

            this.UpdateEnabledState();
        }

        protected override void OnUpdate()
        {
            // Enabled already covers the no-sink case; this is the belt-and-braces for a
            // subclass that drives Update() by hand, as the tests do.
            if (this.sinks.Count == 0) return;

            if (!this.catchUpPending)
            {
                this.RunPass(false);
                return;
            }

            this.catchUpPending = false;

            // Drop the change filter for exactly one pass so a newly-registered sink sees
            // the current state, then put it straight back. Resetting rather than rebuilding
            // the query keeps this off the allocation path — it runs whenever a screen opens.
            this.query.ResetFilter();

            try
            {
                this.RunPass(true);
            }
            finally
            {
                this.query.SetChangedVersionFilter(ComponentType.ReadOnly<TComponent>());
            }
        }

        /// <param name="force">Push regardless of <see cref="HasChanged"/> — the catch-up pass.</param>
        private void RunPass(bool force)
        {
            using var components = this.query.ToComponentDataArray<TComponent>(Allocator.Temp);

            if (components.Length == 0) return;

            for (var i = 0; i < components.Length; ++i)
            {
                var viewModel = this.Convert(components[i]);

                if (!force && this.hasPushedBefore && !this.HasChanged(this.lastPushed, viewModel)) continue;

                this.lastPushed      = viewModel;
                this.hasPushedBefore = true;

                this.PushToSinks(viewModel);
            }
        }

        private void PushToSinks(in TViewModel viewModel)
        {
            ++this.PushCount;

            // Indexed rather than foreach: a sink is host code, and host code closing a
            // screen from inside Push() would mutate this list mid-enumeration. Walking
            // backwards means a removal during the walk cannot skip a sink.
            for (var i = this.sinks.Count - 1; i >= 0; --i)
            {
                if (i >= this.sinks.Count) continue;
                this.sinks[i].Push(viewModel);
            }
        }
    }
}
