using System;
using GameShared.FrameSync.Determinism;
using UnityEngine;

namespace GameShared.Badminton
{
    public static class CourtConstants
    {
        public const float FullLength = 13.4f;
        public const float HalfLength = FullLength * 0.5f;
        public const float SinglesWidth = 5.18f;
        public const float HalfSinglesWidth = SinglesWidth * 0.5f;
        public const float NetHeightAtPost = 1.55f;
        public const float NetHeightAtCenter = 1.524f;
        public const float ServiceLineDistanceFromNet = 1.98f;
        public const float BoundaryEpsilon = 0.0001f;

        public static readonly Vector2 NearLeftCorner = new Vector2(-HalfSinglesWidth, -HalfLength);
        public static readonly Vector2 NearRightCorner = new Vector2(HalfSinglesWidth, -HalfLength);
        public static readonly Vector2 FarLeftCorner = new Vector2(-HalfSinglesWidth, HalfLength);
        public static readonly Vector2 FarRightCorner = new Vector2(HalfSinglesWidth, HalfLength);

        public static bool IsInBounds(Vector2 xz)
        {
            DeterminismRules.AssertFinite(xz.x, nameof(xz.x));
            DeterminismRules.AssertFinite(xz.y, nameof(xz.y));

            return Math.Abs(xz.x) <= HalfSinglesWidth + BoundaryEpsilon &&
                   Math.Abs(xz.y) <= HalfLength + BoundaryEpsilon;
        }
    }
}
