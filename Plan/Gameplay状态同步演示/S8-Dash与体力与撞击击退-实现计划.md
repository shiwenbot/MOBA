# S8：Dash / 体力 / Recover / 撞击击退 实现计划

> **状态**：已完成（2026-08-07）。实现、编译与无头验收均通过；按本次验收口径不执行 Unity 实机、双客户端或画面测试。执行编号 **S8**，见 [00-总索引.md](00-总索引.md)。对应 Timeline 的 **P2**。
> **前置**：S1（误差平滑）已完成（2026-08-04），S7 已完成（2026-08-07）；S8 在其 73 条预测用例基线上新增 33 条，总数为 106。
> **相关**：[设计教训-两类静默失效.md](设计教训-两类静默失效.md) —— 新增位移 effect 通道与 SkillGraph 快照通路，**动手前读第一节**。
> **行号约定**：实现期间相关文件持续演进，本文所引行号可能漂移；关键位置一律按方法名定位。

> **设计动机**：Dash/击退走 SkillGraph + Buff。参考项目 NKGMobaBasedOnET 并未真正解决技能图可回滚（靠服务器权威 Delta 兜底，行为树执行态不快照、回滚不回写）；WeDoBest 走全量快照路线，故 SkillGraph 执行态必须真正进快照。调研依据见文末**附录 A**。

## 本阶段做什么

把 demo 从「同步 Transform」升级为「同步 Gameplay 行为状态」，并给出一个明确标注「故意不预测」的对照项。四件事：

1. **Dash** —— Shift 触发，走 SkillGraph，消耗体力，起手帧锁定方向，若干帧内高速位移。**全程客户端预测。** 载体：技能图触发 + Buff 承载持续效果。
2. **体力** —— 按逻辑帧恢复，不足则无法 Dash。**全程客户端预测。** 载体：属性系统（与技能图/Buff 正交）。
3. **Recover** —— Dash 结束后进入硬直，期间禁止再次 Dash。**全程客户端预测。** 载体：一个禁用 Buff（技能图条件节点检查）。
4. **撞击击退** —— **Dash 撞人**触发，服务端权威裁决击退方向。**触发不预测，演化照常预测**（决策三）。载体：击退 Buff + 位移 effect。冲撞让 Dash（预测）与击退（不预测）两个对照在一次操作里衔接。

前三项演示「能预测就预测」，第四项演示「不该预测的别预测」。**本阶段是 S1~S11 里唯一的玩法增量**，也是 S11 录屏的主要素材来源。

## Context

以下事实均已在当前工作区逐条核对。**前两条推翻了既有文档的表述。**

### 玩家间碰撞一直在跑 —— S6 决策六的前提是错的

`FrameSyncPhysicsWorld.cs` 现含 `ResolvePlayerOccupancy`（圆-圆穿透分离，4 次迭代）、`RebuildOccupancyContacts`（每帧重建接触对）、`MinimumPlayerSeparation = 0.9`。它从 v0.4a 就在，S2 重写时被保留。

真实后果是 S6 引入了一处不对称：**服务端** `BattleLogic` 持有全部 body，分离正常跑；**客户端** S6 后 `_worldState` 只剩自己，`_bodies.Count == 1`，该函数在 `_sortedBodyIdsBuffer.Count <= 1` 处直接早退。于是两球接触期间客户端预测「能走过去」、服务端推回来，每帧失配、每帧回滚。S8 的击退演示必然让两球接触，**这是本阶段必须处理的既有缺口**（决策五）。

### `ApplyBodyImpulse` 已存在，语义是当帧速度增量

`FrameSyncPhysicsWorld.ApplyBodyImpulse(bodyId, impulseX, impulseY)`（`FrameSyncPhysicsWorld.cs:83`）把 `_pendingImpulses` 与移动输入相加写进 `LinearVelocity`，末尾 `_pendingImpulses.Clear()`。它是「本帧额外速度」入口，**正好复用**为位移 effect 的物理出口，物理层保持无状态。持续状态全在 gameplay 层（`PlayerState` + 快照），每帧重新喂。

### SkillGraph 执行态可快照，但容器级未接通（前置 B）

`SkillGraphRunner` **已有完整快照读写**：
- `GetSnapshot()`（`SkillGraphRunner.cs:564`）→ `SkillExecutionSnapshot`（currentNodeId/status/executedSteps/frameIndex/blackboard/delayRemainingFrames）
- `Restore(SkillExecutionSnapshot)`（`:584`）→ 全部回写
- `SkillBlackboard.CaptureSnapshot()/RestoreSnapshot()` 已实现

但 `BattleSkillGraphRuntime`（`RuntimeSkillGraph.cs:455`）执行容器**只有 `Clear()`（:548），无 Capture/Restore**。客户端回滚时整体清空，技能执行态凭空丢失。**前置 B 把 runner 快照接到容器级 → PlayerState → 哈希/协议/回滚。** 这是 Dash 能挂 SkillGraph 的前提。

### Buff 系统只支持属性修饰，不支持位移（前置 A）

`BuffEffect` 是 `readonly struct`，仅 `AttributeKind/ValueType/Value`；`AttributeKind` 只有 `Health/MaxHealth/Mana/MaxMana/Attack`；`ApplyBuffEffects`（`BuffSystem.cs:294-302`）唯一出口 `target.Numeric.AddModifier(...)`。`ModifierValueType` 仅 `Flat/Percent`。

**属性修饰器是「恒定值持续生效」，击退/Dash 需要「每帧衰减的位移」—— 模型不匹配。** 不能把位移塞进 Numeric 管线（衰减难表达、污染 Numeric 快照）。**前置 A 新增 `DisplacementEffect` 通道**，Dash 与击退共用。

### 挂 Buff 的技能图节点 handler 已就绪

`SkillHandlers.cs` 的 `ApplyBuffNodeHandler`（:93）、`RemoveBuffNodeHandler`（:121）、`BuffConditionNodeHandler`（:145）已写好并在 `RegisterDefaults` 注册，通过 `context.BuffCommandSink.EnqueueApplyBuff` 挂 Buff。`BattleSkillGraphRuntime` 持有 `IBuffCommandSink`（`RuntimeSkillGraph.cs:462`）并注入 `SkillContext`（:600）。**Dash 技能图挂 Buff 无需补 handler，只需新增 Buff 配置数据。**

### 属性系统现状：5 项，体力需要新增

`AttributeKind`（`NumericModifier.cs`）只有 `Health/MaxHealth/Mana/MaxMana/Attack`。体力新增 `Stamina=6`、`MaxStamina=7` 两个 kind（决策二）。

### 一致性比较与哈希的覆盖范围不同，新字段要加两处

| 函数 | 覆盖 |
|---|---|
| `ArePlayerSnapshotsEquivalent` | 位置、5 个属性、`NextRuntimeBuffId`、Buff 列表、Numeric 修饰器。**不含 `PhysicsSnapshot`** |
| `StateHasher.Hash` | 上述玩家字段 **+ `PhysicsSnapshot`** 的 body 位置/速度/角速度/旋转/两个 bool + 接触对 |

任何新增字段必须同时进这两处。漏前者 → 该字段失配不触发回滚；漏后者 → S3 哈希对账放过该字段错误。

### 平滑阈值与击退距离的硬约束

`PredictionErrorSmoother`：`SmoothingDurationSeconds = 0.12f`、`MaxSmoothingDistance = 6.0f`，超限走硬跳。`MaxSmoothingDistance` 必须大于单次击退最大瞬时偏差（决策四）。

## 前置任务

两个前置必须先做，是 Dash/击退的关键路径。

### 前置 A：位移 effect 通道（Buff 系统扩展）

现有 Buff 只能做属性修饰。新增一个与属性修饰并列的 effect 通道，承载 Dash/击退的「每帧衰减位移」。设计（乙路径）：

- `BuffConfig` 新增 `DisplacementEffect?`（初始速度 `VelX/VelY`、衰减比 `DecayNumerator/Denominator`、持续帧 `DurationFrames`、`DisplacementKind` 区分 Dash/Knockback）。
- `BuffSystem.ApplyBuff` 加非 Numeric 分支：识别 `DisplacementEffect` 时，把初始速度写进 `PlayerState` 对应位移字段（`DashVelX/Y` 或 `KnockbackVelX/Y`）+ `RemainingFrames = DurationFrames`，**不走 Numeric**。
- `PlayerState` 加位移字段（Dash 位移 + 击退位移，各 `VelX/VelY/RemainingFrames`），进快照/哈希/diff/proto（既有 PlayerState 通路）。
- 新增 `DisplacementSystem`（纯静态，两端共用）：每帧读 PlayerState 位移字段 → 衰减 → `ApplyBodyImpulse` → `remaining--` → 到 0 清零。
- **位移字段生命周期绑定 Buff**（P1-3 修正）：位移字段必须记录所属 `RuntimeBuffId`。`RemoveBuffInternal`（`BuffSystem.cs:457`）当前只清 Numeric 修饰器、**不碰位移字段** —— 漏了则位移 Buff 到期/撞停移除后 `DashVel/RemainingFrames` 残留，球停不下来。必须在 `RemoveBuffInternal` 里加：若被移除的 Buff 带 DisplacementEffect，清零对应位移字段（过期/手动移除/互斥替换统一走它）。

