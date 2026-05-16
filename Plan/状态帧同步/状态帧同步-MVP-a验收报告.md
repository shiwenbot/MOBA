# 状态帧同步-MVP-a 验收报告

- 生成时间：`2026-05-16 08:21:57 +08:00`
- 验收工具：`GameServer/Tools/FrameSyncMvpAValidator`
- 运行参数：`duration=300s, updateHz=60`
- 结果：`未通过`

## 核心指标

- Tick 调用间隔 p95：`49.998ms`（阈值 `< 40ms`）
- 仿真时间漂移：`0.040914s`（阈值 `< 1s`）
- 帧号连续性：`连续`
- 总帧数：`8999`
- Tick 回调异常数：`0`

## 子项验证

- 确定性静态约束扫描：`通过`
- FrameTimerService 行为：`通过`
- CommandPool< T > 行为：`通过`

## 警告

- Advisory pattern [Dictionary<K,V> usage] in FrameSync/Battle/BattleWorldState.cs. Ensure deterministic code paths do not depend on collection iteration order.
- Advisory pattern [Dictionary<K,V> usage] in FrameSync/Snapshot/SnapshotBuffer.cs. Ensure deterministic code paths do not depend on collection iteration order.

## 失败项

- Tick interval P95 49.998ms exceeds threshold 40ms.
