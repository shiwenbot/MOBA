namespace GameShared
{
    /// <summary>
    /// 双端共享代码验证测试
    /// 验证同一份源码在 Unity 客户端和服务端都能编译和运行
    /// </summary>
    public static class SharedTest
    {
        public static void Run()
        {
            const string message = "[GameShared] 双端共享代码验证成功！";

#if FANTASY_UNITY
            UnityEngine.Debug.LogWarning(message);
#elif FANTASY_NET
            System.Console.WriteLine(message);
#else
            // 未知平台，降级处理
            System.Console.WriteLine($"[UnknownPlatform] {message}");
#endif
        }
    }
}