**为什么不扩展 BuffEffect 做属性修饰（甲路径）**：属性修饰器是恒定值，击退的 `v *= 0.75` 衰减要么每帧增删修饰器（破坏确定性、污染 Numeric 快照），要么改 Numeric 重算。绕且险。位移运行时状态放 `PlayerState` 字段、配置放 `DisplacementEffect`，最直。

**Dash 与击退共用同一套**，不各自为政。

### 前置 B：SkillGraph 可回滚通路（容器级快照胶水）

runner 级 `GetSnapshot/Restore` 现成，缺容器级收集与接线：

- `BattleSkillGraphRuntime` 加 `CaptureExecutions()` → `Dictionary<long, ActiveSkillExecutionSnapshot>`（按 casterId），其中 `ActiveSkillExecutionSnapshot { CasterId, TargetId, SkillId, RunnerSnapshot: SkillExecutionSnapshot }`。**`SkillExecutionSnapshot`（`SkillGraphRunner.cs:309`）本身不含 SkillId/TargetId**，而 `Restore`（`:584`）要求 Runner 已 `Initialize`（`:382`，需 graph+context），故身份字段必须单独存。
- `RestoreExecutions(dict)`：按 SkillId 查 `_graphsBySkillId` 得 graph → 用 CasterId/TargetId/SkillId 建 context → `new Runner` + `Initialize(graph, context)` → `Restore(runnerSnapshot)`。
- 该字典挂进 `PlayerState`，进 `ArePlayerSnapshotsEquivalent` / `StateHasher.Hash` / proto / 回滚恢复。
- `SkillExecutionSnapshot` 的 `blackboard`、`delayRemainingFrames` 是 `Dictionary`，**进哈希/proto 必须按键排序**，否则两端哈希不一致。
- 补用例 `skillgraph-restore-resumes-correct-graph`：不同 SkillId/TargetId 的执行恢复后继续跑正确的图。

## 目标

1. **Dash / 体力 / Recover 全程客户端预测** —— 弱网下不等服务端确认，三者进快照、哈希、diff、回滚重播。
2. **SkillGraph 执行态真正进快照可回滚**（前置 B）—— Dash 技能图回滚后逐位恢复。
3. **撞击击退服务端权威** —— 触发不预测，误差走 S1 平滑层，不出现硬跳。
4. **修掉接触期持续 mismatch**（决策五）—— 回滚次数与碰撞次数同量级。
5. **mismatch 日志能区分字段** —— `PositionMismatch` / `StaminaMismatch` / `StateMismatch`。
6. **画面产出** —— Dash 拖影、体力条、击退时 ghost 与实体分离（S11 主素材）。

## 非目标

- 不做技能表现资源、不做伤害/命中/目标选择、不新增除 Dash/击退外的 Buff 配置。
- 不做击退的**触发**预测（刻意选择，非缺陷）。
- 不做远端插值曲线（S10）。
- 不动 S3 哈希口径、不动 S7 的 RTT 通路。
- 不做体力 UI 的布局打磨（S10/S11）。

## 决策一：Dash 走 SkillGraph 触发 + Buff/位移 effect 承载

Dash 不再是 `PlayerState` 上的独立状态机，而是**一张技能图 + 两个 Buff**：

> **实现前置（P1-2 修正）**：当前技能图链路缺三条通道，**不是"只配数据不补代码"**：① `QueueSkillRequest`（`RuntimeSkillGraph.cs:485`）不带方向，需加 Fixed64 dx/dy（**不走 float 黑板**，否则丢定点精度）；② `ApplyBuffCommand`（`SkillHandlers.cs:107`）无法传 DisplacementEffect 的动态初始速度，需扩展或新增 handler；③ `ISkillRuntimeServices`（`SkillContext.cs:9`）没有扣体力/读属性接口，需新增原子化的"校验体力并扣除"服务。

- **触发**：Shift → 走既有技能输入通路（`SkillId = DashId`）→ `QueueSkillRequest` → SkillGraph 执行 Dash 技能图。
- **持续效果由 Buff 承载**：
  - Dash 位移 Buff（带 `DisplacementEffect`，初始速度=起手帧输入方向 × `DashSpeed`）→ 由 `DisplacementSystem` 每帧演化。
  - Recover Buff（禁用，`durationFrames = RecoverFrames`）→ 技能图条件节点 `BuffConditionNodeHandler` 检查它来阻止再次起手。
- **体力消耗**：起手时扣 `DashStaminaCost`（技能图节点或挂 Buff 时扣）。

### 方向在起手帧锁定，Dash 期间不跟随输入

**这是可预测性的要求。** Dash 位移 Buff 挂上时初始速度取起手帧输入方向并固化；之后 `DisplacementSystem` 只做衰减，不读输入。若 Dash 期间跟随输入，服务端丢包复用旧输入时方向与客户端不同，**每次上行丢包产生一次方向级 mismatch**。锁定后是「起手帧输入 + 固定帧数」的纯函数。

实现：起手方向经 `QueueSkillRequest` 的 Fixed64 dx/dy 传入（**不走 float 黑板**，避免丢定点精度），技能图执行时读出、归一化后作为 DisplacementEffect 的 VelX/VelY。零向量在归一化前替换为常量 `(1,0)`。

### 零输入时起手方向定为常量 `(1, 0)`

起手帧输入为零向量时**不拒绝 Dash**，方向取常量 `(1, 0)`。理由是**确定性**：零向量无定义良好的归一化结果，两端必须约定同一个值。**不是抗丢包措施**（`SkillId` 与方向同消息、同到达同丢失，服务端不会复用出 Dash）。

### Recover 用 Buff，起手判定拦截重入

Recover = 一个禁用 Buff（到期自动移除）。**不要在 `PlayerState` 上再加 Dash 相位枚举** —— 相位由「有没有这两个 Buff」隐式表达，状态来源单一。

**入口拦截（P1-5 修正）**：仅查「无 Recover Buff」不够 —— Dash 期间（位移 Buff 在、Recover 没挂）条件仍通过，而 `StartQueuedExecutions`（`RuntimeSkillGraph.cs:584`）收到同 caster 新请求会**直接覆盖** `_activeExecutionsByCasterId[casterId]`，导致重置位移、重复扣体力、原执行挂不上 Recover。入口必须**同时拒绝**：已有活跃 Dash 执行（容器层）**或**已有位移 Buff（Buff 层）。补用例 `dash-blocked-while-dashing`（不只测 Recover 阶段）。

### 为什么体力消耗不进 Buff

体力是属性系统的量（决策二），扣体力是「瞬时属性变更」，不是持续效果。起手时直接 `SetAttributeValue` 扣减即可，无需 Buff。Buff 只承载「持续效果」。

## 决策二：体力走新增 `AttributeKind.Stamina`，不复用 `Mana`

两种候选：复用 `Mana/MaxMana`（❌ 诊断输出会说谎，全是 `mp:`）vs 新增 `Stamina/MaxStamina`（✅）。新增白拿现有属性通路：脏同步、B1 baseline 校验、每 60 帧周期性全量恢复、Numeric 修饰器、自动进哈希。

### 必须是两个 `AttributeKind`

当前值与上限是两个独立 kind（`Mana/MaxMana` 即如此）。只加 `Stamina=6` 却要两个值自相矛盾：`MaxStamina` 没 kind 则无法独立重算、无法被修饰器命中，「Buff 提升体力上限」落空。**定为 `Stamina=6` 与 `MaxStamina=7` 两个 kind，脏标记拆两位。**

### 改动点清单（漏一处即静默失效）

| 位置 | 改动 |
|---|---|
| `AttributeKind` | 加 `Stamina = 6`、`MaxStamina = 7` |
| `PlayerAttributeDirtyFlags` | 加两位，**`All` 一并更新** |
| `PlayerAttributeSnapshot` | 加两字段 + 构造参数 + `Default`。**不加默认值参数**（让漏改处编译失败） |
| `PlayerAttributeSync` | `ComputeDirtyMask` / `Merge` 各加两项 |
| `PlayerState` | 加两属性 + `SetAttributeValue` 两条 switch |
| **`NumericState.GetBaseValue`** | 加两条分支（漏则 base 恒读 0） |
| **`NumericState.SetBaseValue`** | 加两条分支，每条重新构造完整 7 字段快照 |
| **`NumericState.Recalculate`** | 加两行，照 `Mana` 写法 |
| **`StateHasher.Hash`** | 玩家段 + `Numeric.BaseAttributes` 段各加两处 |
| **`AreAttributesEqual`** | 加两项比较 |
| `OuterMessage.proto` | `PlayerSnapshot` 末尾追加 25/26；**`NumericSnapshot` 末尾追加 `BaseStamina`/`BaseMaxStamina`** |
| `BattleSnapshotProtocolMapper` | `PlayerSnapshot` 与 **`NumericSnapshot`** 的 To/From 各加两项 |

### `NumericSnapshot` 的 base 必须协议化

