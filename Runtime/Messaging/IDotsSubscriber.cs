using System;

namespace Cuvara.DOTS.Messaging
{
    /// <summary>
    /// Subscribes to a package event, returning the handle that ends the subscription.
    /// </summary>
    /// <remarks>
    /// Shaped so MessagePipe's <c>ISubscriber&lt;T&gt;.Subscribe(Action&lt;T&gt;)</c> satisfies it
    /// directly. Nothing inside the package subscribes — this exists for consumers, so they are not
    /// forced to take a MessagePipe dependency to hear about a view they own.
    /// </remarks>
    public interface IDotsSubscriber<out TMessage>
    {
        IDisposable Subscribe(Action<TMessage> handler);
    }
}
