namespace Cuvara.DOTS.Messaging
{
    /// <summary>A view instance was taken from the pool and linked to an entity.</summary>
    public readonly struct ViewSpawned
    {
        /// <summary><see cref="Views.EntityViewLink.ViewId"/> of the new view.</summary>
        public readonly int ViewId;

        /// <summary>Asset/pool key it came from.</summary>
        public readonly string ViewKey;

        public ViewSpawned(int viewId, string viewKey)
        {
            ViewId = viewId;
            ViewKey = viewKey;
        }
    }
}
