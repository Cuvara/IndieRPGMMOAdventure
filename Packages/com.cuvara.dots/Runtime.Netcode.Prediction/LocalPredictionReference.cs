using Cuvara.Netcode.Prediction;
using Cuvara.Netcode.World;
using Unity.Entities;

namespace Cuvara.DOTS.Netcode.Prediction
{
    /// <summary>
    /// Singleton carrying the session's predictor and the world state it reconciles against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One predictor instance, owned by the composition root, never constructed here.</b>
    /// <c>RecordInput</c> is called by whatever samples input; <c>Reconcile</c> is called by the
    /// driving system. If those are two different <see cref="LocalMovePredictor"/> objects, the one
    /// that reconciles has an empty input buffer forever — and an empty buffer replays nothing, so
    /// the symptom is prediction that appears to be switched on and to do nothing. That is a
    /// wiring mistake with no error message, which is why the instance arrives from outside rather
    /// than being created by the system that uses it.
    /// </para>
    /// <para>
    /// <see cref="World"/> is here because the reconciliation tick is not on the entity and cannot
    /// be: <c>IEntityView.SetState</c> carries no tick, so <c>ReconciliationAnchor</c> deliberately
    /// carries none either. <c>WorldState.AckTick</c> is the anchor's tick — netcode documents it as
    /// "the newest input tick the server accepted for this player" — and it is read from the same
    /// object the binder is merging into, so the position and the tick always come from one
    /// snapshot rather than from two that happen to be adjacent.
    /// </para>
    /// </remarks>
    public sealed class LocalPredictionReference : IComponentData
    {
        public LocalMovePredictor Predictor;

        public WorldState World;
    }
}
