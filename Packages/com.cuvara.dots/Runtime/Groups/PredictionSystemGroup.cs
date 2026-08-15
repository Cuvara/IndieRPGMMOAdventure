using Unity.Entities;

namespace Cuvara.DOTS.Groups
{
    /// <summary>
    /// Client-side prediction: reconcile against the newest authoritative state, then advance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>After <see cref="SnapshotApplyGroup"/>, and that ordering is the whole reason both groups
    /// exist.</b> A predictor reconciles against the anchor written by snapshot application, so
    /// reconciling first would use the previous frame's authoritative position — a one-frame-stale
    /// correction, which is indistinguishable from prediction being slightly wrong and would be
    /// debugged as a tuning problem.
    /// </para>
    /// <para>
    /// Still inside <see cref="NetcodeSystemGroup"/>, so both halves land in
    /// <c>InitializationSystemGroup</c> — before this frame's <c>TransformSystemGroup</c> and long
    /// before <c>ViewSystemGroup</c>. A position predicted here is a positioned view in the same
    /// frame, exactly as an applied snapshot is.
    /// </para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(NetcodeSystemGroup))]
    [UpdateAfter(typeof(SnapshotApplyGroup))]
    public partial class PredictionSystemGroup : ComponentSystemGroup
    {
    }
}
