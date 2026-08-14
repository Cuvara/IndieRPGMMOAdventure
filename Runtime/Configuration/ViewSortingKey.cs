using Unity.Entities;

namespace Cuvara.DOTS.Configuration
{
    /// <summary>
    /// 2D sorting layer and order carried from a <see cref="ViewConfig"/>.
    /// </summary>
    /// <remarks>
    /// <b>Carried, deliberately not applied.</b> Nothing in this package touches a
    /// <c>SpriteRenderer</c> — the 2D work is a later roadmap item, and applying a sorting order
    /// means reaching into a renderer component in the same main-thread pass as the transform sync.
    /// Authoring it now costs two ints and means the 2D branch does not have to re-open the config
    /// asset format; pretending it were live would be the worse half of that trade.
    /// </remarks>
    public struct ViewSortingKey : IComponentData
    {
        public int LayerId;
        public int Order;
    }
}
