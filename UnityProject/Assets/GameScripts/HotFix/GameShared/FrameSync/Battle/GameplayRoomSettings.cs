using FixedMathSharp;

namespace GameShared.FrameSync.Battle
{
    /// <summary>
    /// P1 俯视移动沙盒的共享空间规则。客户端和服务端必须使用同一份配置。
    /// </summary>
    public static class GameplayRoomSettings
    {
        public static readonly Fixed64 RoomHalfWidth = (Fixed64)12.0f;
        public static readonly Fixed64 RoomHalfHeight = (Fixed64)8.0f;
        public static readonly Fixed64 PlayerRadius = (Fixed64)0.45f;

        // Quantize bounds from the same float geometry used by Box2D before entering Fixed64.
        public static readonly Fixed64 PlayerMaxX = (Fixed64)((float)RoomHalfWidth - (float)PlayerRadius);
        public static readonly Fixed64 PlayerMinX = -PlayerMaxX;
        public static readonly Fixed64 PlayerMaxY = (Fixed64)((float)RoomHalfHeight - (float)PlayerRadius);
        public static readonly Fixed64 PlayerMinY = -PlayerMaxY;
    }
}
