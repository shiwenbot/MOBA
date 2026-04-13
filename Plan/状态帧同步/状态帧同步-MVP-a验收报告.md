# 状态帧同步-MVP-a 验收报告

- 生成时间：`2026-04-13 20:10:22 +08:00`
- 验收工具：`GameServer/Tools/FrameSyncMvpAValidator`
- 运行参数：`duration=300s, updateHz=240`
- 结果：`通过`

## 核心指标

- Tick 调用间隔 p95：`37.497ms`（阈值 `< 40ms`）
- 仿真时间漂移：`0.040042s`（阈值 `< 1s`）
- 帧号连续性：`连续`
- 总帧数：`8999`
- Tick 回调异常数：`0`

## 子项验证

- 确定性静态约束扫描：`通过`
- FrameTimerService 行为：`通过`
- CommandPool< T > 行为：`通过`

## 结论

- 本次自动化验收覆盖项全部通过。
- 客户端 Unity 场景挂载运行属于手工联调项，需在 Unity Editor PlayMode 进行最终确认。
