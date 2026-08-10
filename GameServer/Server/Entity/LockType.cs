namespace Fantasy;

public enum LockType
{
    None = 0,

    /// <summary>
    /// 鉴权注册锁
    /// </summary>
    Authentication_RegisterLock = 1,

    /// <summary>
    /// 鉴权登录锁
    /// </summary>
    Authentication_LoginLock = 2,

    /// <summary>
    /// 登录会话索引初始化锁
    /// </summary>
    Authentication_SessionIndexLock = 3,

    /// <summary>
    /// 战斗账号槽位认领锁
    /// </summary>
    Battle_JoinLock = 4,
}
