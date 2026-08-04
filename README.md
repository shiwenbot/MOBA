# WeDoBest Gameplay Sync Demo

当前分支：`feature/gameplay-sync-sandbox`

> 方向已从“弹珠技术演示”收敛为“求职用 Gameplay 状态同步技术演示”，分支也已由 `feature/pinball-tech-demo` 重命名为当前名称。

本项目当前定位是一个极简俯视角 Gameplay 状态同步沙盒。它不追求完整商业玩法，也不优先实现复杂技能系统，而是用一个小球角色和小房间场景，集中展示弱网条件下的按帧输入、客户端预测、服务端权威校验、不一致诊断与回滚重播能力。

一句话目标：

> 一个极简俯视角 Gameplay 状态同步沙盒，用于演示弱网条件下的按帧输入、客户端预测、服务端权威校验、不一致诊断与回滚重播。

## 当前技术底座

- Unity + TEngine：HybridCLR 热更、YooAsset 资源、UI 和模块系统。
- Fantasy 服务端：认证、Gate、Battle Scene 和网络消息链路。
- 状态帧同步：30Hz 固定逻辑帧、服务端权威推进、客户端预测、快照校正、回滚重播、哈希一致性检查。
- 物理层：当前接有 Box2D / Box2DSharp，但**求解器实际空转**（所有 fixture `GroupIndex = -1`，全场不产生 Contact），房间边界与玩家分离都是手写的。已于 2026-08-03 决策**拿掉 Box2D，改为手写定点物理**，以换取逻辑层完全定点、跨端 bit-exact。
- 无头测试：服务端 `--mode=test` 场景可作为开发基线。

## Demo 方向

演示采用俯视角小球和极简房间，不靠复杂美术表达价值。核心是让面试官能看见并理解 Gameplay 状态在弱网下如何保持一致。

第一阶段玩法闭环：

1. WASD 控制小球移动。
2. Shift Dash，消耗体力并进入短暂 Recover 状态。
3. 体力恢复、Dash 冷却和 Recover 都按逻辑帧推进。
4. 两球相撞产生击退，由服务端权威裁决，**客户端故意不预测**，误差由表现层平滑吸收。
5. 弱网下客户端预测本地移动和 Dash，收到权威快照后校验、diff、rollback、replay。

展示重点：

- `FrameIndex` 是唯一逻辑帧语义，输入和状态变化都绑定到具体帧。
- 客户端可预测移动、Dash、体力消耗和本地状态机。
- 服务端权威确认击退、位置、体力和状态机结果。
- mismatch 日志必须打印真实差异域，而不只打印位置。
- 调试 UI 展示帧号、RTT、预测领先量、回滚次数、状态 hash、最近不一致字段。

## 非目标

- 暂时不做完整技能系统。
- 暂时不做 Buff、伤害、目标选择或复杂战斗结算。
- 暂时不做复杂角色动画、连招和商业化 UI。
- 不把 Unity 物理作为核心权威判定黑盒；关键 Gameplay 状态必须可快照、可哈希、可回滚。
- 不把 demo 做成完整游戏，优先保证同步链路可解释、可验证、可展示。

## 实施 Timeline

**文档总入口**：`Plan/Gameplay状态同步演示/00-总索引.md`（阅读顺序、S 编号总表、文档冲突记录）

执行顺序统一走 **S1~S10 单轴编号**，序号即顺序：

| S | 目标 | 成本 | 状态 |
|---|------|------|------|
| **S1** | 预测误差平滑 + Ghost 可视化 | 半天 | **下一项**，计划已就绪 |
| **S2** | 物理层重写（拿掉 Box2D，手写定点） | 1-2 天 | 计划已就绪 |
| S3 | 客户端哈希上报 + 服务端告警 | 半天 | 未开始 |
| S4 | 输入帧号 RTT 交叉校验 | 半天 | 未开始 |
| S5 | 一致时跳过重放 | 半天 | 未开始 |
| S6 | Dash / 体力 / Recover / 撞击击退 | 未估 | 未开始 |
| S7 | 弱网自动化测试 | 1-2 天 | 未开始 |
| S8 | 断线重连最小版 | 2-3 天 | 未开始 |
| S9 | 远端插值 + 轨迹线 + 完整调试面板 | 未估 | 未开始 |
| S10 | 求职展示打磨 | 未估 | 未开始 |

已完成：`P0` 方向收敛、`P1` 小球移动最小闭环、关掉 WarmStarting、带宽统计。已取消：占点交互（原 `P3`）。

阶段划分（`P0~P5`）见 `Plan/Gameplay状态同步演示/Gameplay状态同步演示-Timeline.md`，那是**分类维度，不是执行顺序**。

## 基线测试

三个 csproj 是单目标 `net9.0`，**必须用 Rider 那套 dotnet**（系统 dotnet 无 net9.0 SDK，会报 `NETSDK1045`）。跑之前先停掉正在运行的服务端，否则锁 dll 报 `MSB3027`。

```bash
"C:/Users/shiwe/.dotnet/dotnet.exe" run --project "GameServer/Server/Main/Main.csproj" -- --mode=test --scenario=all
```

只验证编译（不受服务端进程锁影响）：

```bash
"C:/Users/shiwe/.dotnet/dotnet.exe" build "GameServer/Server/Entity/Entity.csproj" -v q --nologo
```

## 致谢

本项目基于 [TEngine](https://github.com/Alex-Rachel/TEngine) 开发，感谢原作者的贡献。
