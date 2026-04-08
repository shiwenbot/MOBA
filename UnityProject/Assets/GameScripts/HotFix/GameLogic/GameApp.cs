using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
using GameShared.SkillGraph;
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
            // 初始化 Fantasy 网络模块
            await GameClient.Instance.InitAsync(_hotfixAssembly);
            // GameEvent.Get<ILoginUI>().ShowLoginUI();
            GameModule.UI.ShowUIAsync<LoginUI>();
            await TriggerStartupSkillGraph();
        }

        async UniTask TriggerStartupSkillGraph()
        {
            await UniTask.Delay(1000);

            try
            {
                SkillGraphRunResult result = await SkillExecutor.Instance.CastSkill("NewSkillGraph", new SkillContext());
                if (result.IsSuccess)
                {
                    Log.Warning("======= Startup SkillGraph Cast Complete: NewSkillGraph =======");
                    return;
                }

                if (result.IsCancelled)
                {
                    Log.Warning($"Startup SkillGraph Cast Cancelled: {result.Message}");
                    return;
                }

                Log.Error($"Startup SkillGraph Cast Failed: {result.Message}");
                if (result.Exception != null)
                    Log.Error(result.Exception.ToString());
            }
            catch (System.Exception exception)
            {
                Log.Error($"Startup SkillGraph Cast Failed: {exception}");
            }
        }
    }
    
    private static void Release()
    {
        SingletonSystem.Release();
        Log.Warning("======= Release GameApp =======");
    }
}