只同步最终 `Stamina` 而不同步 `Numeric.BaseAttributes.Stamina` **必然失配**：`PlayerState.RestoreRuntimeState` 在 `Numeric.RestoreSnapshot` 之后立刻调 `Numeric.Recalculate`，而 `Recalculate` 走 `GetBaseValue` 取 base，base 为 0 则最终体力算成 0，**当场覆盖刚同步来的权威值**。表现为「体力永远 0 且回滚不停」。

### 恢复必须是整数节律，计数器进快照

`StaminaRegenCounterFrames` 每帧 +1，达 `StaminaRegenIntervalFrames` 时体力 +`StaminaRegenAmount` 并清零。**不用小数累加器**（累加器自身是逻辑状态，漏进快照则回滚后漂移）。该计数器是 `PlayerState` 一等字段，进快照/哈希/diff，不走属性脏同步。**Recover 期间照常恢复体力。**

## 决策三：击退触发不预测、演化照常预测，载体走 Buff/位移 effect

**与 Timeline 原表述有出入，以本决策为准。** Timeline P2 写「客户端不预测击退」，若按字面实现成「客户端对击退全无感知」，击退持续 8 帧则每帧失配，画面连续 8 次抖动，`rollbackCount` 每次碰撞暴涨 8。

拆两段：

| 阶段 | 谁做 | 客户端预测? |
|---|---|---|
| **触发**（判定 Dash 撞人、算冲量方向与大小） | 服务端独占 | ❌ **不预测** |
| **演化**（击退速度逐帧衰减、位移积分、到期清零） | 两端同一份 `DisplacementSystem` | ✅ **照常预测** |

击退状态用 **Buff + `DisplacementEffect`** 承载：服务端触发时挂击退 Buff（带 `DisplacementEffect`），Buff 状态进既有快照通路（可回滚性白拿）。客户端收到含击退 Buff 的权威快照后，由 `DisplacementSystem` 按同一份衰减规则演化。**触发帧吃一次 mismatch（拿到击退状态），后续帧不再失配。**

### 触发条件：Dash 撞人，三种情况

击退不是任意碰撞都触发，**只有 Dash 状态的球撞人才触发**（Dash 是"冲撞"手段）。Dash 状态由"身上有没有位移 Buff"判定（决策一）。新接触对触发时，按双方 Dash 状态分三种：

| 碰撞情况 | 判定 | 效果 |
|---|---|---|
| **Dash 撞非 Dash** | 有位移 Buff 的是主动方 | 被动方被击退飞出；主动方**撞停**（移除位移 Buff）+ 进 Recover |
| **双方 Dash 同帧对撞** | 都是主动方 | **互相击退**（都弹开）；双方都撞停 + 都进 Recover |
| **双方都非 Dash** | — | 只占位分离，**不击退**（决策五的镜像 body 处理） |

双方对撞"互相击退"参照 OW 莱因哈特对冲锋的做法（对称结算，不判谁先）—— 同帧用对称规则，确定性简单，不需要打破平局。

**撞停**让 Dash 有两个结束路径：正常到期（6 帧跑完）或撞人提前结束，两者都进同一个 Recover Buff。

### 讲解口径

「击退裁决权在服务端，客户端不猜谁撞了谁；但拿到裁决结果后，客户端和服务端跑同一套衰减，所以只在裁决那一帧修正一次。」**不要说「客户端完全不预测击退」** —— 与代码不符。

### 触发边沿：新接触 **或** 接触期间起 Dash

两种边沿都要触发击退：
- **新接触**：上一帧没接触、本帧接触了。防止贴身期间每帧重灌速度、永不衰减。
- **接触期间起 Dash**（P1-4 修正）：两人已贴身，随后一方起 Dash —— 接触对不是新的，但 Dash 资格从 false→true。**只看新接触会漏掉这个场景**。

实现：触发键不只看 contactPair 新旧，还要追踪「该对是否已因当前 Dash 触发过」。维护「已触发集合」，键为 `(contactPair, dashEpoch)`，dashEpoch = 该方当前位移 Buff 的 RuntimeBuffId（每次新 Dash 是新 epoch）。新接触且任一方在 Dash → 触发并记录；接触持续期间某方 Dash false→true 且该 epoch 未触发 → 触发。该集合服务端私有，不进快照。补用例 `dash-starts-while-already-in-contact`。

### 触发实现

击退由碰撞触发，不经技能图节点。服务端在 `DetectNewContactsAndTriggerKnockback` 里：① 按上述两种边沿判定是否触发（新接触 / 接触期间起 Dash）→ ② 检查双方 Dash 状态（有没有位移 Buff）→ ③ 按决策三三种情况挂击退 Buff（被动方 / 互挂）→ ④ 主动方撞停（移除位移 Buff + 挂 Recover Buff）。**撞停移除走 `RemoveBuffInternal`，位移字段随前置 A 的生命周期绑定一并清零。**

`DisplacementEffect` 配置（初始速度=冲量方向×`KnockbackSpeed`、衰减 3/4、`DurationFrames=8`）在 `KnockbackTuning` 里。

## 决策四：击退参数与 S1 平滑阈值联立，且在构造期断言

`MaxSmoothingDistance = 6.0` 必须大于单次击退最大瞬时偏差。

| 参数 | 取值 |
|---|---|
| `KnockbackSpeed` | `12`（≈ `MoveSpeed × 2.4`） |
| `KnockbackFrames` | `8`（30Hz 下约 0.27s） |
| `DecayNumerator/Denominator` | `3/4`（整数比避免定点除法歧义） |

总位移等比级数：`12 × (1/30) × (1 + 0.75 + …)`，8 项和约 3.75，总位移约 **1.5**。最坏瞬时偏差保守估约 **2.8**（含客户端收到击退状态前累积）。**2.8 < 6.0，约 2 倍余量。**

`KnockbackTuning` 静态构造里断言 `估算最大偏差 < MaxSmoothingDistance`，越界抛异常不静默 clamp。**调参后必须重跑。** 理由：调猛后画面瞬移但无头用例照样全绿（哈希只管一致性），属设计教训第一类失效。

## 决策五：客户端把别人镜像成只读碰撞 body

修 Context 第一条的既有缺口。做法：每次应用权威快照后，把 `RemotePlayerBuffer` 里每个玩家最新权威位置写进物理世界对应 body（`SetBodyTransform(resetVelocity: true`）。

三条边界：
1. **只读、纯障碍物** —— 不进 `_worldState`、不进 `_selfPredictions`、不参与 `AdvancePredictionTo`、不参与回滚。
2. **位置恒取最新权威值，不插值不推进** —— 插值归 S10。
3. **随 buffer 移除而清理 body** —— `RemotePlayerBuffer` 移除条目处同步 `RemoveBody`，否则留隐形墙。

### 「只读」必须在物理层显式建模，`resetVelocity` 不够

`SetBodyTransform(resetVelocity: true)` 只清速度，不让 body 免于被推。`CalculateSeparationShares` 在双方都无朝向意图时各分一半穿透量。镜像 body 的 `requestedVelocity` 恒为零 → 自己静止而权威更新让两者重叠时，**镜像被推走**，在重放 `_leadFrames` 帧期间持续漂移。

最小改动：`FixedPhysicsBody` 加 `IsKinematicObstacle` 标志，`CalculateSeparationShares` 里若一方带该标志则全部穿透量由另一方承担（等价无限质量）。`FrameSyncPhysicsWorld` 加 `SetBodyKinematicObstacle(bodyId, value)`。**该标志不进快照** —— 服务端从不设它，纯客户端本地概念，不影响哈希基线。**这是本阶段唯一必要的物理 API 新增**（为决策五，非为击退）。

### 残差有界但不为零

客户端用延迟约 RTT/2 的对手位置，分离结果与服务端不同。残差从「完整穿透深度」缩到「对手在 RTT/2 内的移动距离」，量级降一档不归零。**写进验收指南，不判 FAIL。** 彻底消除需预测别人，与 S6 方向相反。

### 与 S6 决策六的关系

S6 决策六「客户端零碰撞代码」建立在「玩家间无碰撞」错误前提上，本阶段修正为镜像方案。**但 S6 核心原则保留**：别人不参与预测/回滚/`_worldState`。改的只是「别人在客户端物理世界里有没有只读碰撞占位」。**需回写 S6 计划决策六勘误。**

## 决策六：击退判定进 `BattleLogic`，哈希基线**必须**变

S3~S7 一律要求「`BattleLogic` 零改动、哈希基线不变」。**本阶段相反。** 击退判定是 gameplay 规则，属确定性内核（纯几何判定，完全确定性），与 S7 拒绝的墙钟时间性质不同。

代价是两个哈希基线合法变化：`fixed-physics-bit-exact` 与 Determinism scenario。新增快照字段必然改哈希，**预期结果，非 bug**。

### 验收判据是「变一次后稳定」

实现完成跑一次记录新基线 → **再跑一次确认逐位一致** → 更新用例常量与验收指南。**只跑一次就更新是错的**（固化偶发结果）。

### 必须同步改三份既有验收指南

| 指南 | 现状 | 需改成 |
|---|---|---|
| `哈希上报与服务端告警-验收测试指南-20260804.md` | **「哈希基线必须不变」** | 注明 S8 是唯一例外 |
| `回滚粒度改造-验收测试指南-20260806.md` | `fixed-physics-bit-exact` 必须不变 | 同上 |
| `弱网自动化测试-验收测试指南-20260805.md` | 用例数与基线 | 更新数字 |

