namespace Cuvara.UIToolkit.VContainer
{
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Managers;
    using Cuvara.UIToolkit.View;
    using global::VContainer;
    using global::VContainer.Unity;

    /// <summary>
    /// Registers the package's pieces into a VContainer scope.
    /// </summary>
    /// <remarks>
    /// <para><b>VContainer is a required dependency of this package, not an optional one.</b>
    /// An earlier draft gated this assembly behind a versionDefine and a matching
    /// <c>defineConstraints</c> so the package would install without a container. That gating
    /// is gone: the project standardises on VContainer for all dependency injection, so a host
    /// without it is not a case this package supports, and pretending otherwise cost an
    /// assembly-level branch that nothing exercised.</para>
    ///
    /// <para><b>Why this is still its own assembly.</b> Not for gating any more — for
    /// direction. This assembly may reference the view and manager types; they may not
    /// reference it. Keeping composition in a leaf assembly is what stops a container
    /// reference from creeping into the view layer, where it does not belong.</para>
    ///
    /// <para>A screen's lifetime is a child scope. Create one when the screen opens with
    /// <c>IObjectResolver.CreateScope</c>, register the view, the presenter as an entry point
    /// (<c>IStartable</c>/<c>IDisposable</c>) and any screen-scoped services in it, and
    /// dispose the scope when the screen closes. One <c>Dispose()</c> then unsubscribes the
    /// presenter's handlers, releases screen-scoped services and tears down the view together,
    /// rather than leaving each of those to be remembered separately — which is where UI leaks
    /// come from.</para>
    /// </remarks>
    public static class UIToolkitRegistration
    {
        /// <summary>
        /// Registers <see cref="UIToolkitViewFactory"/> and finds the scene's
        /// <see cref="RootUIDocument"/>.
        /// </summary>
        /// <remarks>
        /// <c>RegisterComponentInHierarchy</c> rather than a <c>FindObjectOfType</c> at
        /// first use: it fails at container build time, naming the scope, if the scene has
        /// no document — instead of at the first screen open, by which point the stack
        /// trace is somewhere else entirely.
        ///
        /// <para>It does NOT register an <see cref="IVisualTreeAssetLoader"/>. That is the
        /// one thing the package cannot supply, because only the host knows whether a key
        /// means an Addressables address, a <c>Resources</c> path, or a dictionary lookup.
        /// Register yours before or after calling this.</para>
        /// </remarks>
        public static IContainerBuilder RegisterUIToolkit(this IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<RootUIDocument>();
            builder.Register<UIToolkitViewFactory>(Lifetime.Singleton);

            return builder;
        }
    }
}
