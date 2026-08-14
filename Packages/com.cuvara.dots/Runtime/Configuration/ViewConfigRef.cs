using Unity.Entities;

namespace Cuvara.DOTS.Configuration
{
    /// <summary>
    /// "Use the config at this index in the session's <see cref="ViewConfigTable"/>."
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate optional component rather than a field on <c>EntityViewRequest</c>, and that is a
    /// deliberate choice about default values. An <c>int</c> field would default to 0, which is a
    /// perfectly valid config index — so an entity that never set it would silently spawn whatever
    /// config happens to be first in the table. Encoding "unset" as -1 or index+1 works but relies on
    /// everyone remembering the encoding. Component presence has no such failure mode: either the
    /// entity has a config or it does not.
    /// </para>
    /// <para>
    /// It also keeps the bare-key path exactly as it was — an entity with only
    /// <c>EntityViewRequest.ViewKey</c> behaves identically to before this component existed.
    /// </para>
    /// </remarks>
    public struct ViewConfigRef : IComponentData
    {
        public int Index;
    }
}