漏改的后果是验收 agent 照指南判 FAIL 而实现是对的。

## 决策七：Dash 复用既有 `SkillId` 输入通路

Dash 走 SkillGraph，就是技能，故复用 `SkillId`。`C2B_PlayerInput.SkillId` 已存在并已路由进 `QueueSkillRequests` → SkillGraph，**输入通路无需新增字段**。两点前提：
- 既有技能输入已进 `BufferedInput` / `_inputHistory` / `PendingInput`，Dash 复用即自动满足「重放时 Dash 输入不消失」，无需给这些结构加字段。
- 前置 B 让 SkillGraph 执行态进快照，输入与执行态单一语义，不挤在两个字段里。

### 仍须确认：`SkillId` 已在 `_inputHistory`

但动手前需核实 `QueueSkillRequests` 在客户端重放路径上同样被调用（否则 Dash 输入在重放时丢失）。

### Dash 走既有 `InputBuffer`，不在 `Tick` 里直接读

`BattleClientController` 已有技能通路：`Update` 里 `GetKeyDown` → `_inputBuffer.Record(BufferedInputKind.Skill, skillId, SkillInputBufferFrames=2)`，`Tick` 里 `TryConsume`。**Dash 照抄，`BufferedInputKind.Skill` 传 `DashId` 即可，不另起一套。** 两个它防的 bug 仍适用：

| 错误做法 | 症状 |
|---|---|
| `Tick` 里直接 `GetKeyDown` | 渲染帧率高于 30Hz 时按下发生在两次 Tick 之间**丢按键** |
| 用 `GetKey`（长按） | Recover 结束瞬间**自动连发**，体力被抽干 |

自动化输入走既有 `TryGetSkillRequest`，传 `DashId`。

## 架构

```
新增：
  GameShared/FrameSync/Battle/DisplacementEffect.cs      ← 位移 effect 配置（前置 A）
  GameShared/FrameSync/Battle/DisplacementSystem.cs      ← 纯静态：逐帧衰减 + ApplyBodyImpulse（Dash/击退共用）
  GameShared/FrameSync/Battle/StaminaSystem.cs           ← 纯静态：整数节律恢复
  GameShared/FrameSync/Battle/KnockbackTuning.cs         ← 击退常量 + 与平滑阈值联立断言（决策四）
  GameShared/FrameSync/Battle/DashTuning.cs              ← Dash/体力常量
  GameShared/FrameSync/Battle/SeparationNormalResolver.cs← 法线解析纯函数（Step 4 抽取）
  GameShared/SkillGraph/SkillExecutionSnapshotCodec.cs   ← 容器级快照收集/恢复（前置 B）

服务端 BattleLogic.Tick 内顺序：
  SkillGraphRuntime.Step(frame)                  ← 含 Dash 技能图执行、挂/移 Buff
  DisplacementSystem.ApplyAll(states, physics)   ← 当前位移速度 → ApplyBodyImpulse
  DisplacementSystem.DecayAll(states)            ← 紧跟 Apply（见下）
  physics.Step(dt)                               ← 内含占位分离
  SyncPlayerStatesFromPhysics()
  ── 以下服务端独占 ──
  DetectNewContactsAndTriggerKnockback()         ← 新接触→判Dash状态→挂击退Buff(被动方/互挂)+主动方撞停(移位移Buff+挂Recover)
  StaminaSystem.Tick(state)

客户端 ApplyLocalPrediction 内：同上，但跳过 DetectNewContacts，应用权威快照后镜像远端 body
```

### 衰减必须紧跟 Apply，不能放在触发之后

若 `DecayAll` 排在 `DetectNewContacts` 之后，本帧刚写入的 `KnockbackSpeed=12` 立刻被衰减成 9，**12 从未参与积分**，决策四验算失效。`Decay` 紧跟 `Apply`（都在 `physics.Step` 之前），新写入值天然不被本帧衰减。时序：

| 帧 | Apply 速度 | 帧末 remaining |
|---|---|---|
| N（碰撞） | —（触发在帧末） | 8 |
| N+1 | **12**（完整） | 7 |
| N+8 | 12 × 0.75⁷ | 0 |

共 8 帧积分，与 `KnockbackFrames=8` 对齐。**用例必须逐帧断言速度、remaining、位移增量。**

所有 System 纯静态、零引擎依赖，放 `GameShared` 两端共用 —— 各写一份必然漂移。

## 实现步骤

### Step 0a：前置 A —— 位移 effect 通道

新增 `DisplacementEffect`、改 `BuffConfig`/`BuffSystem.ApplyBuff` 加非 Numeric 分支、`PlayerState` 加位移字段（Dash 位移 + 击退位移，**字段记录所属 RuntimeBuffId**）+ 快照/哈希/diff/proto。**改 `RemoveBuffInternal`（`BuffSystem.cs:457`）：移除带 DisplacementEffect 的 Buff 时清零对应位移字段**（P1-3）。单独跑全量用例确认「只有基线变、行为不变」。

### Step 0b：前置 B —— SkillGraph 可回滚通路

`BattleSkillGraphRuntime` 加 `CaptureExecutions`/`RestoreExecutions`（**value 是 `ActiveSkillExecutionSnapshot{CasterId,TargetId,SkillId,RunnerSnapshot}`，不只 SkillExecutionSnapshot** —— P1-1），接 `PlayerState` 快照/`StateHasher`/`ArePlayerSnapshotsEquivalent`/proto（Dictionary 按键排序）。加用例 `skillgraph-execution-replays-after-rollback` + `skillgraph-restore-resumes-correct-graph`。

### Step 1：体力接入属性系统

按决策二清单改 12 处。`MaxStamina` 默认 100、初值满。**单独跑一次全量用例。**

### Step 2：Dash 技能图 + Buff 配置

新建 Dash 技能图数据 + **补三条代码通道（P1-2）**：`QueueSkillRequest` 加 Fixed64 dx/dy、`ApplyBuffCommand`/新 handler 携带动态初始速度、新增"校验体力并扣除"服务。`DashTuning`：`DashFrames=6`、`RecoverFrames=20`、`DashSpeed=18`、`DashStaminaCost=30`。技能图入口**同时检查**：无 Recover Buff、无活跃 Dash 执行、无位移 Buff、体力足够（P1-5）。

位移交汇点在 `DisplacementSystem`，**不在 `MoveSystem` 加 Dash 分支**。

### Step 3：Dash 输入通路

复用既有技能通路：`Update` 里 `GetKeyDown(LeftShift/RightShift)` → `Record(BufferedInputKind.Skill, DashId, 2)`，`Tick` 里 `TryConsume`。自动化源 `TryGetSkillRequest` 传 `DashId`。服务端两处接线点（`SubmitInput` 转发、`Tick` 驱动）按方法名定位。

### Step 4：击退

`KnockbackTuning`（含决策四断言）。服务端加「已触发集合」键为 `(contactPair, dashEpoch)`，追踪**两种边沿**：新接触 / 接触期间起 Dash（P1-4）。触发前判双方 Dash 状态，按决策三三种情况处理，主动方撞停（移位移 Buff + 挂 Recover，**走 RemoveBuffInternal 清位移字段**）。冲量方向取两球中心连线；**完全重合时必须与占位分离用同一退化规则**。

`DetermineSeparationNormal` 现为 `FrameSyncPhysicsWorld` 的 `private static`，`BattleLogic` 调不到。**本步含结构改造**：抽成 `SeparationNormalResolver` 纯静态，占位分离与击退触发共用。抽取注意其第二级退化依赖 `requestedVelocities`，而击退触发在 `physics.Step` 之后 `_pendingLinearVelocities`/`_pendingImpulses` 已清空，拿不到 —— 纯函数签名要允许「无速度信息」调用，直接落第三级退化（bodyId 排序）。**两调用方同输入同结果，用例专守。**

### Step 5：远端 body 镜像（决策五）

`ApplyAuthoritativeSnapshot` 里 `_remotePlayers.ApplyAuthoritative(...)` 之后、对每个远端 `SetBodyTransform(resetVelocity: true)` + `SetBodyKinematicObstacle(true)`。**顺序陷阱**：`RestoreSelfOnly` 内部 `ClearBodies()` 再重建自己，镜像写入**必须在 `RestoreSelfOnly` 之后**，否则被清掉（症状与「没实现」一模一样）。buffer 移除处同步 `RemoveBody`。

### Step 6：表现层

Dash 拖影/变色；体力条；击退 ghost 分离（S11 主素材）；HUD 加 Dash 相位、体力、击退剩余帧。复用 S1 ghost 与既有 HUD，**不做布局打磨**。

### Step 7：mismatch 日志分字段

`CheckConsistency` 追加体力、Dash、击退标签，在**首个不一致字段**处打标签。判定顺序：**先 gameplay 语义（体力 → Dash → 击退），后位置与属性** —— 位置失配往往是前三者后果。

### Step 8：无头用例

注册两处（`AllCaseNames` + `RunCase` switch），**只追加不改既有**。

前置与 SkillGraph（5 条）：

