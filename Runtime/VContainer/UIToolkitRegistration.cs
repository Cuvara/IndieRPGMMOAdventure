namespace Cuvara.UIToolkit.VContainer
{
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Managers;
    using Cuvara.UIToolkit.View;
    using global::VContainer;
    using global::VContainer.Unity;

    /// <summary>
    /// Registers the package's pieces into a VContainer scope, for hosts that use one.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is its own assembly.</b> VContainer is optional, and an assembly
    /// definition that references a package which is not installed is a hard compile error,
    /// not a silently-skipped reference. The fix is a separate assembly carrying both the
    /// <c>versionDefines</c> entry that sets <c>GDK_VCONTAINER</c> when
    /// <c>jp.hadashikick.vcontainer</c> is present AND a <c>defineConstraints</c> on that
    /// same symbol: with VContainer absent the constraint is unmet, the assembly is not
    /// compiled at all, and its unresolvable reference never matters. Putting the file in
    /// the main assembly behind an <c>#if</c> would not work — the reference would still
    /// have to be declared, and would still fail to resolve.</para>
    ///
    /// <para>Nothing in the package needs this. It is a convenience for one popular
    /// container, and a host using any other container — or none — wires the same three
    /// things by hand in about as many lines.</para>
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
