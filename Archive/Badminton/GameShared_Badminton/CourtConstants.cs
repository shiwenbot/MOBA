using FixedMathSharp;

namespace GameShared.Badminton
{
    public static class CourtConstants
    {
        public static readonly Fixed64 FullLength = new Fixed64(13.4);
        public static readonly Fixed64 HalfLength = FullLength * Fixed64.Half;
        public static readonly Fixed64 SinglesWidth = new Fixed64(5.18);
        public static readonly Fixed64 HalfSinglesWidth = SinglesWidth * Fixed64.Half;
        public static readonly Fixed64 NetHeightAtPost = new Fixed64(1.55);
        public static readonly Fixed64 NetHeightAtCenter = new Fixed64(1.524);
        public static readonly Fixed64 ServiceLineDistanceFromNet = new Fixed64(1.98);
        public static readonly Fixed64 BoundaryEpsilon = new Fixed64(0.0001);

        public static readonly Vector2d NearLeftCorner = new Vector2d(-HalfSinglesWidth, -HalfLength);
        public static readonly Vector2d NearRightCorner = new Vector2d(HalfSinglesWidth, -HalfLength);
        public static readonly Vector2d FarLeftCorner = new Vector2d(-HalfSinglesWidth, HalfLength);
        public static readonly Vector2d FarRightCorner = new Vector2d(HalfSinglesWidth, HalfLength);

        public static bool IsInBounds(Vector2d xz)
        {
            return FixedMath.Abs(xz.x) <= HalfSinglesWidth + BoundaryEpsilon &&
                   FixedMath.Abs(xz.y) <= HalfLength + BoundaryEpsilon;
        }
    }
}
