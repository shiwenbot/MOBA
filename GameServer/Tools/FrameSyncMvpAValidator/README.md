# FrameSyncMvpAValidator

用于 `状态帧同步-MVP-a` 阶段的自动化验收：

- `GameShared/FrameSync` 确定性静态约束扫描
- `TickDispatcher` 5 分钟 Tick 稳定性与漂移指标
- `FrameTimerService` 触发/重入规则验证
- `CommandPool<T>` 1000 次复用与 Reset 协议验证

## 运行

```powershell
dotnet run --project 'D:\unity\Tencent\TEngine\GameServer\Tools\FrameSyncMvpAValidator\FrameSyncMvpAValidator.csproj' -- --duration-seconds 300 --update-hz 240
```

默认输出报告：

`D:\unity\Tencent\TEngine\Plan\状态帧同步\状态帧同步-MVP-a验收报告.md`
