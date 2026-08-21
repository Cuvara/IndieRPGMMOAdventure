namespace Cuvara.UIToolkit.Tests.Collections
{
    using System;
    using Cuvara.UIToolkit.Core;

    /// <summary>
    /// An <see cref="IPresenterInstantiator"/> that just news up whatever it is asked for,
    /// and counts how many times it did.
    /// </summary>
    /// <remarks>
    /// The collection adapters take an instantiator in their constructor precisely so a
    /// test does not have to stand up a real container to exercise them — the host
    /// framework's adapters resolve a container in <c>Awake</c> and are therefore
    /// untestable without a live scene. <see cref="InstantiateCount"/> is what several
    /// tests use to prove that scrolling recycles presenters instead of building new ones
    /// per row.
    /// </remarks>
    public sealed class CountingPresenterInstantiator : IPresenterInstantiator
    {
        public int InstantiateCount { get; private set; }

        public object Instantiate(Type type)
        {
            ++this.InstantiateCount;
            return Activator.CreateInstance(type);
        }
    }
}
