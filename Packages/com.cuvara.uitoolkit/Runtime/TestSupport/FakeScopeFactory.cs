namespace Cuvara.UIToolkit.TestSupport
{
    using System;
    using System.Collections.Generic;
    using Cuvara.UIToolkit.Flow;

    /// <summary>
    /// A dictionary-backed <see cref="IScreenScopeFactory"/> that counts how many scopes
    /// were created and disposed — so the navigator's disposal guarantees are ASSERTED
    /// rather than argued.
    /// </summary>
    /// <remarks>
    /// Fifteen lines, no container. That the navigator can be tested with this is the entire
    /// reason it talks to <see cref="IScreenScopeFactory"/> instead of naming a DI framework.
    /// </remarks>
    public sealed class FakeScopeFactory : IScreenScopeFactory
    {
        private readonly Dictionary<Type, Func<object>> factories = new();

        /// <summary>How many scopes were created.</summary>
        public int Created { get; private set; }

        /// <summary>How many scopes were disposed.</summary>
        public int Disposed { get; private set; }

        /// <summary>Registers a factory for <typeparamref name="T"/> so scopes can resolve it.</summary>
        public void Bind<T>(Func<object> factory)
        {
            this.factories[typeof(T)] = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public IScreenScope CreateScreenScope()
        {
            ++this.Created;
            return new FakeScreenScope(this);
        }

        private sealed class FakeScreenScope : IScreenScope
        {
            private readonly FakeScopeFactory owner;
            private bool disposed;

            public FakeScreenScope(FakeScopeFactory owner) { this.owner = owner; }

            public object Resolve(Type type)
            {
                if (this.disposed) throw new ObjectDisposedException(nameof(FakeScreenScope));

                return this.owner.factories.TryGetValue(type, out var factory)
                    ? factory()
                    : throw new InvalidOperationException($"FakeScopeFactory: nothing bound for {type.Name}.");
            }

            public void Dispose()
            {
                if (this.disposed) return;
                this.disposed = true;
                ++this.owner.Disposed;
            }
        }
    }
}
