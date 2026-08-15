namespace Cuvara.DOTS.Messaging
{
    /// <summary>A view instance was returned to the pool. Its handle is dead from here on.</summary>
    public readonly struct ViewDespawned
    {
        public readonly int ViewId;
        public readonly string ViewKey;

        public ViewDespawned(int viewId, string viewKey)
        {
            ViewId = viewId;
            ViewKey = viewKey;
        }
    }
}
