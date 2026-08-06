using GameShared.FrameSync.Battle;

namespace Fantasy;

public sealed class AttributeBroadcastBaseline
{
    public AttributeBroadcastBaseline(PlayerAttributeSnapshot attributes, uint frameIndex)
    {
        Update(attributes, frameIndex);
    }

    public PlayerAttributeSnapshot Attributes { get; private set; }
    public uint FrameIndex { get; private set; }

    public void Update(PlayerAttributeSnapshot attributes, uint frameIndex)
    {
        Attributes = attributes;
        FrameIndex = frameIndex;
    }
}
