namespace Scripts.Session
{
    using System;
    using System.Threading;

    /// <summary>
    /// Connects <see cref="SessionRecovery"/> to an <see cref="ISessionStatusView"/>: status
    /// changes go to the view, the view's Retry goes back as a fresh session. No VisualElement
    /// in here, so it is tested without a panel.
    /// </summary>
    public sealed class SessionStatusPresenter : IDisposable
    {
        private readonly SessionRecovery recovery;
        private readonly ISessionStatusView view;
        private readonly CancellationToken lifetime;

        public SessionStatusPresenter(SessionRecovery recovery, ISessionStatusView view, CancellationToken lifetime)
        {
            this.recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.lifetime = lifetime;

            this.recovery.StatusChanged += this.OnStatusChanged;
            this.view.RetryClicked += this.OnRetryClicked;
            this.view.Show(this.recovery.Status);
        }

        public void Dispose()
        {
            this.recovery.StatusChanged -= this.OnStatusChanged;
            this.view.RetryClicked -= this.OnRetryClicked;
        }

        private void OnStatusChanged(SessionStatus status) => this.view.Show(status);

        private async void OnRetryClicked()
        {
            // async void: this is an event handler. RetryAsync reports its own failures through
            // the status, so nothing escapes.
            await this.recovery.RetryAsync(this.lifetime);
        }
    }
}
