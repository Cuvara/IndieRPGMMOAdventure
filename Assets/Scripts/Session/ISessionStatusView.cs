namespace Scripts.Session
{
    using System;

    /// <summary>The overlay that tells the player their session is down and offers Retry.</summary>
    public interface ISessionStatusView
    {
        /// <summary>Shows <paramref name="status"/>, or hides the overlay when it is not visible.</summary>
        void Show(SessionStatus status);

        /// <summary>Raised when the player presses Retry.</summary>
        event Action RetryClicked;
    }
}