| 用例 | 断言 |
|---|---|
| `displacement-effect-applies-and-decays` | 挂位移 Buff 后 PlayerState 位移字段=初始速度；逐帧衰减、到期清零 |
| **`displacement-cleared-on-buff-expiry`** | **守前置 A 生命周期（P1-3）**。位移 Buff 到期/手动移除后位移字段立即清零；下一帧冲量为零（不只 Buff 不存在） |
| **`skillgraph-execution-replays-after-rollback`** | **守前置 B**。含 Dash 执行的帧回滚重放后 runner 状态逐位恢复 |
| **`skillgraph-restore-resumes-correct-graph`** | **守前置 B 身份字段（P1-1）**。不同 SkillId/TargetId 的执行恢复后继续跑正确的图，不串图 |
| `dash-buff-latches-direction-at-start` | 起手后改输入，位移仍按起手方向 |

Dash 与体力（7 条）：

| 用例 | 断言 |
|---|---|
| `dash-consumes-stamina-and-enters-recover` | 起手扣体力、挂位移 Buff、随后挂 Recover Buff |
| `dash-blocked-during-recover` | Recover Buff 在时拒绝起手，体力不扣 |
| **`dash-blocked-while-dashing`** | **守 P1-5**。Dash 期间再次按 Shift：不起手、不扣体力、不覆盖当前执行、位移不重置 |
| `dash-blocked-when-stamina-insufficient` | 体力不足不起手 |
| `dash-zero-input-uses-default-direction` | 零输入起手取常量 `(1,0)` |
| **`dash-input-survives-replay`** | 含 Dash 的 SkillId 输入回滚重放后结果与首次预测逐位相同 |
| **`stamina-regen-counter-restores-on-rollback`** | 回滚后 `StaminaRegenCounterFrames` 恢复权威值 |

Dash 输入通路（2 条）：

| 用例 | 断言 |
|---|---|
| `dash-input-buffered-across-render-frames` | 渲染帧按下、当帧无逻辑 Tick 时下一 Tick 仍消费到 |
| `dash-hold-does-not-autofire` | 持续按住跨越整个 Dash+Recover 周期，只起手一次 |

击退（11 条）：

| 用例 | 断言 |
|---|---|
| `knockback-triggers-only-on-new-contact` | 接触持续 5 帧只触发一次；分离后再接触再触发一次 |
| **`dash-starts-while-already-in-contact`** | **守 P1-4**。两人已贴身、一方起 Dash：击退触发（不只看新接触）；同 Dash epoch 不重复触发 |
| **`dash-collision-knockbacks-passive-player`** | **守决策三**。Dash 撞非 Dash：被动方击退飞出、主动方撞停 + 进 Recover |
| **`dual-dash-collision-knockbacks-both`** | **守决策三**。双方 Dash 对撞：互相击退、双方都撞停 + 都进 Recover |
| `non-dash-collision-no-knockback` | 双方都非 Dash 碰撞只占位分离，不挂击退 Buff |
| **`knockback-decay-timeline-is-frame-exact`** | 逐帧断言：N+1 速度完整 `KnockbackSpeed`、N+2 ×0.75、…、N+8 归零，共 8 帧 |
| `knockback-total-displacement-matches-series` | 总位移与级数和（3.75 项）一致 |
| `separation-normal-resolver-agrees-across-callers` | 同输入下占位分离与击退触发同法线；重合都落 bodyId 排序退化 |
| **`knockback-state-replays-without-new-mismatch`** | **守决策三核心**。应用含击退 Buff 的权威快照后重放不产生新 mismatch |
| `knockback-worst-case-deviation-under-smoothing-threshold` | 最坏偏差 < `MaxSmoothingDistance` |
| `client-prediction-never-self-triggers-knockback` | 客户端预测路径不含触发逻辑，孤立跑不产生击退 |

远端 body 镜像（4 条）：

| 用例 | 断言 |
|---|---|
| `remote-body-blocks-self-movement` | 自己撞不进远端占位；远端不在 `_worldState`、`PlayerCount==1` |
| **`remote-body-not-displaced-by-occupancy`** | **守 kinematic 建模**。自己静止且重叠时连跑多帧镜像逐位不变。**不加 `IsKinematicObstacle` 时必挂** |
| **`remote-body-mirror-survives-restore-self-only`** | **守 Step 5 顺序陷阱**。`RestoreSelfOnly` 后镜像仍在 |
| `remote-body-removed-with-buffer-entry` | 玩家消失后 body 一并移除 |

字段同步（4 条）：

| 用例 | 断言 |
|---|---|
| `dash-displacement-state-proto-roundtrip` | Dash 位移字段往返位精确 |
| `knockback-state-proto-roundtrip` | 击退位移字段往返位精确 |
| **`numeric-stamina-base-proto-roundtrip`** | **守决策二**。两个体力 base 往返位精确；base 不同步时 `Recalculate` 把体力清零 |
| `stamina-mismatch-triggers-rollback` | 守 `AreAttributesEqual` |

合计 **33 条**，总数须在 S7 落地后重数再加。

### Step 9：report 与场景

`BattleAutomationClientSnapshot` 加 `dashCount`、`staminaAtEnd`、`knockbackTriggerCount`、`contactMismatchFrames`。新场景 `two-client-knockback`，**登记三处**（`TestRunner.Execute` switch、`NormalizeScenario`、`TestScenario` 常量表）。**必须有一条断言「击退确实发生了」（`knockbackTriggerCount > 0`）**，否则碰撞判定失效时所有判据假通过。

## 验证方式

> **本次验收范围（2026-08-07）**：以代码编译、无头用例、哈希双跑和确定性扫描为完成门禁；Unity Play Mode、真实双客户端 report 与画面观测不执行，也不阻塞本次交付。

- **无头**：`--mode=test --scenario=all` 全通过，退出码 `0`
- **基线稳定性**：两个哈希基线变化**一次**后连续两跑逐位一致（决策六）
- **确定性扫描**：`--mode=validate --duration-seconds 30 --update-hz 60`，Issues 零
- **弱网 Dash**：`two-client-weaknet-delay` 下起手不等确认，体力与位移最终收敛
- **弱网击退**：`two-client-knockback` 固定场景定量判据（下方）
- **接触期对比**：`contactMismatchFrames` before/after

### 定量判据绑定固定场景

场景 `two-client-knockback` 固定参数：**双向各 100ms 延迟、零丢包、固定种子、Dash 撞人 3 次（含 1 次双方 Dash 对撞）、每次接触约 4 帧**。

| 判据 | 阈值 |
|---|---|
| 每次服务端击退触发引起的 `StateMismatch` | **≤ 1** |
| 全程 `StateMismatch` 总数 | **≤ 3**（= 碰撞次数） |
| `contactMismatchFrames`（接触期 `PositionMismatch`，决策五残差） | **≤ 6** |
| 同场景相对决策五实施前的 `contactMismatchFrames` | **下降 ≥ 60%** |

**`StateMismatch` 与 `PositionMismatch` 必须分开统计** —— 前者是击退状态到达（决策三），后者是镜像滞后残差（决策五）。混计则互相掩盖。**换网络参数必须重新标定。**

## 完成标准

1. **Dash / 体力 / Recover 全程客户端预测**，弱网下不等确认；三者及 `StaminaRegenCounterFrames` 进快照/哈希/diff/回滚。
2. **SkillGraph 执行态进快照可回滚**（前置 B）—— `skillgraph-execution-replays-after-rollback` 通过。
3. **位移 effect 通道可用**（前置 A）—— `displacement-effect-applies-and-decays` 通过；Dash/击退共用。
4. **Dash 走 SkillGraph + Buff**，方向起手锁定、零输入取常量；Recover 是禁用 Buff、条件节点拦截。
5. **体力走两个 `AttributeKind`**，`AreAttributesEqual` 与 `StateHasher` 两处都已加；整数节律恢复，计数器进快照。
6. **击退裁决权在服务端**，客户端不自触发（`client-prediction-never-self-triggers-knockback`）。
7. **击退状态随快照下发并被客户端重放** —— `knockback-state-replays-without-new-mismatch` 通过；固定场景每次触发 `StateMismatch ≤ 1`、总数 `≤ 3`。**核心判据。**
8. **触发按新接触判定**（`knockback-triggers-only-on-new-contact`）。
9. **击退衰减时序逐帧正确** —— 首帧完整速度、8 帧积分、总位移与级数和一致。`Decay` 紧跟 `Apply`。
10. **参数与平滑阈值联立断言在构造期生效**，越界抛异常。调参后重跑。
11. **法线解析抽成两端共用纯函数**（`separation-normal-resolver-agrees-across-callers`）。
12. **客户端镜像别人为只读碰撞 body**，不进 `_worldState`/回滚；`RestoreSelfOnly` 后镜像仍在。
13. **镜像 body 显式建模为 kinematic**，自己静止重叠时镜像逐位不变。**仅 `resetVelocity` 不够。**
14. **接触期残差有界** —— `contactMismatchFrames ≤ 6` 且下降 ≥ 60%；`StateMismatch`/`PositionMismatch` 分开统计。
15. **Dash 按键走既有 `InputBuffer`** —— 渲染帧按下下一帧可消费、长按不连发；自动化走同一语义。
16. **`Numeric.BaseAttributes` 两个体力 base 已协议化**（`numeric-stamina-base-proto-roundtrip`）。
17. **mismatch 日志按类型区分**，判定顺序先 gameplay 语义后位置。
18. **`SkillId`(Dash) 进 `_inputHistory` 且重放复现**（`dash-input-survives-replay`）。
19. 所有 System 与 `SeparationNormalResolver` 零 `UnityEngine`/`Fantasy` 引用，两端共用。
20. **两个哈希基线按新字段更新且连续两跑稳定**；三份既有验收指南「基线必须不变」已改注。
21. **`knockbackTriggerCount > 0` 进判据**，防碰撞失效假通过。
22. **S6 决策六勘误已回写**。
23. 能说清**为什么击退触发不预测**（对手位置在本地是过去时，预测期望值为负）、**为什么演化照常预测**（否则每击退帧一次回滚），两者不矛盾。
24. 能说清**为什么 Dash 走 SkillGraph 但击退 Buff 不走**（Dash 由玩家输入触发、击退由碰撞触发），以及**为什么位移用 DisplacementEffect 而非属性修饰**（衰减量与恒定修饰模型不匹配）。
25. **位移字段随 Buff 移除统一清零**（P1-3）—— `displacement-cleared-on-buff-expiry` 通过；位移 Buff 到期/撞停后下一帧冲量为零，不只 Buff 不存在。
26. **SkillGraph 快照含身份字段，恢复后跑正确的图**（P1-1）—— `skillgraph-restore-resumes-correct-graph` 通过；不同 SkillId/TargetId 不串图。
27. **Dash 入口拒绝重入**（P1-5）—— `dash-blocked-while-dashing` 通过；Dash 期间再按 Shift 不起手、不扣体力、不覆盖执行。
28. **击退触发覆盖两种边沿**（P1-4）—— `dash-starts-while-already-in-contact` 通过；贴身起 Dash 也触发，不只新接触。

