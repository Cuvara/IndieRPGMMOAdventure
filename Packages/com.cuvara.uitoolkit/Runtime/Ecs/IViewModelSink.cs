namespace Cuvara.UIToolkit.Ecs
{
    /// <summary>
    /// Receives a ViewModel produced from ECS data. The host's Presenter implements this.
    /// </summary>
    /// <remarks>
    /// <para><b>This interface is the package's entire coupling to MVP.</b> It knows a sink,
    /// not a Presenter, not a View, and certainly not a <c>VisualElement</c>. The host
    /// decides what a sink is: a Presenter, a reactive property, a queue, a test spy.</para>
    ///
    /// <para><b>Why the ECS side stops here.</b> The project's UI architecture contract is
    /// explicit that ECS must never manipulate UI Toolkit — not <c>VisualElement</c>, not
    /// <c>Button</c>, not <c>Label</c> — and that the path runs
    /// <c>ECS -&gt; adapter -&gt; Presenter -&gt; View -&gt; UI Toolkit</c>. Everything in
    /// this assembly is the ADAPTER. It ends at <see cref="Push"/>; the two layers below it
    /// belong to the host.</para>
    ///
    /// <para>There is also a hard technical reason pointing the same way, and it is worth
    /// separating from the architectural one because it constrains something different.
    /// <c>VisualElement</c> is plain managed C#, not a <c>UnityEngine.Object</c>, so it
    /// cannot be touched from <c>ISystem</c>, <c>IJobEntity</c>, Burst, or any worker thread
    /// — there is no attribute, no unsafe cast and no <c>NativeContainer</c> that makes it
    /// work. That fact constrains WHERE the adapter runs (main thread, always). The contract
    /// constrains WHAT it may talk to (a ViewModel, never a view). Both apply, and satisfying
    /// one does not satisfy the other.</para>
    ///
    /// <para><b>A ViewModel must be a plain value.</b> No <c>VisualElement</c>, no
    /// <c>VisualTreeAsset</c>, no <c>UIDocument</c>, no <c>GameObject</c>. Prefer a readonly
    /// struct: it makes the "plain data" property enforceable at a glance, and
    /// <see cref="Push"/> takes it by <c>in</c> so a large one costs no copy.</para>
    /// </remarks>
    /// <typeparam name="TViewModel">A plain data type. See the remarks on what it may not contain.</typeparam>
    public interface IViewModelSink<TViewModel>
    {
        /// <summary>
        /// Called on the main thread when the source data has changed, never per frame.
        /// </summary>
        void Push(in TViewModel viewModel);
    }
}
