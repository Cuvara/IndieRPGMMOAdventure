namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// Axis-aligned play area, in the package's own vocabulary.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>Shared.GameLogic.Components.MapBounds</c> in shape but not in identity: the
    /// package must expose the same value type whether or not the shared library is installed, so
    /// the conversion lives in <c>Cuvara.DOTS.GameLogic</c> and nothing here knows that type exists.
    /// Edges are normalized on construction, the same way <c>MapBounds</c> does it.
    /// </remarks>
    public readonly struct SimBounds
    {
        public readonly float MinX;
        public readonly float MinY;
        public readonly float MaxX;
        public readonly float MaxY;

        public SimBounds(float minX, float minY, float maxX, float maxY)
        {
            MinX = minX < maxX ? minX : maxX;
            MaxX = minX < maxX ? maxX : minX;
            MinY = minY < maxY ? minY : maxY;
            MaxY = minY < maxY ? maxY : minY;
        }

        /// <summary>Bounds of <paramref name="width"/> x <paramref name="height"/> centred on the origin.</summary>
        public static SimBounds FromSize(float width, float height)
        {
            var halfW = (width < 0f ? -width : width) * 0.5f;
            var halfH = (height < 0f ? -height : height) * 0.5f;
            return new SimBounds(-halfW, -halfH, halfW, halfH);
        }

        public float Width => MaxX - MinX;

        public float Height => MaxY - MinY;
    }
}