## 风险与对策

| 风险 | 对策 |
|---|---|
| **沿用 S6「玩家间无碰撞」旧结论** | 漏掉决策五，接触期持续 mismatch 且掩盖击退信号。`ResolvePlayerOccupancy` 从 v0.4a 就在 |
| **SkillGraph 容器级快照漏接** | Dash 技能图回滚丢失，每次回滚 Dash 状态错。`skillgraph-execution-replays-after-rollback` 专守（前置 B） |
| **`SkillExecutionSnapshot` 的 Dictionary 不排序** | `blackboard`/`delayRemainingFrames` 进哈希/proto 时不排序 → 两端哈希不一致。按键排序后处理（前置 B） |
| **位移状态漏进快照** | 回滚后位移凭空消失。`PlayerState` 位移字段进既有通路（前置 A） |
| **位移 effect 误走 Numeric 管线** | 衰减难表达、污染 Numeric 快照。`DisplacementEffect` 走独立分支，不进 `ApplyBuffEffects` 的 Numeric 路径（前置 A） |
| **位移 Buff 移除不清位移字段** | 撞停/到期后 DashVel 残留，球停不下来。位移字段记录 RuntimeBuffId，RemoveBuffInternal 统一清理（前置 A，P1-3）。`displacement-cleared-on-buff-expiry` 专守 |
| **SkillGraph 快照缺身份字段** | 只存 SkillExecutionSnapshot 无法重建 Runner（Restore 需先 Initialize）。存 ActiveSkillExecutionSnapshot{CasterId,TargetId,SkillId,RunnerSnapshot}（前置 B，P1-1）。`skillgraph-restore-resumes-correct-graph` 专守 |
| **方向/体力无传入通道** | QueueSkillRequest 不带方向、ApplyBuffCommand 不传动态速度、无扣体力接口。补 Fixed64 方向 + 动态速度 handler + 体力服务（决策一，P1-2）。**不走 float 黑板** |
| **贴身起 Dash 漏触发** | 只看新接触漏掉"已接触+起 Dash"。追踪 (contactPair, dashEpoch) 边沿（决策三，P1-4）。`dash-starts-while-already-in-contact` 专守 |
| **Dash 期间重入覆盖执行** | StartQueuedExecutions 直接覆盖同 caster 执行，重置位移重复扣体力。入口同时拒活跃执行/位移 Buff（决策一，P1-5）。`dash-blocked-while-dashing` 专守 |
| **体力恢复用小数累加器** | 累加器漏进快照则回滚后漂移，偶发体力 mismatch。整数节律 + 计数器进快照（决策二） |
| **漏 `AreAttributesEqual` 体力比较** | 体力失配不触发回滚，永久错位。`stamina-mismatch-triggers-rollback` 专守 |
| **漏 `StateHasher` 新字段** | S3 哈希对账放过该字段错误。两处（玩家段 + `Numeric.BaseAttributes` 段）都要加 |
| **漏 `NumericSnapshot` 两个体力 base** | `RestoreRuntimeState` 紧跟的 `Recalculate` 把体力算成 0。`numeric-stamina-base-proto-roundtrip` 专守 |
| **`SetBaseValue` 既有分支漏传新参数** | 该属性设值时把体力清零。**不给构造函数加默认值**，让漏改处编译失败 |
| **体力只加一个 `AttributeKind`** | `MaxStamina` 无 kind 无法独立重算/被修饰器命中。必须两个 kind |
| **击退客户端完全不感知** | 8 帧击退 = 8 次回滚，画面连续抖动。触发不预测、演化照常预测（决策三） |
| **触发按「正在接触」判定** | 贴身期间每帧重灌速度，永不衰减。维护上一帧接触对集合（决策三） |
| **重合时法线退化另写一套** | 与占位分离不一致产生极难复现偏差。`DetermineSeparationNormal` 是 `private static`，必须抽成 `SeparationNormalResolver`（Step 4） |
| **`Decay` 排在触发之后** | 首帧速度从未参与积分，决策四验算失效。`Decay` 紧跟 `Apply`；逐帧断言 |
| **击退用例只断言「最终归零」** | 放过整条时序错位。逐帧断言速度/remaining/位移增量 |
| **击退参数调猛走硬跳** | 画面瞬移但无头用例全绿。构造期断言 + 调参后重跑（决策四） |
| **以为 `resetVelocity: true` 等于只读** | 双方无意图时各分一半穿透，镜像漂移。必须 `IsKinematicObstacle`（决策五） |
| **镜像 body 写在 `RestoreSelfOnly` 之前** | 被 `ClearBodies()` 清掉，症状与「没实现」一样。写在之后；`remote-body-mirror-survives-restore-self-only` 专守 |
| **远端 body 不随玩家移除** | 隐形墙残留。buffer 移除处同步 `RemoveBody` |
| **Dash 在 `Tick` 里直接 `GetKeyDown`** | 高渲染帧率下丢按键。走既有 `InputBuffer`（决策七） |
| **Dash 用 `GetKey` 长按** | Recover 结束自动连发。`GetKeyDown` + `Record`/`TryConsume` |
| **「mismatch 与碰撞次数同量级」当判据** | 不可执行。绑定固定场景给死阈值，`StateMismatch`/`PositionMismatch` 分开统计 |
| **把零输入常量方向说成抗丢包** | 它是确定性要求（零向量归一化未定义），不是抗丢包（决策一） |
| **Dash 期间跟随输入转向** | 丢包时方向级 mismatch。起手锁定（决策一） |
| **位移 effect 配置塞进 `BuffEffect` 当属性修饰** | 衰减无法表达。走独立 `DisplacementEffect` 通道（前置 A） |
| **击退 Buff 经技能图节点触发** | 击退由碰撞触发，不经技能图。服务端直接挂（决策三） |
| **按「哈希基线必须不变」判 FAIL** | 本阶段是唯一要求基线变化的阶段。三份指南改注，S3 那份优先（决策六） |
| **基线只跑一次就更新** | 固化偶发结果。连续两跑确认（决策六） |
| **新场景名只登记一处** | `--scenario=all` 假 PASS。三处登记（Step 9） |
| **无「击退确实发生了」断言** | 碰撞失效时判据假通过（Step 9） |
| **proto 新字段插中间** | opcode/字段号按声明顺序，后续全改号。**末尾追加** |
| **服务端运行中报 `MSB3027`** | 先停服务端，或只 build `Entity.csproj` |

## 相关实现文件

