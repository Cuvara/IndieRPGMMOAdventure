namespace Cuvara.UIToolkit.Flow.Tests
{
    using System;
    using System.Collections.Generic;
    using Cuvara.UIToolkit.Flow;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>
    /// The type that deletes teardown code, and the assertion that makes a leak a test failure.
    /// </summary>
    /// <remarks>
    /// <para>Almost all of this runs with no panel, no scene and no <c>UIDocument</c>, and that
    /// is the property being protected rather than a convenience: if this class ever needs a live
    /// panel to be exercised, every consumer's screen test needs one too.</para>
    ///
    /// <para>The three click tests are the exception, and they earn it. A <c>Button</c>'s
    /// <c>clicked</c> comes from its <c>Clickable</c> manipulator, so it only fires inside real
    /// event dispatch — asserting the hook any other way means peeking at a delegate list, which
    /// tests the implementation rather than the behaviour.</para>
    /// </remarks>
    public class ScreenSubscriptionsTests
    {
        private sealed class Spy : IDisposable
        {
            public int DisposeCount;

            public void Dispose() { ++this.DisposeCount; }
        }

        private sealed class Thrower : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("boom");
        }

        #region Releasing

        [Test]
        public void DisposeReleasesEverythingAdded()
        {
            var subs  = new ScreenSubscriptions();
            var first = new Spy();
            var second = new Spy();

            subs.Add(first);
            subs.Add(second);

            subs.Dispose();

            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void LiveCountIsZeroAfterDispose()
        {
            // This is the assertion every screen's generated test carries, and the reason
            // LiveCount is public at all: it turns "did OnBindAsync leak a subscription" from a
            // code-review opinion into a failing test.
            var subs = new ScreenSubscriptions();
            subs.Add(new Spy());
            subs.Add(new Spy());

            Assert.That(subs.LiveCount, Is.EqualTo(2), "precondition");

            subs.Dispose();

            Assert.That(subs.LiveCount, Is.Zero);
            Assert.That(subs.IsDisposed, Is.True);
        }

        [Test]
        public void ItReleasesInReverseOrderOfRegistration()
        {
            // Registrations nest — an adapter built over a control a later registration also
            // touches. Unwinding forwards tears down the outer thing while an inner one still
            // references it.
            var order = new List<string>();
            var subs  = new ScreenSubscriptions();

            subs.AddAction(() => order.Add("first"));
            subs.AddAction(() => order.Add("second"));
            subs.AddAction(() => order.Add("third"));

            subs.Dispose();

            Assert.That(order, Is.EqualTo(new[] { "third", "second", "first" }));
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            // A scope disposing this and a test disposing it are both legitimate, and neither
            // knows about the other.
            var subs = new ScreenSubscriptions();
            var spy  = new Spy();
            subs.Add(spy);

            subs.Dispose();
            subs.Dispose();

            Assert.That(spy.DisposeCount, Is.EqualTo(1));
        }

        #endregion

        #region Failure during teardown

        [Test]
        public void OneFailingDisposableDoesNotStopTheRest()
        {
            // Abandoning teardown on the first exception leaves a screen half-alive with nothing
            // holding a reference to finish the job — strictly worse than the original fault.
            var subs    = new ScreenSubscriptions();
            var before  = new Spy();
            var after   = new Spy();

            subs.Add(before);
            subs.Add(new Thrower());
            subs.Add(after);

            Assert.Throws<AggregateException>(() => subs.Dispose());

            Assert.That(before.DisposeCount, Is.EqualTo(1), "a registration before the failure must still be released");
            Assert.That(after.DisposeCount, Is.EqualTo(1), "a registration after the failure must still be released");
        }

        [Test]
        public void AfterAFailingTeardownNothingIsStillHeld()
        {
            var subs = new ScreenSubscriptions();
            subs.Add(new Thrower());

            Assert.Throws<AggregateException>(() => subs.Dispose());

            Assert.That(subs.LiveCount, Is.Zero);
            Assert.That(subs.IsDisposed, Is.True);
        }

        #endregion

        #region Registering after teardown

        [Test]
        public void AddingAfterDisposeDisposesTheArgumentImmediately()
        {
            // The case: an async bind is cancelled and a continuation lands after teardown.
            // Throwing turns a benign race into an exception inside a finally; keeping it is the
            // leak this class exists to prevent. Disposing now is the only option that ends with
            // nothing held.
            var subs = new ScreenSubscriptions();
            subs.Dispose();

            var late = new Spy();
            subs.Add(late);

            Assert.That(late.DisposeCount, Is.EqualTo(1));
            Assert.That(subs.LiveCount, Is.Zero);
        }

        [Test]
        public void AddingAfterDisposeDoesNotThrow()
        {
            var subs = new ScreenSubscriptions();
            subs.Dispose();

            Assert.DoesNotThrow(() => subs.AddAction(() => { }));
        }

        #endregion

        #region Button clicks

        // A Button's `clicked` fires from its Clickable manipulator, which needs a panel and a
        // real event. These three therefore build one — everything else in this file works on
        // detached elements, and that distinction is deliberate rather than accidental: what is
        // under test here is that the hook is really made and really undone, which a reflection
        // peek at the delegate list would only approximate.

        [Test]
        public void ClickedRegistersOneReleasableSubscription()
        {
            var subs = new ScreenSubscriptions();

            subs.Clicked(new Button(), () => { });

            Assert.That(subs.LiveCount, Is.EqualTo(1));

            subs.Dispose();

            Assert.That(subs.LiveCount, Is.Zero);
        }

        [Test]
        public void ClickedHandlerFiresOnARealClick()
        {
            using var panel = new TestPanel();

            var subs   = new ScreenSubscriptions();
            var button = new Button();
            var clicks = 0;

            panel.Root.Add(button);
            subs.Clicked(button, () => ++clicks);

            panel.Submit(button);

            Assert.That(clicks, Is.EqualTo(1));
        }

        [Test]
        public void ClickedHandlerDoesNotFireAfterDispose()
        {
            // The single most common thing a screen forgets to undo. `clicked` is a plain
            // multicast delegate, so a handler that outlives its bind fires once per bind — the
            // bug the old unregister-then-register pattern existed to paper over.
            using var panel = new TestPanel();

            var subs   = new ScreenSubscriptions();
            var button = new Button();
            var clicks = 0;

            panel.Root.Add(button);
            subs.Clicked(button, () => ++clicks);

            panel.Submit(button);
            Assert.That(clicks, Is.EqualTo(1), "precondition: the hook works");

            subs.Dispose();
            panel.Submit(button);

            Assert.That(clicks, Is.EqualTo(1), "the handler must not survive teardown");
        }

        #endregion

        #region Callbacks

        [Test]
        public void OnRegistersAndUnregistersACallback()
        {
            var subs    = new ScreenSubscriptions();
            var element = new VisualElement();
            var seen    = 0;

            subs.On<GeometryChangedEvent>(element, _ => ++seen);

            Assert.That(subs.LiveCount, Is.EqualTo(1));

            subs.Dispose();

            Assert.That(subs.LiveCount, Is.Zero);
        }

        #endregion

        #region OnFirstGeometry

        [Test]
        public void OnFirstGeometry_RegistersOneSubscription()
        {
            var subs    = new ScreenSubscriptions();
            var element = new VisualElement();

            subs.OnFirstGeometry(element, _ => { });

            Assert.That(subs.LiveCount, Is.EqualTo(1));

            subs.Dispose();
            Assert.That(subs.LiveCount, Is.Zero);
        }

        [Test]
        public void OnFirstGeometry_NullElementThrows()
        {
            var subs = new ScreenSubscriptions();

            Assert.Throws<ArgumentNullException>(() => subs.OnFirstGeometry(null, _ => { }));
        }

        [Test]
        public void OnFirstGeometry_NullCallbackThrows()
        {
            var subs = new ScreenSubscriptions();

            Assert.Throws<ArgumentNullException>(() => subs.OnFirstGeometry(new VisualElement(), null));
        }

        #endregion

        #region Null arguments

        [Test]
        public void NullArgumentsThrow()
        {
            var subs = new ScreenSubscriptions();

            Assert.Throws<ArgumentNullException>(() => subs.Add(null));
            Assert.Throws<ArgumentNullException>(() => subs.AddAction(null));
            Assert.Throws<ArgumentNullException>(() => subs.Clicked(null, () => { }));
            Assert.Throws<ArgumentNullException>(() => subs.Clicked(new Button(), null));
            Assert.Throws<ArgumentNullException>(() => subs.On<GeometryChangedEvent>(null, _ => { }));
            Assert.Throws<ArgumentNullException>(() => subs.On<GeometryChangedEvent>(new VisualElement(), null));
        }

        #endregion
    }
}
