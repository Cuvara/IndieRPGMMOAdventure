namespace Cuvara.UIToolkit.Core
{
    using System;
    using System.Collections.Generic;
    using Cysharp.Threading.Tasks;
    using UnityEngine.UIElements;

    /// <summary>
    /// Turns a key into a <see cref="VisualTreeAsset"/>. The host decides what a key means.
    /// </summary>
    /// <remarks>
    /// <para>One method, on purpose. The package needs exactly one thing from an asset
    /// pipeline — "give me the UXML behind this name" — and asking for anything more would
    /// make the seam harder to implement than the problem it solves.</para>
    ///
    /// <para><b>Why this exists rather than a dependency on a real loader.</b> The host
    /// framework this was extracted from loads through an asset manager that comes from an
    /// OpenUPM scoped registry. A UPM package cannot declare a scoped registry of its own,
    /// so such a package can never be a resolvable dependency of this one — a consumer
    /// installing from a git URL would get an unresolvable manifest. Addressables is the
    /// same shape of problem for a different reason: it is a heavyweight optional
    /// dependency that many projects deliberately do not use. So the package declares the
    /// interface and the host implements it over Addressables, <c>Resources</c>, a
    /// dictionary, or whatever it already has.</para>
    /// </remarks>
    public interface IVisualTreeAssetLoader
    {
        /// <summary>Loads the UXML registered under <paramref name="key"/>.</summary>
        /// <exception cref="KeyNotFoundException">No asset is registered under that key.</exception>
        UniTask<VisualTreeAsset> LoadAsync(string key);
    }

    /// <summary>Creates presenters. The host binds this to its container.</summary>
    /// <remarks>
    /// <para>The collection adapters build one presenter per realized row and have no
    /// business knowing which DI container — if any — the host uses. In the framework this
    /// was extracted from, the adapters reached for a container through a static service
    /// locator, which is both a dependency and the reason those adapters could not be
    /// exercised without a live scene.</para>
    ///
    /// <para>Default to <c>ActivatorPresenterInstantiator</c> when the host has no
    /// container: a row presenter with a parameterless constructor needs nothing else.</para>
    /// </remarks>
    public interface IPresenterInstantiator
    {
        /// <summary>Constructs an instance of <paramref name="type"/>.</summary>
        object Instantiate(Type type);
    }
}
