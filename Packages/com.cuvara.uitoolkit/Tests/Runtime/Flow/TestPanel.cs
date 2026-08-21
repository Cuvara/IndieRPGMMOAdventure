namespace Cuvara.UIToolkit.Flow.Tests
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// A live UI Toolkit panel with nothing else attached, for the few assertions that genuinely
    /// need event dispatch.
    /// </summary>
    /// <remarks>
    /// <para>Most of this package's flow tests use detached elements on purpose, because a test
    /// that needs a panel is a test a consumer cannot easily copy. But a handful of behaviours
    /// only exist inside dispatch — a <c>Button</c>'s <c>clicked</c> comes from its
    /// <c>Clickable</c> manipulator, and <c>SendEvent</c> on a detached element is silently a
    /// no-op — and asserting those against a reflection peek at a delegate list would be
    /// asserting the implementation rather than the behaviour.</para>
    ///
    /// <para><see cref="Submit"/> uses <see cref="NavigationSubmitEvent"/> rather than a
    /// synthesised pointer sequence. It is the same path a gamepad or keyboard activation takes,
    /// it is one event instead of three, and it needs no coordinates — a pointer sequence has to
    /// land inside the element's <c>layout</c>, which means waiting for a layout pass and makes
    /// the test about geometry rather than about the click.</para>
    /// </remarks>
    internal sealed class TestPanel : IDisposable
    {
        private readonly GameObject    gameObject;
        private readonly PanelSettings panelSettings;

        public VisualElement Root { get; }

        public TestPanel()
        {
            this.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();

            this.gameObject = new(nameof(TestPanel));
            this.gameObject.SetActive(false);

            var document = this.gameObject.AddComponent<UIDocument>();
            document.panelSettings = this.panelSettings;

            this.gameObject.SetActive(true);

            this.Root = document.rootVisualElement;

            if (this.Root == null) throw new InvalidOperationException($"{nameof(TestPanel)} has no rootVisualElement.");
        }

        /// <summary>Activates <paramref name="target"/> the way a gamepad or keyboard submit does.</summary>
        public void Submit(VisualElement target)
        {
            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = target;
            target.SendEvent(evt);
        }

        public void Dispose()
        {
            if (this.gameObject != null) UnityEngine.Object.DestroyImmediate(this.gameObject);
            if (this.panelSettings != null) UnityEngine.Object.DestroyImmediate(this.panelSettings);
        }
    }
}
