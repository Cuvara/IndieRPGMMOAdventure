namespace Cuvara.UIToolkit.TestSupport
{
    using System;
    using Cuvara.UIToolkit.Flow;

    /// <summary>
    /// Assertion helpers for <see cref="ScreenSubscriptions"/> in tests.
    /// </summary>
    /// <remarks>
    /// <para><see cref="ScreenSubscriptions"/> is sealed and already exposes
    /// <see cref="ScreenSubscriptions.LiveCount"/> and
    /// <see cref="ScreenSubscriptions.IsDisposed"/>. These extensions turn those
    /// properties into a one-liner assertion so every screen test ends the same way:</para>
    /// <code>
    /// subs.Dispose();
    /// subs.AssertAllReleased();
    /// </code>
    /// </remarks>
    public static class ScreenSubscriptionsAssertions
    {
        /// <summary>
        /// Throws if any registrations survived <see cref="ScreenSubscriptions.Dispose"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Subscriptions were not disposed, or some survived disposal.
        /// </exception>
        public static void AssertAllReleased(this ScreenSubscriptions subscriptions)
        {
            if (subscriptions == null) throw new ArgumentNullException(nameof(subscriptions));

            if (!subscriptions.IsDisposed)
                throw new InvalidOperationException(
                    "ScreenSubscriptions has not been disposed yet. Call Dispose() before asserting.");

            if (subscriptions.LiveCount != 0)
                throw new InvalidOperationException(
                    $"ScreenSubscriptions has {subscriptions.LiveCount} live registration(s) after disposal — a leak.");
        }
    }
}
