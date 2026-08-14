namespace Cuvara.DOTS.Messaging
{
    /// <summary>
    /// Publishes a package event. One method, contravariant, and no transport of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this interface exists when MessagePipe is the chosen messaging library.</b>
    /// <c>Cuvara.DOTS.Runtime</c> must install with only its four pinned Unity dependencies, so it
    /// cannot name a MessagePipe type. It therefore declares the shape it needs, and the binding to
    /// MessagePipe's <c>IPublisher&lt;T&gt;</c> lives in <c>Cuvara.DOTS.DI</c> behind a
    /// <c>versionDefines</c> gate. This is not a second messaging system: there is no bus, no
    /// registry, no dispatch and no subscriber list behind it — it is one method that a MessagePipe
    /// publisher already satisfies.
    /// </para>
    /// <para>
    /// With MessagePipe absent, <see cref="NullDotsPublisher{TMessage}"/> is used and publishing is a
    /// no-op. That is the documented behaviour, not a degraded fallback — writing a signal bus to
    /// fill the gap is exactly what this design refuses.
    /// </para>
    /// </remarks>
    public interface IDotsPublisher<in TMessage>
    {
        void Publish(TMessage message);
    }
}
