namespace Scripts.UI.Hud.Ecs
{
    using Cuvara.UIToolkit.Ecs;
    using Unity.Entities;

    /// <summary>
    /// Turns the <see cref="HudState"/> singleton into a <see cref="HudSnapshot"/> and
    /// pushes it to registered sinks — the game's one concrete
    /// <see cref="EcsViewModelBridge{TComponent,TViewModel}"/>.
    /// </summary>
    /// <remarks>
    /// <para>Everything interesting is inherited: disabled while no sink is registered,
    /// chunk change filter, the one-shot unfiltered catch-up when a sink arrives. This
    /// class is a <see cref="Convert"/> override and nothing else — which is the package's
    /// stated intent for a host bridge.</para>
    ///
    /// <para><see cref="DisableAutoCreationAttribute"/> because <see cref="HudEcsBootstrap"/>
    /// installs it explicitly (and tests into a throwaway world); the group attribute is
    /// restated so the placement is visible here. No <c>HasChanged</c> override:
    /// <see cref="HudStateSystem"/> already deduplicates at the write, so the filter alone
    /// is exact.</para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class HudBridgeSystem : EcsViewModelBridge<HudState, HudSnapshot>
    {
        protected override HudSnapshot Convert(in HudState component) => HudSnapshot.From(component);
    }
}