| 文件 | 改动 |
|---|---|
| `GameShared/FrameSync/Battle/DisplacementEffect.cs` | **新建**（前置 A），位移 effect 配置 |
| `GameShared/FrameSync/Battle/DisplacementSystem.cs` | **新建**，纯静态：逐帧衰减 + ApplyBodyImpulse，Dash/击退共用 |
| `GameShared/FrameSync/Battle/StaminaSystem.cs` | 新建，纯静态：整数节律恢复 |
| `GameShared/FrameSync/Battle/KnockbackTuning.cs` | 新建，含与 `MaxSmoothingDistance` 联立断言（决策四） |
| `GameShared/FrameSync/Battle/DashTuning.cs` | 新建，Dash/体力常量 |
| `GameShared/FrameSync/Battle/SeparationNormalResolver.cs` | **新建**，法线解析纯函数（Step 4） |
| `GameShared/FrameSync/Battle/BuffConfig.cs` | 加 `DisplacementEffect?`（前置 A） |
| `GameShared/FrameSync/Battle/BuffSystem.cs` | `ApplyBuff` 加非 Numeric 分支写位移字段；**`RemoveBuffInternal`（:457）加位移字段清理**（P1-3） |
| `GameShared/FrameSync/Battle/PlayerState.cs` | 位移字段（Dash/击退各 VelX/Y/RemainingFrames）、恢复计数器、两体力属性 + switch |
| `GameShared/FrameSync/Battle/PlayerStateSnapshot.cs` | 同上全部字段 |
| `GameShared/FrameSync/Battle/NumericModifier.cs` | `AttributeKind` 加 `Stamina=6`、`MaxStamina=7` |
| `GameShared/FrameSync/Battle/PlayerAttributeDirtyFlags.cs` | 加两位，**`All` 一并更新** |
| `GameShared/FrameSync/Battle/PlayerAttributeSnapshot.cs` | 加两字段 + 构造参数。**不加默认值参数** |
| `GameShared/FrameSync/Battle/PlayerAttributeSync.cs` | `ComputeDirtyMask`/`Merge` 各加两项 |
| `GameShared/FrameSync/Battle/NumericState.cs` | **`GetBaseValue`/`SetBaseValue`/`Recalculate` 三处**各加两条分支 |
| `GameShared/FrameSync/Snapshot/StateHasher.cs` | **玩家段 + `Numeric.BaseAttributes` 段 + SkillGraph 执行态段各加**（最易漏） |
| `GameShared/FrameSync/Battle/FixedPhysicsBody.cs` | 加 `IsKinematicObstacle`（**不进快照**，决策五） |
| `GameShared/FrameSync/Battle/FrameSyncPhysicsWorld.cs` | `SetBodyKinematicObstacle` 入口、`CalculateSeparationShares` 尊重标志、`DetermineSeparationNormal` 改调 `SeparationNormalResolver` |
| `GameShared/FrameSync/Battle/RemotePlayerBuffer.cs` | 移除条目处同步 `RemoveBody` |
| `GameShared/SkillGraph/RuntimeSkillGraph.cs` | `BattleSkillGraphRuntime` 加 `CaptureExecutions`/`RestoreExecutions`（前置 B，value=`ActiveSkillExecutionSnapshot{CasterId,TargetId,SkillId,RunnerSnapshot}`，P1-1）；**`QueueSkillRequest` 加 Fixed64 dx/dy**（P1-2） |
| `GameShared/SkillGraph/SkillExecutionSnapshotCodec.cs` | **新建**（前置 B），Dictionary 排序序列化/哈希 |
| Dash 技能图数据 + Dash/击退 Buff 配置 | **新建**（Step 2/4）；既有 `ApplyBuffNodeHandler` 不携带动态速度，**需补动态速度 handler + 体力校验扣除服务**（P1-2） |
| `GameShared/SkillGraph/SkillHandlers.cs` | **新增动态速度 handler**（给 DisplacementEffect 传起手方向，P1-2） |
| `GameShared/SkillGraph/SkillContext.cs` | `ISkillRuntimeServices` 加"校验体力并扣除"接口（P1-2） |
| `GameServer/Tools/NetworkProtocol/Outer/OuterMessage.proto` | `PlayerSnapshot`、**`NumericSnapshot`**、`SkillExecutionSnapshot` 段**末尾追加**，跑 `Run.bat` |
| `GameServer/Server/Entity/Battle/BattleLogic.cs` | Tick 内驱动 SkillGraph/Displacement/Stamina、新接触触发挂击退 Buff。**本阶段刻意改内核**（决策六） |
| `GameServer/Server/Entity/Battle/BattleComponent.cs` | `SubmitInput`/`Tick` 接线。**按方法名定位** |
| `GameLogic/Battle/BattleSimulation.cs` | `ApplyLocalPrediction` 驱动（**不含触发**）、`ApplyAuthoritativeSnapshot` 镜像远端 body、`CheckConsistency` 日志分字段 |
| `GameLogic/Battle/BattleClientController.cs` | Dash 走 `BufferedInputKind.Skill`+`DashId`、拖影、体力条、HUD |
| `GameLogic/Battle/BattleSnapshotProtocolMapper.cs` | `PlayerSnapshot`/`NumericSnapshot`/SkillExecution To/From 双向补全 |
| `GameLogic/Battle/BattlePredictionSelfTestSuite.cs` | **33 条**，注册两处 |
| `GameLogic/Battle/Automation/BattleAutomationRuntime.cs` | 四个 report 字段（`StateMismatch`/`PositionMismatch` **分开计**）+ 新场景判据 |
| `GameServer/Server/Entity/TestHarness/TestRunner.cs` | `two-client-knockback` 登记三处 |
| `Tools/AutomationAcceptance/` 三份既有指南 | **基线判据改注**（决策六），S3 那份优先 |
| `Plan/…/S6-回滚粒度改造-…-实现计划.md` | 决策六勘误 |

**不需要改**：
- `AttributeBroadcastBaseline.cs` —— 存整个 `PlayerAttributeSnapshot` 结构体，两新字段自动跟随
- `NetworkConditionSimulator.cs` —— S4 注入能力已够
- S7 的 RTT 通路 —— 无交互
- SkillGraph 既有节点 handler（`ApplyBuffNodeHandler` 等）—— 已就绪，但**不携带动态速度**，需新增动态速度 handler（见上表，P1-2）

## 估时

**6~7 天**（两个前置上修，但 Dash 输入通路复用 SkillId 省回一部分）。仍是 S1~S7 里最大的一个。

| 部分 | 估时 |
|---|---|
| Step 0a 位移 effect 通道（前置 A） | 6 小时 |
| Step 0b SkillGraph 可回滚通路（前置 B） | 4 小时 |
| Step 1 体力接入属性系统（两 kind + `NumericSnapshot` 协议化 + 基线确认） | 6 小时 |
| Step 2 Dash 技能图 + Buff 配置 | 4 小时 |
| Step 3 Dash 输入通路（复用 SkillId） | 2 小时 |
| Step 4 击退 + 服务端触发 + `SeparationNormalResolver` 抽取 | 5 小时 |
| Step 5 远端 body 镜像 + kinematic 建模 | 3 小时 |
| Step 6 表现层 + HUD | 3 小时 |
| Step 7 mismatch 日志分字段 | 1 小时 |
| Step 8 三十三条用例 | 8 小时 |
| Step 9 report + 场景 + 基线更新 + 三份指南改注 | 2.5 小时 |

**建议先写用例再改实现的五条**（风险集中处，症状都具误导性）：
1. `skillgraph-execution-replays-after-rollback` —— 漏了则 Dash 回滚错位，症状像「网络不稳」
2. `numeric-stamina-base-proto-roundtrip` —— 漏了则体力恒为 0
3. `knockback-decay-timeline-is-frame-exact` —— 漏了则「击退比预期弱一点」，几乎不被注意
4. `knockback-state-replays-without-new-mismatch` —— 本阶段演示价值核心
5. `remote-body-not-displaced-by-occupancy` —— 漏了则症状像「决策五无效」

## 验收指南

`Tools/AutomationAcceptance/Dash与击退-验收测试指南-<日期>.md`（随实现一并产出）。必须包含：

1. **哈希基线变化是预期行为** —— 与 S3~S7 相反，**放最前面**。判据「变一次后连续两跑一致」
2. **三份既有指南改注位置** —— 验收 agent 可能手持旧指南
3. **弱网 Dash 判据** —— 起手不等确认、体力与位移最终收敛
4. **击退 mismatch 定量阈值** —— 绑定固定场景（双向 100ms、零丢包、固定种子、3 次对撞）：每次触发 `StateMismatch ≤ 1`、总数 `≤ 3`。**写明「换网络参数需重新标定」**
5. **`StateMismatch` 与 `PositionMismatch` 分开统计**
6. **接触期 before/after 对比** —— `contactMismatchFrames ≤ 6` 且下降 ≥ 60%
7. **「击退确实发生了」断言清单**
8. **残差声明** —— 镜像用延迟 RTT/2 对手位置，残差有界但不为零，**不判 FAIL**
9. **参数断言** —— 越界抛异常；调参后重跑
10. **口径声明** —— 「触发不预测、演化照常预测」。不应按「客户端完全不知道击退」判 FAIL
11. **SkillGraph 可回滚声明** —— Dash 走 SkillGraph，执行态进快照，回滚逐位恢复（前置 B）
12. **真机观测项** —— Dash 拖影、体力条、击退 ghost 分离；长按 Shift 不连发

## 后续衔接

- **S9（断线重连）** —— 重连后须恢复 Dash 技能图执行态、体力、击退位移。**SkillGraph 执行快照与 `StaminaRegenCounterFrames` 最易漏**
- **S10（远端插值）** —— 决策五镜像 body 位置届时改用插值值，缩小残差。调试面板加 Dash/体力/击退
- **S11（求职展示）** —— 击退 ghost 分离是主素材。**定稿后复核 `MaxSmoothingDistance=6.0`**（决策四）
- **更多技能** —— 本阶段把 SkillGraph 可回滚通路（前置 B）和位移 effect 通道（前置 A）打通后，后续技能直接配技能图 + Buff 即可，无需再动基础设施
- **玩家间碰撞的客户端预测** —— 决策五只做只读镜像。真要预测别人需另开号，与 S6 方向相反
- **服务端脏字段过滤** —— S6 Context 末节列为「另开号」。本阶段新增多个快照字段后带宽收益更明显
- **B 编号待查**：`AttributeBroadcastBaseline.Remove` 两个字典删除不一致。**与本阶段无关**，S9 前单独核实

