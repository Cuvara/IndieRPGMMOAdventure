namespace Cuvara.UIToolkit.TestSupport
{
    using System.Collections.Generic;

    /// <summary>
    /// Records every <c>Push</c> call for assertion. Works with or without ECS — it
    /// implements <c>IViewModelSink&lt;T&gt;</c> from the Ecs assembly when that assembly
    /// is present, but is defined here in terms of the same shape so it compiles either way.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Values"/> is the ordered list of everything pushed, and
    /// <see cref="PushCount"/> is a shorthand for its length. Assert against both:
    /// the count proves frequency, the values prove content.</para>
    /// </remarks>
    public sealed class SpyViewModelSink<TViewModel>
    {
        private readonly List<TViewModel> values = new();

        /// <summary>Every value pushed, in order.</summary>
        public IReadOnlyList<TViewModel> Values => this.values;

        /// <summary>How many times <see cref="Push"/> was called.</summary>
        public int PushCount => this.values.Count;

        /// <summary>The most recent value, or default if nothing was pushed.</summary>
        public TViewModel Last => this.values.Count > 0 ? this.values[^1] : default;

        /// <summary>Records <paramref name="viewModel"/>.</summary>
        public void Push(in TViewModel viewModel) { this.values.Add(viewModel); }

        /// <summary>Clears the recorded history.</summary>
        public void Clear() { this.values.Clear(); }
    }
}
