namespace Cuvara.UIToolkit.Flow
{
    using System;
    using System.Collections.Generic;

    /// <summary>What the flow needs to know to build one screen.</summary>
    public readonly struct ScreenRegistration
    {
        /// <summary>The presenter type. Resolved from the screen's own scope.</summary>
        public readonly Type PresenterType;

        /// <summary>The concrete view type. Constructed through its <c>(VisualTreeAsset)</c> constructor.</summary>
        public readonly Type ViewType;

        /// <summary>The key the host's loader resolves to this screen's UXML.</summary>
        public readonly string AssetKey;

        /// <summary>How this screen behaves in the stack.</summary>
        public readonly ScreenOptions Options;

        public ScreenRegistration(Type presenterType, Type viewType, string assetKey, ScreenOptions options)
        {
            this.PresenterType = presenterType ?? throw new ArgumentNullException(nameof(presenterType));
            this.ViewType      = viewType ?? throw new ArgumentNullException(nameof(viewType));
            this.AssetKey      = string.IsNullOrEmpty(assetKey) ? throw new ArgumentException("A screen needs an asset key.", nameof(assetKey)) : assetKey;
            this.Options       = options;
        }
    }

    /// <summary>
    /// Which screens exist, and what each one is made of.
    /// </summary>
    /// <remarks>
    /// <para><b>Populated by explicit registration, not by scanning for an attribute.</b> The
    /// framework this package replaces recovered all of this reflectively from an attribute on
    /// the presenter type and then constructed the presenter through a service locator. Three
    /// things go wrong with that, and all three are avoided by paying one registration line per
    /// screen:</para>
    /// <list type="number">
    /// <item><b>AOT.</b> Android and WebGL are IL2CPP with managed stripping enabled. Reflective
    /// construction needs a preserve attribute on every generated constructor and care with the
    /// link file — and a desktop build exercises neither IL2CPP nor the stripper, so the quickest
    /// build cannot validate it and a green result there actively misleads.</item>
    /// <item><b>It is real constructor injection.</b> The registration is generic, so the
    /// container builds the presenter with its actual dependencies. Constructing by runtime
    /// <c>Type</c> through a locator is the thing the project's UI contract forbids outright.</item>
    /// <item><b>It is greppable.</b> "Which screens exist?" is answered by reading one file
    /// rather than by searching for an attribute across an assembly.</item>
    /// </list>
    ///
    /// <para>Registration is keyed by presenter type because that is what a caller names:
    /// <c>PushAsync&lt;InventoryPresenter&gt;()</c>.</para>
    /// </remarks>
    public sealed class ScreenRegistry
    {
        private readonly Dictionary<Type, ScreenRegistration> registrations = new();

        /// <summary>How many screens are registered.</summary>
        public int Count => this.registrations.Count;

        /// <summary>Every registered presenter type.</summary>
        public IEnumerable<Type> PresenterTypes => this.registrations.Keys;

        /// <summary>Records how to build <paramref name="presenterType"/>.</summary>
        /// <exception cref="InvalidOperationException">
        /// The same presenter type is registered twice. That is always a mistake — two
        /// registrations mean two different asset keys or option sets for one screen, and which
        /// one wins would depend on registration order. Failing at container build is far
        /// cheaper than discovering it when the wrong UXML loads.
        /// </exception>
        public void Register(Type presenterType, Type viewType, string assetKey, ScreenOptions options = ScreenOptions.None)
        {
            var registration = new ScreenRegistration(presenterType, viewType, assetKey, options);

            if (this.registrations.TryGetValue(presenterType, out var existing))
            {
                throw new InvalidOperationException(
                    $"{presenterType.Name} is already registered with key '{existing.AssetKey}'; refusing to re-register it with '{assetKey}'. "
                    + "A screen has one registration.");
            }

            this.registrations.Add(presenterType, registration);
        }

        /// <summary>Looks up how to build <paramref name="presenterType"/>.</summary>
        public bool TryGet(Type presenterType, out ScreenRegistration registration)
        {
            return this.registrations.TryGetValue(presenterType, out registration);
        }

        /// <summary>Looks up a registration, or throws with a message that says what to do about it.</summary>
        /// <exception cref="InvalidOperationException">
        /// The screen was never registered. The message names the missing type and the call that
        /// would fix it, because the alternative — a key-not-found from a dictionary — leaves the
        /// reader to work out both.
        /// </exception>
        public ScreenRegistration Get(Type presenterType)
        {
            if (this.TryGet(presenterType, out var registration)) return registration;

            throw new InvalidOperationException(
                $"{presenterType?.Name ?? "null"} is not a registered screen. Add "
                + $"builder.RegisterScreen<{presenterType?.Name}, YourView>(\"YourAssetKey\") to your container configuration.");
        }
    }
}
