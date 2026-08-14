using Unity.Mathematics;

namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// Places the server's 2D <c>(x, y)</c> coordinates into the client's 3D world.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Caller-supplied, not hardcoded and not per-archetype.</b> The reference implementation this
    /// was lifted from wrote <c>new float3(x, 0.5f, y)</c> inline, and that one literal is two
    /// unrelated things fused together:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Which plane the server's coordinates live on</b> — a property of the <i>world</i>. Every
    /// entity in a session agrees on it, and a per-archetype setting would let two archetypes
    /// disagree about which axis is up, which is never a thing anyone wants. That is this type.
    /// </description></item>
    /// <item><description>
    /// <b>The half-height lift so a capsule's pivot sits on the ground</b> — a property of the
    /// <i>art</i>, which differs per archetype and already has a home:
    /// <see cref="Cuvara.DOTS.Configuration.ViewConfig.PositionOffset"/>, applied to the view
    /// instance by the spawn path. Keeping it there is strictly better than the sample's constant:
    /// the entity stays on the plane where gameplay maths wants it, and only the visual is lifted.
    /// </description></item>
    /// </list>
    /// <para>
    /// So the default here is <see cref="XZPlane"/> — the sample's mapping <i>minus</i> the lift —
    /// and a project whose art needs the lift authors it as a config offset rather than getting it
    /// silently applied to entity positions.
    /// </para>
    /// <para>
    /// It is a constructor argument rather than a <see cref="ViewConfig"/> field or a constant
    /// because a 2D game rendering on XY, or a server that ever changes its axis convention, is a
    /// one-line change at the composition root instead of a fork of this assembly.
    /// </para>
    /// </remarks>
    public readonly struct SnapshotSpaceMapping
    {
        /// <summary>World direction the server's <c>+x</c> maps to.</summary>
        public readonly float3 Right;

        /// <summary>World direction the server's <c>+y</c> maps to.</summary>
        public readonly float3 Forward;

        /// <summary>World position of the server's origin.</summary>
        public readonly float3 Origin;

        public SnapshotSpaceMapping(float3 right, float3 forward, float3 origin)
        {
            Right = right;
            Forward = forward;
            Origin = origin;
        }

        /// <summary>
        /// Server <c>(x, y)</c> onto Unity's ground plane: <c>(x, 0, y)</c>. The default, and what a
        /// top-down camera over an XZ world expects.
        /// </summary>
        public static SnapshotSpaceMapping XZPlane =>
            new SnapshotSpaceMapping(new float3(1f, 0f, 0f), new float3(0f, 0f, 1f), float3.zero);

        /// <summary>
        /// Server <c>(x, y)</c> onto Unity's XY plane: <c>(x, y, 0)</c>. For a 2D presentation.
        /// </summary>
        public static SnapshotSpaceMapping XYPlane =>
            new SnapshotSpaceMapping(new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), float3.zero);

        /// <summary>
        /// True when this mapping was constructed rather than left at <c>default</c>. A
        /// <c>default</c> mapping collapses every entity onto the origin, which looks like a
        /// networking failure rather than a configuration one, so callers are given a way to check
        /// and <see cref="DotsEntityView"/> substitutes <see cref="XZPlane"/>.
        /// </summary>
        public bool IsPopulated => !Right.Equals(float3.zero) || !Forward.Equals(float3.zero);

        /// <summary>Projects one snapshot position into world space.</summary>
        public float3 ToWorld(float x, float y) => Origin + Right * x + Forward * y;
    }
}
