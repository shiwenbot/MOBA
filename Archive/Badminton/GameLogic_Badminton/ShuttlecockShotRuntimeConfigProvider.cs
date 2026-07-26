using System;
using GameShared.Badminton.Config;
using UnityEngine;
using Log = TEngine.Log;

namespace GameLogic
{
    public sealed class ShuttlecockShotRuntimeConfigProvider : IShuttlecockShotConfigProvider
    {
        private static bool s_loggedFallbackWarning;

        public static ShuttlecockShotRuntimeConfigProvider Instance { get; } = new ShuttlecockShotRuntimeConfigProvider();

        private ShuttlecockShotRuntimeConfigProvider()
        {
        }

        public bool TryGet(ShuttlecockShotType shotType, out ShuttlecockShotDefinition definition)
        {
            try
            {
                if (GameShared.Badminton.Config.ShuttlecockShotConfigProvider.Instance.TryGet(shotType, out definition))
                {
                    return true;
                }
            }
            catch (Exception exception)
            {
                if (!s_loggedFallbackWarning)
                {
                    s_loggedFallbackWarning = true;
                    Log.Warning($"[Badminton] 正式球路配置未就绪，回退到内置配置。reason={exception.Message}");
                }
            }

            return ShuttlecockShotConfigFallback.Instance.TryGet(shotType, out definition);
        }
    }
}
