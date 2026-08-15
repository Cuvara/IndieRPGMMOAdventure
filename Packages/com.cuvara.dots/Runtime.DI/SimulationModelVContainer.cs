#if CUVARA_DOTS_VCONTAINER
using Cuvara.DOTS.Simulation;
using VContainer;

namespace Cuvara.DOTS.DI
{
    /// <summary>
    /// Chooses which <see cref="ISimulationModel"/> the container gets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the single conditional that decides whether the shared game logic is in play.</b>
    /// Everything else — the interface, the value types, the passive model, every consumer call
    /// site — compiles identically either way. A consumer that resolves
    /// <see cref="ISimulationModel"/> is byte-identical whether or not
    /// <c>com.rpgmmo.shared-gamelogic</c> is installed; only the line below differs.
    /// </para>
    /// <para>
    /// The pattern is the one <c>com.gdk.core</c> uses for <c>GDK_VCONTAINER</c>: the define comes
    /// from the asmdef's <c>versionDefines</c>, so it is driven by what the package manager actually
    /// resolved rather than by a define someone has to remember to set in Player Settings.
    /// </para>
    /// </remarks>
    public static class SimulationModelVContainer
    {
        /// <summary>
        /// Registers the best available <see cref="ISimulationModel"/>:
        /// <c>SharedGameLogicSimulation</c> when the shared package is installed, otherwise
        /// <see cref="PassiveSimulationModel"/>, which predicts nothing and reports
        /// <c>IsAuthoritative == false</c>.
        /// </summary>
        public static IContainerBuilder RegisterSimulationModel(this IContainerBuilder builder, Lifetime lifetime = Lifetime.Singleton)
        {
#if CUVARA_SHARED_GAMELOGIC
            builder.Register<Cuvara.DOTS.GameLogic.SharedGameLogicSimulation>(lifetime).As<ISimulationModel>();
#else
            builder.Register<PassiveSimulationModel>(lifetime).As<ISimulationModel>();
#endif
            return builder;
        }
    }
}
#endif
