using System;

namespace GameShared.FrameSync.Core
{
    public interface IFrameSyncLogger
    {
        void LogError(Exception exception, string context);
    }
}
