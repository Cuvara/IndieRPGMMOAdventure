#if CUVARA_DOTS_UNIT_POOLING && CUVARA_DOTS_UNIT_RESOURCES && CUVARA_DOTS_UNITASK && CUVARA_DOTS_VCONTAINER
using Cuvara.DOTS.Provisioning;
using VContainer;

namespace Cuvara.DOTS.GameFoundation
{
    /// <summary>
    /// Registers the GameFoundation-backed view provider, mirroring
    /// <c>GameFoundationVContainer.RegisterGameFoundation</c>.
    /// </summary>
    /// <remarks>
    /// Lives in the GameFoundation assembly rather than the VContainer one so that a project with
    /// VContainer but no GameFoundation still compiles: the core registration extension must not
    /// reference UniT types. Call this <b>after</b> <c>RegisterGameFoundation</c> — it resolves
    /// <c>IAssetsManager</c> and <c>IObjectPoolManager</c> that call registers.
    /// </remarks>
    public static class GameFoundationViewProvisioningVContainer
    {
        /// <summary>
        /// Registers <see cref="GameFoundationViewAssetProvider"/> as <see cref="IViewAssetProvider"/>.
        /// </summary>
        /// <remarks>
        /// The <c>ChunkViewProvisioner</c> is deliberately <b>not</b> registered here.
        /// <c>RegisterDotsViews()</c> owns it, because it is the only call site that can hand the
        /// provisioner an <c>EntityViewCascade</c> as its <c>IViewCascadeSink</c> — and a
        /// provisioner without one releases chunk assets out from under live views. Registering it
        /// in both places would give whichever ran last, silently.
        /// </remarks>
        public static IContainerBuilder RegisterGameFoundationViewProvisioning(this IContainerBuilder builder, Lifetime lifetime = Lifetime.Singleton)
        {
            builder.Register<GameFoundationViewAssetProvider>(lifetime).As<IViewAssetProvider>();
            return builder;
        }
    }
}
#endif
