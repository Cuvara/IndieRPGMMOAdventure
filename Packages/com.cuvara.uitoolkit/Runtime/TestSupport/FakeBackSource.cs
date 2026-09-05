namespace Cuvara.UIToolkit.TestSupport
{
    using System;

    /// <summary>
    /// A test double for back navigation: fires a Back press and reports whether it was
    /// consumed.
    /// </summary>
    /// <remarks>
    /// <para>Wire <see cref="BackHandler"/> to <c>IScreenNavigator.HandleBack</c> (or any
    /// <c>Func&lt;bool&gt;</c>), then call <see cref="PressBack"/>. The return value is
    /// the handler's answer — true means consumed, false means the platform's own Back
    /// should run.</para>
    ///
    /// <para>This replaces what the real <c>BackNavigationSource</c> does through
    /// <c>NavigationCancelEvent</c>, without needing a panel or an input system.</para>
    /// </remarks>
    public sealed class FakeBackSource
    {
        /// <summary>The handler that decides whether Back was consumed.</summary>
        public Func<bool> BackHandler { get; set; }

        /// <summary>How many times <see cref="PressBack"/> was called.</summary>
        public int PressCount { get; private set; }

        /// <summary>How many presses were consumed (handler returned true).</summary>
        public int HandledCount { get; private set; }

        /// <summary>How many presses were NOT consumed (handler returned false or was null).</summary>
        public int UnhandledCount => this.PressCount - this.HandledCount;

        /// <summary>Simulates a Back press. Returns whether it was consumed.</summary>
        public bool PressBack()
        {
            ++this.PressCount;

            if (this.BackHandler == null) return false;

            var handled = this.BackHandler.Invoke();

            if (handled) ++this.HandledCount;

            return handled;
        }
    }
}
