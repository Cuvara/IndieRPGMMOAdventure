namespace Scripts.DI
{
    using System;
    using Scripts.Session;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The session-status overlay: a full-screen dim layer with the cause and a Retry button,
    /// shown while the session is reconnecting or has ended (audit CN-02).
    /// </summary>
    /// <remarks>
    /// Built in code rather than from UXML: it is one label and one button with no designer
    /// iteration ahead of it, and a code-built tree needs no generated <c>.uxml.g.cs</c>. It
    /// borrows the gameplay panel's <see cref="PanelSettings"/> and sits above it by sort
    /// order. There is no login screen in this client yet, so there is no Back-to-login button;
    /// add one beside Retry when a screen exists to go back to.
    /// </remarks>
    public sealed class SessionStatusOverlay : ISessionStatusView, IDisposable
    {
        /// <summary>Above the gameplay panel (sort order 0) and the HUD.</summary>
        private const float SortingOrder = 1000f;

        private readonly GameObject host;
        private readonly VisualElement root;
        private readonly Label caption;
        private readonly Button retry;

        private SessionStatusOverlay(GameObject host, VisualElement parent)
        {
            this.host = host;

            this.root = new VisualElement { name = "session-status" };
            this.root.style.position = Position.Absolute;
            this.root.style.left = 0;
            this.root.style.right = 0;
            this.root.style.top = 0;
            this.root.style.bottom = 0;
            this.root.style.alignItems = Align.Center;
            this.root.style.justifyContent = Justify.Center;
            this.root.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            this.root.style.display = DisplayStyle.None;

            this.caption = new Label { name = "session-status-caption" };
            this.caption.style.color = Color.white;
            this.caption.style.fontSize = 24;
            this.caption.style.whiteSpace = WhiteSpace.Normal;
            this.caption.style.maxWidth = 640;
            this.caption.style.unityTextAlign = TextAnchor.MiddleCenter;
            this.root.Add(this.caption);

            this.retry = new Button(() => this.RetryClicked?.Invoke()) { name = "session-status-retry", text = "Retry" };
            this.retry.style.marginTop = 16;
            this.retry.style.paddingLeft = 32;
            this.retry.style.paddingRight = 32;
            this.root.Add(this.retry);

            parent.Add(this.root);
        }

        public event Action RetryClicked;

        /// <summary>
        /// Creates the overlay, or returns null when the panel settings are not in Resources
        /// (a headless or test run): the session then runs without the player-facing UI.
        /// </summary>
        public static SessionStatusOverlay TryCreate()
        {
            var settings = Resources.Load<PanelSettings>(GameplayPanelDriver.PanelSettingsResource);
            if (settings == null) return null;

            // Inactive while wiring, so the document attaches to its panel exactly once.
            var host = new GameObject("SessionStatus");
            host.SetActive(false);
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.sortingOrder = SortingOrder;
            host.SetActive(true);

            return new SessionStatusOverlay(host, document.rootVisualElement);
        }

        public void Show(SessionStatus status)
        {
            if (status == null || !status.IsVisible)
            {
                this.root.style.display = DisplayStyle.None;
                return;
            }

            this.caption.text = status.Caption;
            this.retry.style.display = status.CanRetry ? DisplayStyle.Flex : DisplayStyle.None;
            this.root.style.display = DisplayStyle.Flex;
        }

        public void Dispose()
        {
            if (this.host != null) UnityEngine.Object.Destroy(this.host);
        }
    }
}
