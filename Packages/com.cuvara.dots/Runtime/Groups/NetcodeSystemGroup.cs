using Unity.Entities;

namespace Cuvara.DOTS.Groups
{
    /// <summary>
    /// Wire traffic reaches the world here: snapshot application and anything else that turns
    /// received bytes into component data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// First thing in the frame, before provisioning and long before simulation, because everything
    /// downstream is a reaction to what arrived. A snapshot applied later than this would be a frame
    /// late for every consumer that read the world in between.
    /// </para>
    /// <para>
    /// <b>Empty in this version.</b> The group exists now rather than when its first system lands so
    /// that consumers can write <c>[UpdateAfter(typeof(NetcodeSystemGroup))]</c> today and not have
    /// it start meaning something different later. An empty <see cref="ComponentSystemGroup"/> costs
    /// an empty update call.
    /// </para>
    /// <para>
    /// <see cref="DisableAutoCreationAttribute"/> on purpose — see
    /// <see cref="Views.DotsViewBootstrap"/> for why every package system is created by hand.
    /// </para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class NetcodeSystemGroup : ComponentSystemGroup
    {
    }
}
