namespace Cuvara.UIToolkit.Collections
{
    using System;
    using Cuvara.UIToolkit.Core;

    /// <summary>
    /// Default <see cref="IPresenterInstantiator"/>: builds a presenter with
    /// <see cref="Activator.CreateInstance(Type)"/>.
    /// </summary>
    /// <remarks>
    /// The fallback the adapters use when no instantiator is supplied — a presenter with a
    /// parameterless constructor needs nothing more than this. A host with its own
    /// container binds <see cref="IPresenterInstantiator"/> to that container instead and
    /// passes it explicitly; this type never has to know that binding exists.
    /// </remarks>
    public sealed class ActivatorPresenterInstantiator : IPresenterInstantiator
    {
        /// <summary>Shared stateless instance; there is never a reason to allocate more than one.</summary>
        public static ActivatorPresenterInstantiator Instance { get; } = new();

        public object Instantiate(Type type)
        {
            return Activator.CreateInstance(type);
        }
    }
}
