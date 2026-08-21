namespace Cuvara.UIToolkit.VContainer
{
    using System;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Managers;
    using Cuvara.UIToolkit.View;
    using global::VContainer;
    using global::VContainer.Unity;

    /// <summary>Binds the screen flow to a VContainer scope.</summary>
    /// <remarks>
    /// <para>This is the only file in the package that knows both the flow and a DI container.
    /// The navigator itself talks to <see cref="IScreenScopeFactory"/>, so the core assembly
    /// stays container-free and the whole stack is testable without one.</para>
    /// </remarks>
    public static class ScreenFlowRegistration
    {
        /// <summary>
        /// Registers the navigator, the screen registry and the per-screen scope factory.
        /// </summary>
        /// <remarks>
        /// Call once, alongside <c>RegisterUIToolkit()</c>. It does NOT register an
        /// <c>IVisualTreeAssetLoader</c> — only the host knows whether a key is an Addressables
        /// address, a <c>Resources</c> path or a dictionary lookup — and it does not wire an
        /// input source, because Back is a host decision routed through
        /// <see cref="IScreenNavigator.HandleBack"/>.
        /// </remarks>
        public static IContainerBuilder RegisterScreenFlow(this IContainerBuilder builder)
        {
            builder.Register<ScreenRegistry>(Lifetime.Singleton);
            builder.Register<IScreenScopeFactory, VContainerScreenScopeFactory>(Lifetime.Singleton);

            builder.Register<ScreenNavigator>(Lifetime.Singleton)
                .As<IScreenNavigator>()
                .AsSelf();

            return builder;
        }

        /// <summary>Declares one screen: its presenter, its view, and the key its UXML loads from.</summary>
        /// <remarks>
        /// <para><b>Generic, so nothing is constructed by runtime <c>Type</c>.</b> The presenter is
        /// registered as itself, which means VContainer builds it with its real constructor
        /// dependencies — actual constructor injection rather than a service-locator call that
        /// happens to work. It is also compiler-checked and survives IL2CPP stripping without a
        /// preserve attribute anywhere.</para>
        ///
        /// <para><c>Lifetime.Scoped</c> is what makes one screen equal one scope: each
        /// <c>CreateScope</c> gets its own presenter, and disposing that scope disposes it.</para>
        /// </remarks>
        public static IContainerBuilder RegisterScreen<TPresenter, TView>(
            this IContainerBuilder builder,
            string                 assetKey,
            ScreenOptions          options = ScreenOptions.None)
            where TPresenter : class, IUIToolkitScreenPresenter
            where TView : class, Cuvara.UIToolkit.Core.IUIToolkitView
        {
            builder.Register<TPresenter>(Lifetime.Scoped);

            // The registry is a singleton instance built at container build time, so the
            // registration has to happen through a build callback rather than immediately —
            // the registry does not exist yet while Configure is running.
            builder.RegisterBuildCallback(container =>
                container.Resolve<ScreenRegistry>().Register(typeof(TPresenter), typeof(TView), assetKey, options));

            return builder;
        }

        /// <summary>Declares a modal. Identical to <see cref="RegisterScreen{TPresenter,TView}"/> but modal by default.</summary>
        /// <remarks>
        /// Every option passed here has behaviour and a test that fails if the behaviour is
        /// removed. That is a rule for this package, and it comes from measurement: the popup
        /// attribute in the framework this replaces declared three flags, two of which were read
        /// zero times — so setting them did nothing and warned about nothing.
        /// </remarks>
        public static IContainerBuilder RegisterPopup<TPresenter, TView>(
            this IContainerBuilder builder,
            string                 assetKey,
            ScreenOptions          options = ScreenOptions.Modal | ScreenOptions.DimsBelow)
            where TPresenter : class, IUIToolkitScreenPresenter
            where TView : class, Cuvara.UIToolkit.Core.IUIToolkitView
        {
            return builder.RegisterScreen<TPresenter, TView>(assetKey, options | ScreenOptions.Modal);
        }
    }

    /// <summary>Creates one VContainer child scope per screen.</summary>
    /// <remarks>
    /// <para>A child scope is what gives a screen a real lifetime: its presenter and anything
    /// registered <c>Scoped</c> are built inside it, and one <c>Dispose</c> ends all of them
    /// together. This is the piece that lets a screen author write no teardown code at all.</para>
    ///
    /// <para>The scope installs nothing of its own. The view is attached to the presenter by the
    /// navigator after construction rather than registered into the scope, because the presenter's
    /// constructor deliberately does not take one — see the presenter base's remarks on why its
    /// constructor stays clean.</para>
    /// </remarks>
    public sealed class VContainerScreenScopeFactory : IScreenScopeFactory
    {
        private readonly IObjectResolver resolver;

        public VContainerScreenScopeFactory(IObjectResolver resolver)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public IScreenScope CreateScreenScope() => new VContainerScreenScope(this.resolver.CreateScope());
    }

    /// <summary>One screen's VContainer child scope.</summary>
    internal sealed class VContainerScreenScope : IScreenScope
    {
        private IScopedObjectResolver scope;

        public VContainerScreenScope(IScopedObjectResolver scope)
        {
            this.scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        public object Resolve(Type type)
        {
            if (this.scope == null) throw new ObjectDisposedException(nameof(VContainerScreenScope));

            return this.scope.Resolve(type);
        }

        public void Dispose()
        {
            var toDispose = this.scope;
            this.scope = null;
            toDispose?.Dispose();
        }
    }
}
