# T1：SnapshotManager 死代码清理

> **状态**：已转化（2026-08-06）。经讨论定 S6 决策七选 A：摘掉 `SnapshotManager` 注册 + 迁移 `RunSelfTest` 到启动期，清理随之不可达的孤儿方法。本项并入 S6 Step 5 执行，不再独立追踪。下文为立项时的背景与选项分析（供参考）。

## 一句话

`SnapshotManager` 是 v0.3a 的产物，v0.3-arch 把回滚搬进 `BattleSimulation` 后，它的 `RollBack`/`CheckConsistency`/`LatestHash`/`TryGetSnapshot` **全部失去消费方**，退化成「被注册但无人看的死 tickable」，每帧还在 `Tick` 里分配产出没人读的 `LatestHash`。可清理，但 S6 不越界，单存此处。

## 代码核实（2026-08-06，已查实，无需复查）

来源：scout 全仓核查 + 人工 grep。结论已写进 S6 计划决策二（`:84-85` 定性更正）。

| 成员 | 位置 | 实际状态 |
|------|------|---------|
| `RollBack` | `SnapshotManager.cs:51-58` | 有真实实现（TryGet→RestoreSnapshot→重算 hash），**全仓零调用方**。grep `.RollBack(` 仅 2 处命中，全部指向 `BattleSimulation.RollBack`（`BattleClientController.cs:239` + 测试 `:659`） |
| `CheckConsistency` | `SnapshotManager.cs:60-62` | **空方法体**，零调用方 |
| `Tick` | `SnapshotManager.cs:32` | **每帧执行**：`_worldState.TakeSnapshot()`（分配 `PlayerStateSnapshot[]` + 物理快照）+ `_buffer.Save` + `StateHasher.Hash`，产出 `LatestHash` |
| `LatestHash` / `TryGetSnapshot` | 同文件 | **全仓零消费方** |
| 注册 | `ClientTickDriver.cs`（构造 + `Register`） | 作为 `ITickable` 注册，每帧跑 `Tick` |

**关键事实**：实际回滚链路是 `BattleSimulation.RollBack:314` → `ReconcileAuthoritativeSnapshot` → `BattleWorldState.RestoreSnapshot`，**根本不经过 SnapshotManager**。两条 `RollBack` 同名但分属两条路径，SnapshotManager 这条从未被触发。

**删除的技术成本**：`ITickable.RollBack` / `CheckConsistency` 是 **default interface method**（`ITickable.cs:11-17` 带默认空体），删除 `SnapshotManager` 的覆盖不违约、编译不报错，会自动回落到接口空默认。**零技术成本。**

## 为什么是孤儿（不是活跃预留）

旧表述（含 S6 早期草稿）曾说它是「v0.3a 为 v0.3c 预留的钩子」。**这个说法已作废**：

- v0.3a 确实预留了此钩子等 v0.3c 接
- 但 v0.3-arch（逻辑/渲染分离重构）把回滚搬进了 `BattleSimulation.RollBack`
- v0.3c 完成时走的就是 `BattleSimulation` 那条路，**此钩子从未被认领**
- 它比 S 线的诞生还早就已经是孤儿

详见 S6 决策二 `:84-85` 的「定性更正」段。

## 为什么暂不处理

1. **不在 S6 范围内**。S6 干「拆回滚粒度（别人不参与回滚）」，清理死基础设施是另一件事。混进来会让 diff 变杂、bisect 变难。
2. **优先级让位于主线**。除每帧分配开销外无运行副作用（不报错、不崩溃、不影响正确性）。
3. **v0.x 线已于 2026-08-06 冻结**（见 `Plan/状态帧同步/状态帧同步-阶段总目录.md:3` 顶部声明），故**无「踩到活跃 v0.x 功能」的顾虑**——清理是安全的，剩下的只是优先级与时机。

## 待决策（动手前必须定）

### 决策 1：SnapshotManager 整体去留（S6 决策七已给二选一，未拍板）

详见 `S6-回滚粒度改造-别人不参与回滚-实现计划.md` 决策七（约 `:126-135`）：

- **A（推荐）**：摘掉 tickable 注册——删 `ClientTickDriver` 里 `SnapshotManager` 的构造与 `Register`，把 `RunSelfTest`（`SnapshotManager.cs:64-76`）迁移到启动期一次性调用。**零行为变化，省每帧分配。**
- **B**：保留，但显式记为「已知遗留成本，另开号」追踪。

> S6 决策七原文：「默认不选『只加注释当没事』——那等于沉默放过，正是 `设计教训-两类静默失效.md` 的失效模式。」

### 决策 2：两个孤儿方法删不删

`RollBack` / `CheckConsistency` 既然是 default interface method 的覆盖、v0.x 已冻结、零调用方：
- **删**：最干净，零成本，消除「叫 RollBack 却不参与回滚」的误导源
- **留**：若 A 选了摘注册，SnapshotManager 整体可能直接删，此问题消解

### 决策 3：时机

- **单独开号**：一次改动只干一件事，最干净
- **搭主线改动的车**：如搭 S6 或某次重构，省一次提交——但违背「一次改动一件事」的范围纪律

## 参考

- **S6 决策七**（方案 A/B 出处）：`Plan/Gameplay状态同步演示/S6-回滚粒度改造-别人不参与回滚-实现计划.md` 约 `:126-135`
- **S6 决策二定性更正**（孤儿定性）：同文件 `:84-85`
- **代码**：`UnityProject/Assets/GameScripts/HotFix/GameLogic/FrameSync/SnapshotManager.cs`、`GameShared/FrameSync/Core/ITickable.cs`
- **v0.x 冻结声明**：`Plan/状态帧同步/状态帧同步-阶段总目录.md:3`
- **核实过程**：2026-08-06 scout 全仓核查（`history://VerifyV0xKernelStages`）+ 人工 grep