---

## 附录 A：架构决策调研依据

> 本节是各项决策（含前置 A/B）的调研支撑与证据，供理解「为什么这么定」，**非执行内容**。正文已给出结论，这里补背景、范式对比与行号证据。

### A.1 参考项目 NKGMobaBasedOnET 怎么做（结论：不能照搬）

WeDoBest 的技能图/Buff 设计时参考了 `D:\Unity\NKGMobaBasedOnET`。源码核查结论：

- **技能图 = NPBehave 行为树**：`SkillGraph.cs`（GraphProcessor 节点图）是纯 Editor，运行时是常驻 NPBehave 行为树 `NP_RuntimeTree`，挂 `NP_RuntimeTreeManager`。技能释放 = 往黑板写输入，`BlackboardCondition` 装饰器激活子树；计时走帧级 `Clock`。
- **技能节点挂 Buff 调用链**：`NP_AddBuffAction.AddBuff()` → `VTDUtilities.AutoAddBuff()` → `BuffFactory.AcquireBuff()` → `ABuffSystemBase.Init()` → `BuffTimerAndOverlayHelper.CalculateTimerAndOverlay()` → `BuffManagerComponent.AddBuff()`。
- **持续状态全在 Buff 侧**：Buff 的计时/叠加/状态机由目标 Unit 的 `BuffManagerComponent` 持有并快照化；技能图只负责「触发挂 Buff」瞬时动作。可回滚性落在 Buff 一侧，与技能图执行解耦。

**关键真相：参考项目并未真正解决「技能图可回滚」**：

| 事实 | 证据 |
|---|---|
| 黑板值进了快照 | `NP_RuntimeTreeBBSnap` 只存黑板键值 |
| **行为树执行态没进快照** | 不存节点 `currentState`、Composite 游标、Clock 待触发表 |
| **回滚时不回写技能图** | `NP_RuntimeTreeManagerTicker` 未实现 `OnLSF_RollBackTick` |
| **Whole 快照不用于还原** | 只用于求 Delta，回滚靠重放权威 Delta + 追帧重算 |
| **确定性不靠定点数** | 随机只在 `#if SERVER`，靠服务器权威 + 预测校正兜底 |

它「看起来」没问题，是因为服务器每帧广播正确的黑板/Buff Delta，客户端被 `LSF_SyncBuffHandler` 按 ADD/CHANGE/REMOVE 强行校正。**WeDoBest 走全量快照 + 回滚还原路线（Timeline 技术约束 3），不能用参考项目回答「怎么让 SkillGraph 可回滚」。**

> 注：NKGMoba 的 `BuffWorkTypes` 有「击退」槽位（`RePluse=1<<1`），但搜索范围内只见它被当状态互斥标志用，**未验证其真跑通过 Buff 驱动的击退位移**，只能当概念弱参考。

### A.2 范式对照：两套不同模型

| 维度 | NKGMobaBasedOnET | WeDoBest |
|---|---|---|
| 同步模型 | 服务器权威 + Delta 校正 + 追帧重算 | 全量快照 + 哈希对账 + 回滚**还原**重放 |
| 快照用途 | 只算 Delta，**不还原** | 直接还原整份状态 |
| 确定性来源 | 服务器权威（随机只在服务端） | 定点数 + 纯函数帧逻辑 |
| 技能图执行态 | **不快照、不回滚**，靠服务器兜底 | 欲快照化（runner 已有 `SkillExecutionSnapshot`，容器层未接通 → 前置 B 补） |

### A.3 WeDoBest 现状实测（带行号）

技能图在 `UnityProject/Assets/GameScripts/HotFix/GameShared/SkillGraph/`，Buff 在 `.../GameShared/FrameSync/Battle/`。

- **技能图运行时**：节点图 + `ISkillNodeHandler.Execute(node, context)` + `SkillContext`（含黑板）+ 条件/变量系统，与 NPBehave 同类。`BattleSkillGraphRuntime`（`RuntimeSkillGraph.cs:455`）执行容器，`Step`（:495）按 casterId 排序驱动各 Runner。**容器只有 `Clear()`（:548），无 Capture/Restore —— 前置 B 的缺口。**
- **已就绪（降低前置 B 工作量）**：
  - `SkillGraphRunner.GetSnapshot()`（`SkillGraphRunner.cs:564`）/ `Restore(snapshot)`（:584）/ `SkillBlackboard.CaptureSnapshot`+`RestoreSnapshot` 全现成。
  - `IBuffCommandSink` 已注入 `SkillContext`（`RuntimeSkillGraph.cs:462,:600`）；`RuntimeBuffTargetSelectors`（:75）已预留。
  - 挂 Buff handler 已就绪：`ApplyBuffNodeHandler`/`RemoveBuffNodeHandler`/`BuffConditionNodeHandler`（`SkillHandlers.cs:93-158`）并已 `RegisterDefaults`。**Dash 技能图挂 Buff 只需配置数据，不补代码。**
- **Buff 系统**：`BuffSystem`（静态类）。Buff 列表/`NextRuntimeBuffId`/Numeric 修饰器已进 `ArePlayerSnapshotsEquivalent` 与 `StateHasher.Hash` —— **击退走 Buff 可回滚性白拿**。但 `BuffEffect` 仅 `AttributeKind/ValueType/Value`，`ApplyBuffEffects`（`BuffSystem.cs:294-302`）唯一出口 `Numeric.AddModifier` —— **不支持位移，前置 A 的缺口**。

### A.4 架构原则：B1 两条（缺一不可）

1. **快照通路接通**（前置 B）：`SkillExecutionSnapshot` 接进容器级 → PlayerState → 哈希/协议/回滚，让技能图整体可回滚，是保底。
2. **持续状态尽量下沉**（设计纪律）：跨帧持续状态（计时、衰减、层数）优先用 Buff/`PlayerState` 字段承载，减少对执行态快照的依赖 —— 执行态越简单，快照/哈希/协议的确定性越易守住。

> 通路接通提供保底能力，下沉原则让日常开发不必每次都赌执行态快照的正确性。

### A.5 动手前待核实（风险表之外）

1. **`SkillBlackboardSnapshot` 结构**：读 `SkillContext.cs` 确认其字段与序列化方式（进 proto 的可行性）。
2. **`SkillGraphStepBSmokeTest.cs`**（`GameLogic/SkillGraph/`）：已有 Step B 冒烟测试，核实覆盖范围，避免重复造。
3. **用例基数**：S7 落地后重数，不要沿用任何文档数字。
4. **`QueueSkillRequests` 重放路径**：决策七复用 SkillId，须核实该路径在客户端重放时同样被调用（否则 Dash 输入在重放时丢失）。

### A.6 参考证据索引

**WeDoBest（实测）**
- `GameShared/SkillGraph/RuntimeSkillGraph.cs`：`BattleSkillGraphRuntime`（:455-676，`Clear` :548）、`IBuffCommandSink`（:462,:600）、`RuntimeBuffTargetSelectors`（:75）。
- `GameShared/SkillGraph/SkillGraphRunner.cs`：`SkillExecutionSnapshot`（:309）、`GetSnapshot`（:564）、`Restore`（:584）、`SkillExecutionEvent/Diff/Comparer`（:139-287）。
- `GameShared/SkillGraph/SkillHandlers.cs`：挂 Buff handler（:93-158）。
- `GameShared/FrameSync/Battle/BuffSystem.cs`：`ApplyBuffEffects`（:294-302）；同目录 `BuffEffect.cs`（仅三字段）。
- `GameShared/FrameSync/Battle/FrameSyncPhysicsWorld.cs`：`ApplyBodyImpulse`（:83）。

**NKGMobaBasedOnET（scout 核查）**
- 技能图=NPBehave：`Model/NKGMOBA/Battle/NPBehave/...`（`NP_RuntimeTree`、`Node.cs`、`Clock.cs`、`Blackboard.cs`、`BlackboardCondition.cs`）。
- 挂 Buff 链：`NP_AddBuffAction` → `VTDUtilities.AutoAddBuff` → `BuffFactory.AcquireBuff` → `ABuffSystemBase.Init` → `BuffTimerAndOverlayHelper.CalculateTimerAndOverlay` → `BuffManagerComponent.AddBuff`。
- 帧同步：`Hotfix/.../LockStepStateFrameSync/LSF_ComponentUtilities.cs`、`Ticker/NP_RuntimeTreeManagerTicker.cs`（无 `OnLSF_RollBackTick`）、`Handler/LSF_SyncBuffHandler.cs`。
- 快照：`Model/.../SkillSystem/LockStepStateFrameSync/BuffSnapInfoCollection.cs`、`Model/.../NPBehave/LockStepStateFrameSync/NP_RuntimeTreeBBSnap.cs`。
