#if CUVARA_DOTS_VCONTAINER && CUVARA_DOTS_MESSAGEPIPE
using System;
using Cuvara.DOTS.Messaging;
using MessagePipe;

namespace Cuvara.DOTS.DI
{
    /// <summary>
    /// Adapts MessagePipe's <see cref="ISubscriber{TMessage}"/> to the package's
    /// <see cref="IDotsSubscriber{TMessage}"/>.
    /// </summary>
    /// <remarks>
    /// MessagePipe's own <c>Subscribe</c> returns an <see cref="IDisposable"/> already, so the
    /// subscription lifetime rules are MessagePipe's and this adds nothing to them.
    /// </remarks>
    internal sealed class MessagePipeDotsSubscriber<TMessage> : IDotsSubscriber<TMessage>
    {
        private readonly ISubscriber<TMessage> _subscriber;

        public MessagePipeDotsSubscriber(ISubscriber<TMessage> subscriber) => _subscriber = subscriber;

        public IDisposable Subscribe(Action<TMessage> handler) => _subscriber.Subscribe(handler);
    }
}
#endif
