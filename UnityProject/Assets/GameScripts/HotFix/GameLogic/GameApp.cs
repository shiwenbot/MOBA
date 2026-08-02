using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
#if ENABLE_OBFUZ
using Obfuz;
#endif
using TEngine;
#pragma warning disable CS0436

/// <summary>
/// 游戏App。
/// </summary>
#if ENABLE_OBFUZ
[ObfuzIgnore(ObfuzScope.TypeName | ObfuzScope.MethodName)]
#endif
public partial class GameApp
{
    private static List<Assembly> _hotfixAssembly;

    /// <summary>
    /// 热更域App主入口。
    /// </summary>
    /// <param name="objects"></param>
    public static void Entrance(object[] objects)
    {
        GameEventHelper.Init();
        _hotfixAssembly = (List<Assembly>)objects[0];
        Log.Warning("======= 看到此条日志代表你成功运行了热更新代码 =======");
        Log.Warning("======= Entrance GameApp =======");
        Utility.Unity.AddDestroyListener(Release);
        Log.Warning("======= StartGameLogic =======");
        StartGameLogic();
    }

    private static void StartGameLogic()
    {
        Init().Forget();

        async UniTaskVoid Init()
        {
            await GameClient.Instance.InitAsync(_hotfixAssembly);
            GameplaySandboxView.Create();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            SkillGraphStepBSmokeTest.Run();
            // 带宽节省量调试面板（仅开发期）。服务端需开 BATTLE_BANDWIDTH_STATS=1 才有数据。
            GameModule.UI.ShowUIAsync<BandwidthStatsUI>();
#endif
        }
    }

    private static void Release()
    {
        SingletonSystem.Release();
        Log.Warning("======= Release GameApp =======");
    }
}
