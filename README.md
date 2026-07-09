# WeDoBest Gameplay Sync Demo

当前分支：`feature/pinball-tech-demo`

> 分支名保留历史命名；当前方向已从“弹珠技术演示”收敛为“求职用 Gameplay 状态同步技术演示”。

本项目当前定位是一个极简俯视角 Gameplay 状态同步沙盒。它不追求完整商业玩法，也不优先实现复杂技能系统，而是用一个小球角色和小房间场景，集中展示弱网条件下的按帧输入、客户端预测、服务端权威校验、不一致诊断与回滚重播能力。

一句话目标：

> 一个极简俯视角 Gameplay 状态同步沙盒，用于演示弱网条件下的按帧输入、客户端预测、服务端权威校验、不一致诊断与回滚重播。

## 当前技术底座

- Unity + TEngine：HybridCLR 热更、YooAsset 资源、UI 和模块系统。
- Fantasy 服务端：认证、Gate、Battle Scene 和网络消息链路。
- 状态帧同步：30Hz 固定逻辑帧、服务端权威推进、客户端预测、快照校正、回滚重播、哈希一致性检查。
- Box2D / Box2DSharp：用于确定性碰撞、地图边界和简单交互体。
- 无头测试：服务端 `--mode=test` 场景可作为开发基线。

## Demo 方向

演示采用俯视角小球和极简房间，不靠复杂美术表达价值。核心是让面试官能看见并理解 Gameplay 状态在弱网下如何保持一致。

第一阶段玩法闭环：

1. WASD 控制小球移动。
2. Shift Dash，消耗体力并进入短暂 Recover 状态。
3. 体力恢复、Dash 冷却和 Recover 都按逻辑帧推进。
4. 进入目标区后开始占点，占点进度由服务端权威结算。
5. 弱网下客户端预测本地移动和 Dash，收到权威快照后校验、diff、rollback、replay。

展示重点：

- `FrameIndex` 是唯一逻辑帧语义，输入和状态变化都绑定到具体帧。
- 客户端可预测移动、Dash、体力消耗和本地状态机。
- 服务端权威确认占点、位置、体力和状态机结果。
- mismatch 日志必须打印真实差异域，而不只打印位置。
- 调试 UI 展示帧号、RTT、预测领先量、回滚次数、状态 hash、最近不一致字段。

## 非目标

- 暂时不做完整技能系统。
- 暂时不做 Buff、伤害、目标选择或复杂战斗结算。
- 暂时不做复杂角色动画、连招和商业化 UI。
- 不把 Unity 物理作为核心权威判定黑盒；关键 Gameplay 状态必须可快照、可哈希、可回滚。
- 不把 demo 做成完整游戏，优先保证同步链路可解释、可验证、可展示。

## 实施 Timeline

详细实施计划见：

- `Plan/Gameplay状态同步演示/Gameplay状态同步演示-Timeline.md`

摘要：

| 阶段 | 目标 | 核心产出 |
|------|------|---------|
| P0 | 方向收敛与文档基线 | README、timeline、旧弹珠方向降级为历史 |
| P1 | 小球移动最小闭环 | 俯视房间、小球移动、服务端权威快照 |
| P2 | Dash / 体力 / Recover | 可预测行为状态进入快照、哈希和回滚 |
| P3 | 占点交互 | 服务端权威交互进度与状态同步 |
| P4 | 弱网与回滚可视化 | RTT、lead、rollback、mismatch diff、ghost 显示 |
| P5 | 求职展示打磨 | README 讲解、录屏脚本、自动化验收命令 |

## 基线测试

```powershell
dotnet run --project GameServer\Server\Main\Main.csproj --framework net8.0 -- --mode=test --scenario=all
```

## 致谢

本项目基于 [TEngine](https://github.com/Alex-Rachel/TEngine) 开发，感谢原作者的贡献。
