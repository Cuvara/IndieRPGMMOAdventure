namespace Cuvara.UIToolkit.Flow
{
    using System;
    using System.Collections.Generic;
    using UnityEngine.UIElements;

    /// <summary>
    /// Everything a screen hooked up while binding, released in one call when the screen ends.
    /// </summary>
    /// <remarks>
    /// <para><b>This type exists to delete a category of code, not to add one.</b> In the
    /// framework this package replaces, every generated screen carried an unregister-then-register
    /// pair and a <c>Dispose</c> override that had to remember each callback by hand — and it
    /// carried them because <c>Dispose()</c> there meant "closing" as well as "ending", so bind
    /// ran again on an object whose handlers were still attached. Here, bind runs once per
    /// scope and the scope disposes this. There is nothing for a screen author to remember and
    /// no override to forget.</para>
    ///
    /// <para><b>It is handed to <c>OnBindAsync</c> as a required parameter</b> rather than exposed
    /// as a property, which is the reason it cannot be skipped: there is no overload without it,
    /// so the thing you need in order to clean up is already in scope at the moment you create
    /// something needing cleanup.</para>
    ///
    /// <para><b><see cref="LiveCount"/> is what makes a leak a test failure</b> instead of a code
    /// review opinion. A screen's test disposes this and asserts the count is zero; a handler
    /// registered outside it survives and the assertion catches it. That is the whole reason the
    /// count is public.</para>
    ///
    /// <para>Not thread-safe, deliberately. Everything in this package's flow runs on the main
    /// thread — a <c>VisualElement</c> cannot be touched from anywhere else — so a lock here
    /// would buy nothing and imply a concurrency story that does not exist.</para>
    /// </remarks>
    public sealed class ScreenSubscriptions : IDisposable
    {
        private readonly List<IDisposable> disposables = new();

        /// <summary>How many registrations are still live. Zero after <see cref="Dispose"/>.</summary>
        public int LiveCount => this.disposables.Count;

        /// <summary>Whether <see cref="Dispose"/> has run.</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>Takes ownership of <paramref name="disposable"/>, releasing it on <see cref="Dispose"/>.</summary>
        /// <remarks>
        /// Adding to an already-disposed instance disposes the argument immediately rather than
        /// throwing or silently keeping it. The case arises when an async bind is cancelled and a
        /// continuation lands after teardown; throwing there turns a benign race into an
        /// exception in a <c>finally</c>, and keeping it is the leak this class exists to
        /// prevent. Disposing it now is the only option that ends with nothing held.
        /// </remarks>
        public void Add(IDisposable disposable)
        {
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));

            if (this.IsDisposed)
            {
                disposable.Dispose();
                return;
            }

            this.disposables.Add(disposable);
        }

        /// <summary>Registers <paramref name="action"/> to run on <see cref="Dispose"/>.</summary>
        /// <remarks>
        /// For the things that are not <see cref="IDisposable"/> — an event unsubscription, a
        /// flag to reset. Without this, an author needing one reaches for a <c>Dispose</c>
        /// override, and the override is the thing being designed out.
        /// </remarks>
        public void AddAction(Action onDispose)
        {
            if (onDispose == null) throw new ArgumentNullException(nameof(onDispose));

            this.Add(new ActionDisposable(onDispose));
        }

        /// <summary>Registers a click handler on <paramref name="button"/> and unhooks it on <see cref="Dispose"/>.</summary>
        /// <remarks>
        /// The single most common thing a screen does, and the single most common thing it forgets
        /// to undo. <c>clicked</c> is a plain multicast delegate, so a handler added on each bind
        /// without a matching removal fires once per bind — the bug the unregister-then-register
        /// pattern existed to paper over.
        /// </remarks>
        public void Clicked(Button button, Action handler)
        {
            if (button == null) throw new ArgumentNullException(nameof(button));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            button.clicked += handler;
            this.AddAction(() => button.clicked -= handler);
        }

        /// <summary>Registers a UI Toolkit event callback and unregisters it on <see cref="Dispose"/>.</summary>
        public void On<TEvent>(VisualElement element, EventCallback<TEvent> callback, TrickleDown trickleDown = TrickleDown.NoTrickleDown)
            where TEvent : EventBase<TEvent>, new()
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            element.RegisterCallback(callback, trickleDown);
            this.AddAction(() => element.UnregisterCallback(callback, trickleDown));
        }

        /// <summary>
        /// Calls <paramref name="callback"/> on the first <see cref="GeometryChangedEvent"/>
        /// from <paramref name="element"/>, then unregisters itself.
        /// </summary>
        /// <remarks>
        /// <para>Under a <c>display:none</c> ancestor, descendants have no resolved layout —
        /// <c>resolvedStyle.width</c>, <c>element.layout</c>, <c>worldBound</c> are zero or
        /// stale until one layout pass after the ancestor shows. A screen that measures during
        /// <c>OnBindAsync</c> measures nothing; this is the convenience that defers the
        /// measurement to the first real layout pass.</para>
        ///
        /// <para>If the subscriptions are disposed before the event fires, the callback never
        /// runs and the handler is unregistered — no stale closure, no surprise call after
        /// teardown.</para>
        /// </remarks>
        public void OnFirstGeometry(VisualElement element, Action<GeometryChangedEvent> callback)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            var fired = false;

            void Handler(GeometryChangedEvent evt)
            {
                if (fired) return;
                fired = true;
                element.UnregisterCallback<GeometryChangedEvent>(Handler);
                callback(evt);
            }

            element.RegisterCallback<GeometryChangedEvent>(Handler);
            this.AddAction(() =>
            {
                if (!fired) element.UnregisterCallback<GeometryChangedEvent>(Handler);
            });
        }

        /// <summary>Releases everything registered, in reverse order of registration.</summary>
        /// <remarks>
        /// <para>Reverse order because registrations often nest — an adapter built over a control
        /// that a later registration also touches — and unwinding in the order things were built
        /// tears down the outer thing while an inner one still references it.</para>
        ///
        /// <para>One failing <see cref="IDisposable"/> does not stop the rest. A teardown that
        /// abandons half its work on the first exception leaves a screen partly alive with
        /// nothing holding a reference to finish the job, which is strictly worse than the
        /// original fault. Exceptions are aggregated and rethrown after everything has been
        /// released.</para>
        ///
        /// <para>Idempotent: disposing twice is a no-op, because a scope disposing this and a
        /// test disposing it are both legitimate and neither knows about the other.</para>
        /// </remarks>
        public void Dispose()
        {
            if (this.IsDisposed) return;
            this.IsDisposed = true;

            List<Exception> failures = null;

            for (var i = this.disposables.Count - 1; i >= 0; --i)
            {
                try
                {
                    this.disposables[i].Dispose();
                }
                catch (Exception exception)
                {
                    (failures ??= new()).Add(exception);
                }
            }

            this.disposables.Clear();

            if (failures != null) throw new AggregateException("One or more screen subscriptions threw while being released.", failures);
        }

        private sealed class ActionDisposable : IDisposable
        {
            private Action action;

            public ActionDisposable(Action action) { this.action = action; }

            public void Dispose()
            {
                var toRun = this.action;
                this.action = null;
                toRun?.Invoke();
            }
        }
    }
}
