namespace Cuvara.DOTS.Messaging
{
    /// <summary>
    /// The publisher used when no messaging library is installed. Drops everything.
    /// </summary>
    /// <remarks>
    /// Package code holds a non-null publisher at all times and never branches on whether messaging
    /// exists — the branch happens once, at registration. A null check at every publish site would
    /// be the same decision made repeatedly in the least visible place.
    /// </remarks>
    public sealed class NullDotsPublisher<TMessage> : IDotsPublisher<TMessage>
    {
        public static readonly NullDotsPublisher<TMessage> Instance = new NullDotsPublisher<TMessage>();

        public void Publish(TMessage message)
        {
        }
    }
}
