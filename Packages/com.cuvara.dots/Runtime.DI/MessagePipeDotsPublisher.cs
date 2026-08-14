#if CUVARA_DOTS_VCONTAINER && CUVARA_DOTS_MESSAGEPIPE
using Cuvara.DOTS.Messaging;
using MessagePipe;

namespace Cuvara.DOTS.DI
{
    /// <summary>
    /// Adapts MessagePipe's <see cref="IPublisher{TMessage}"/> to the package's
    /// <see cref="IDotsPublisher{TMessage}"/>.
    /// </summary>
    /// <remarks>
    /// One forwarding call. The type exists only so that the core assembly never names a MessagePipe
    /// type — it is the entire cost of keeping <c>Cuvara.DOTS.Runtime</c> installable standalone.
    /// </remarks>
    internal sealed class MessagePipeDotsPublisher<TMessage> : IDotsPublisher<TMessage>
    {
        private readonly IPublisher<TMessage> _publisher;

        public MessagePipeDotsPublisher(IPublisher<TMessage> publisher) => _publisher = publisher;

        public void Publish(TMessage message) => _publisher.Publish(message);
    }
}
#endif
