# 战斗Buff系统-v0.0a计划

## 目标

完成 NKGMobaBasedOnET 参考仓库到 TEngine 的战斗/Buff 领域模型映射，锁定术语、目录基线、核心数据结构定义和关键架构决策，为 MVP-a（属性与伤害闭环）铺路。

## 前置依赖

无。

## 范围

1. **领域映射表**：参考仓库核心模块 → TEngine 对应关系
2. **目录基线**：`GameShared/FrameSync/Battle/` 新增文件的命名约定
3. **核心数据结构定义**：BuffState、NumericModifier、NumericState、BuffCommands 等结构签名
4. **属性权威性决策**：现有 int 字段与 NumericState 的关系
5. **同步策略决策**：Buff 状态的快照/协议/脏同步方案
6. **命令模型对齐**：Buff 命令与帧命令管线的接入方式
7. **PlayerState 扩展规划**：ActiveBuffs + Numeric 字段的接入方式
8. **ISkillRuntimeServices 接口扩展规划**：接口签名及归属决策
9. **非确定性风险点**：全部已知风险及替代方案

## 非目标

- 具体代码实现（MVP-a/MVP-b 的事）
- Luban 配置表结构设计
- 表现层（BuffViewManager 等）
- 叠加/互斥/驱散规则

---

## 已锁定的关键架构决策

以下决策在 v0.0a 阶段锁定，后续阶段不得违反。

### 决策 1：属性权威性模型

**现有 5 个 int 字段（Health/MaxHealth/Mana/MaxMana/Attack）是"基础值"，始终是回滚/哈希的权威来源。**

```
属性计算链路：
  BaseValue（PlayerState 上的 int 字段）      ← 权威基础值
    + Σ FlatModifier（NumericModifier）        ← 固定值修饰
    × (1 + Σ PercentModifier / 10000)          ← 百分比修饰（万分比）
  = FinalValue                                 ← 计算结果，不存储

写入链路：
  DamageSystem → 直接写 PlayerState.Health（扣血）
  NumericSystem.Recalculate() → 更新脏标记 → 写回 PlayerState 对应字段
```

**NumericState 是修饰器容器，不是第二套权威值**。它的职责是：
- 管理活跃修饰器列表（Add/Remove by source）
- 每帧重算最终值并写回 PlayerState 对应 int 字段
- 快照/恢复时，NumericState 连同修饰器一起被保存/恢复

这意味着：
- 回滚只需恢复 PlayerState（已有机制）+ NumericState 修饰器列表
- 一致性哈希仍然只看 PlayerState 上的 int 字段（已被 NumericState 写回）
- 现有 StateHasher 无需修改哈希逻辑，只需在 Tick 末尾确保 NumericState.Recalculate() 已执行

### 决策 2：数值计算公式（锁定）

```
FinalValue = (Base + ΣFlat) × (1 + ΣPercent / 10000)
取整方式：向零截断（Truncate toward zero）
溢出保护：clamp(int.MinValue, int.MaxValue)
负数保护：Health/Mana 的 FinalValue clamp(0, MaxValue)
```

**修饰器应用顺序**：先 Flat 后 Percent，不可互换。同类型修饰器按 SourceBuffId 升序累加。

### 决策 3：同步策略

**Buff 状态全量进快照，属性走现有脏同步。**

```
每帧 Tick 末尾：
  1. NumericSystem.Recalculate() → 更新 PlayerState.int 字段 + 脏标记
  2. BuffSystem.ApplyTick() → 更新 BuffState 剩余帧/层数
  3. SnapshotManager.Save() → 保存完整快照（含 Buff 列表 + Numeric 修饰器）

服务端广播（S2C_FrameSnapshot）：
  - 属性：复用现有 PlayerAttributeDirtyFlags 脏同步（v0.4b 已实现）
  - Buff 列表：每帧全量广播 BuffState[]（MVP 阶段，~30 KB/s/客户端）
  - 后续 v0.4a 引入 Buff 脏标记增量优化（目标 ~3-5 KB/s）

客户端合并（BattleSimulation.EnqueueServerSnapshot）：
  - 属性：现有合并逻辑
  - Buff 列表：全量替换 ActiveBuffs
  - 一致性检查：对比 Buff 列表哈希 + 属性哈希

协议扩展方向（MVP-a 实施）：
  S2C_FrameSnapshot 增加：
    repeated BuffSnapshot active_buffs = N;    // 全量 Buff 列表
    NumericSnapshot numeric = N+1;             // 修饰器快照（MVP-b）
```

### 决策 4：命令模型对齐

**Buff 命令必须是帧对齐的确定性命令，走 Tick 内消费，不走异步接口。**

```
命令生命周期：
  SkillGraph 节点/其他系统 → 写入 BattleWorldState.CommandQueue
  → Tick 开头 ProcessCommands(frameIndex) 按 frameIndex 顺序消费
  → ApplyBuffCommand → BuffSystem.AddBuff()
  → RemoveBuffCommand → BuffSystem.RemoveBuff()

关键约束：
  - 命令必须带 FrameIndex，与输入命令同一管线
  - 命令是 sealed class（非 struct），配合现有 CommandPool<T> where T : class, IResettable, new()
  - 客户端预测：本地帧写入预测队列，服务端权威帧写入权威队列
  - 回滚时命令队列一并恢复
```

**ISkillRuntimeServices 不新增 Buff 方法**。Buff 命令归 BattleLogic/BattleSimulation 的帧命令聚合器管理，与 ISkillRuntimeServices（偏表现层/异步编排）解耦。MVP-c 阶段定义独立的 `IBuffCommandSink` 接口供 SkillGraph 节点调用。

---

## 领域映射表

基于 NKGMobaBasedOnET 参考仓库的分析结论，映射到 TEngine 现有架构：

| 参考仓库概念 | TEngine 对应 | 位置 |
|---|---|---|
| BuffFactory | `BuffSystem.AddBuff()` 纯函数 | GameShared/FrameSync/Battle/BuffSystem.cs |
| IBuffSystem / ABuffSystemBase | `BuffState` 数据驱动，无继承体系 | GameShared/FrameSync/Battle/BuffState.cs |
| BuffTimerAndOverlayHelper | `BuffSystem.ApplyTick()` 纯函数处理计时+叠加 | 同 BuffSystem.cs |
| BuffManagerComponent | `PlayerState.ActiveBuffs` 字段 | PlayerState.cs 扩展 |
| NumericComponent | `NumericState`（修饰器容器）+ `NumericSystem`（纯函数） | GameShared/FrameSync/Battle/NumericState.cs |
| DamageCalcHelper | `DamageSystem.CastDamage()` 纯函数 | GameShared/FrameSync/Battle/DamageSystem.cs |
| LSF Tick / 帧命令 | 现有 `TickDispatcher` + 帧命令队列 | 已有 |
| Buff/属性同步命令 | `ApplyBuffCommand` / `RemoveBuffCommand`（帧对齐） | GameShared/FrameSync/Battle/BuffCommands.cs |
| Buff/属性协议广播 | `S2C_FrameSnapshot` 扩展 | 协议层扩展 |
| 一致性检查（Buff 域） | `StateHasher` 扩展 + Buff 列表哈希 | StateHasher.cs 扩展 |

### 设计决策

1. **无继承 Buff 体系**：参考仓库用 ABuffSystemBase 继承体系，TEngine 改为纯数据驱动。BuffState 是 readonly struct，行为由 BuffSystem 纯函数根据 BuffFlags 分支处理。理由：快照/哈希更简单，避免继承带来的序列化复杂度。

2. **命令化副作用**：SkillGraph 节点不直接操作 BuffSystem，通过帧命令队列写入命令，Tick 内按 FrameIndex 顺序消费。与现有输入命令管线一致。

3. **帧计时而非墙钟**：Buff 持续时间用帧数表示（`RemainingFrames`），不依赖 wall clock。与已锁定的"战斗时间以帧计时"决策一致。

4. **修饰器容器而非第二套属性**：NumericState 只管修饰器列表和重算逻辑，权威值始终在 PlayerState 的 int 字段上。

---

## 目录基线

```
GameShared/FrameSync/Battle/          ← 现有目录，纯 C# 共享逻辑
├── MoveSystem.cs                     ← 已有
├── PlayerState.cs                    ← 已有，扩展
├── PlayerStateSnapshot.cs            ← 已有，扩展
├── PlayerAttributeSnapshot.cs        ← 已有
├── PlayerAttributeDirtyFlags.cs      ← 已有，扩展
├── PlayerAttributeSync.cs            ← 已有
├── BattleWorldState.cs               ← 已有
├── BattleWorldSnapshot.cs            ← 已有
├── ...                               ← 其他已有文件
│
├── BuffState.cs                      ← 新增：Buff 实例状态（readonly struct）
├── BuffSystem.cs                     ← 新增：Buff 纯函数系统
├── BuffCommands.cs                   ← 新增：ApplyBuffCommand / RemoveBuffCommand（sealed class）
├── NumericModifier.cs               ← 新增：属性修饰器（readonly struct）
├── NumericState.cs                   ← 新增：修饰器容器 + 重算逻辑
├── DamageSystem.cs                   ← 新增：伤害计算纯函数
└── BuffFlags.cs                      ← 新增：Buff 特征标志（Flags 枚举，替代线性枚举）
```

### 命名约定

- 系统：`XxxSystem.cs`，公开静态纯函数，无实例状态（与 MoveSystem 一致）
- 状态：`XxxState.cs`，sealed class（需要内部状态变更）或 readonly struct（不可变数据）
- 命令：`XxxCommand.cs`，sealed class 实现 IResettable，配合 CommandPool\<T> 使用
- 快照：`XxxSnapshot.cs`，readonly struct，不可变
- 标志：`XxxFlags.cs`，[Flags] 枚举

---

## 核心数据结构定义

以下为接口签名级别定义，不含具体实现。字段设计基于参考仓库分析和 TEngine 现有约束。

### BuffFlags（替代线性 BuffType 枚举）

```
[Flags] enum BuffFlags : uint：
  None        = 0
  Duration    = 1 << 0       — 有持续时间的（RemainingFrames > 0 时倒计时）
  Stackable   = 1 << 1       — 允许叠层
  Passive     = 1 << 2       — 被动/永久（不自动过期）
  Dispellable = 1 << 3       — 可被驱散
  Debuff      = 1 << 4       — 减益标记（用于驱散/免疫判断）
```

设计要点：
- 用 Flags 替代线性枚举，解决"生命周期/叠层/作用形态"三个维度不正交的问题
- Duration 和 Passive 互斥（由配置表保证），但 Flags 模式允许未来扩展组合
- 例：一个可叠加的持续型减益 Buff = Duration | Stackable | Dispellable | Debuff

### BuffState

```
readonly struct BuffState：
  RuntimeBuffId : long        — 运行时唯一实例 ID（自增，区分同 BuffId 多实例）
  BuffId : int                — 配置表 Buff ID
  CasterId : long             — 施加者 PlayerId
  TargetId : long             — 承受者 PlayerId
  StackCount : int            — 当前层数（最小 1）
  RemainingFrames : int       — 剩余持续帧数（-1 = Passive 类型，不倒计时）
  AppliedFrame : int          — 施加时的全局帧号（用于 Tick 节拍计算和排序）
  Flags : BuffFlags           — Buff 特征标志
```

设计要点：
- `RuntimeBuffId` 解决多来源同 Buff 实例冲突（参考仓库结论第 5 点）
- `RemainingFrames = -1` 仅用于 Passive 类型 Buff，不参与倒计时
- `AppliedFrame` 用于决定同帧多 Buff 的处理顺序（按 AppliedFrame 升序，稳定排序）
- readonly struct 保证不可变，快照时直接复制

### NumericModifier

```
readonly struct NumericModifier：
  SourceBuffId : long              — 来源 Buff 的 RuntimeBuffId（绑定关系）
  ValueType : ModifierValueType   — Flat / Percent
  AttributeKind : AttributeKind   — 作用属性（Health/MaxHealth/Mana/Attack 等）
  Value : int                     — Flat 为整数增减；Percent 为万分比
```

设计要点：
- 百分比用万分比（int）避免浮点精度问题
- 通过 `SourceBuffId` 绑定 Buff，Buff 移除时联动清理所有关联修饰器
- 同类型修饰器按 SourceBuffId 升序累加（确定性顺序）
- 不需要独立 ModifierId——SourceBuffId 即唯一标识

### NumericState

```
sealed class NumericState：
  修饰器列表 : List<NumericModifier>              — 活跃修饰器（按 SourceBuffId 排序）
  脏标记 : PlayerAttributeDirtyFlags              — 上次 Recalculate 后变更的属性

  核心方法签名：
    void AddModifier(NumericModifier mod)          — 插入并保持排序
    void RemoveBySource(long sourceBuffId)         — 按来源 Buff 移除所有修饰器
    void Recalculate(PlayerState target)           — 重算最终值并写回 target 对应 int 字段
    NumericModifierSnapshot CaptureSnapshot()      — 快照
    void RestoreSnapshot(NumericModifierSnapshot)  — 恢复
```

关键约束：
- **NumericState 不是权威值容器**，而是修饰器管理器。权威值始终在 PlayerState 的 int 字段
- `Recalculate(PlayerState)` 将计算结果写回 PlayerState.Health/Attack 等字段
- 快照保存修饰器列表，恢复后调用 Recalculate 即可重建正确属性值
- List 始终按 SourceBuffId 排序，确保遍历顺序确定性

### BuffCommands

```
sealed class ApplyBuffCommand : IResettable：
  CasterId : long
  TargetId : long
  BuffId : int
  DurationFrames : int         — -1 = 被动
  StackCount : int             — 初始层数
  FrameIndex : int             — 帧对齐：命令应在哪个 Tick 消费

sealed class RemoveBuffCommand : IResettable：
  TargetId : long
  RuntimeBuffId : long         — 0 = 移除该 BuffId 的所有实例
  BuffId : int
  RemoveReason : RemoveReason 枚举 — Expired / Dispel / Death / Manual
  FrameIndex : int             — 帧对齐
```

设计要点：
- **sealed class**（非 struct），配合现有 `CommandPool<T> where T : class, IResettable, new()`
- 必须带 `FrameIndex`，与输入命令走同一帧对齐管线
- 命令写入 BattleWorldState 的命令队列，Tick 内按 FrameIndex 顺序消费
- RemoveBuffCommand 支持"按实例"（RuntimeBuffId > 0）和"按 BuffId 全部"（RuntimeBuffId = 0）两种模式

### PlayerState 扩展

现有 PlayerState 扩展方向（MVP-a/MVP-b 逐步添加，v0.0a 只规划）：

```
现有字段（保持不变，是权威基础值）：
  PlayerId, X, Y, Health, MaxHealth, Mana, MaxMana, Attack

MVP-a 新增：
  NumericState Numeric                    — 修饰器容器（管理修饰器列表，写回 int 字段）

MVP-b 新增：
  List<BuffState> ActiveBuffs             — 当前活跃 Buff 列表（按 RuntimeBuffId 排序）
  long NextRuntimeBuffId                  — 运行时 Buff ID 分配器（自增计数器）

快照扩展方向：
  PlayerStateSnapshot 增加：
    BuffState[] ActiveBuffs               — Buff 快照数组（已排序）
    long NextRuntimeBuffId                — 分配器状态（必须入快照，否则回滚后 ID 漂移）
    NumericModifierSnapshot Numeric       — 修饰器快照
```

关键约束：
- `NextRuntimeBuffId` **必须入快照**。回滚恢复后，下一个 AddBuff 分配的 ID 必须与原始序列一致，否则排序/哈希/移除全部出错
- `ActiveBuffs` 始终按 RuntimeBuffId 排序，确保遍历/哈希顺序确定性
- 快照恢复流程：RestoreSnapshot → NumericState.RestoreSnapshot() → NumericState.Recalculate() 写回 int 字段

### ISkillRuntimeServices 与 IBuffCommandSink

**决策：Buff 操作不走 ISkillRuntimeServices。**

ISkillRuntimeServices 当前是偏表现层的异步接口（Log、DelayAsync、PlayAnimationAsync）。Buff 命令是帧对齐的确定性操作，不应挂在异步接口上。

```
新增接口方向（MVP-c 实施）：
  interface IBuffCommandSink：
    void EnqueueApplyBuff(ApplyBuffCommand cmd)
    void EnqueueRemoveBuff(RemoveBuffCommand cmd)
    bool HasBuff(long targetId, int buffId)
    int GetBuffStackCount(long targetId, int buffId)

  BattleLogic / BattleSimulation 实现 IBuffCommandSink
  SkillGraph 节点通过 SkillContext 获取 IBuffCommandSink
```

---

## 非确定性风险点

映射过程中标注的确定性风险及替代方案：

| 风险点 | 参考仓库做法 | TEngine 替代方案 |
|---|---|---|
| Buff 持续时间用 wall clock | Stopwatch / Time.time | 帧计时：RemainingFrames 倒计时，dt 固定 33.333ms |
| 随机数（暴击/闪避） | System.Random | 帧号 + PlayerId 作为种子，或预计算序列（远期） |
| 百分比修饰用 float | float 乘法 | 万分比 int 运算，最终除 10000 |
| Buff 触发回调顺序不确定 | 多态分发 | BuffSystem.ApplyTick 统一处理，按 RuntimeBuffId 排序 |
| 叠加规则判断时机 | 运行时动态判断 | AddBuff 时一次性确定叠加策略，存入 BuffState |
| List 遍历中修改 | foreach 中 Add/Remove | ApplyTick 只标记待删除，Tick 末尾统一处理增删 |
| ActiveBuffs 遍历顺序 | Dictionary 无序 | List\<BuffState> 按 RuntimeBuffId 排序，快照时已排序 |
| NumericModifier 累加顺序 | 无保证 | 同类型按 SourceBuffId 升序累加 |
| 百分比累计溢出 | 无保护 | ΣPercent clamp(-10000, int.MaxValue)，防止除零/反转 |
| 同帧多 Buff 执行顺序 | 无保证 | 按 AppliedFrame → RuntimeBuffId 双重排序 |
| DOT tick 节拍不一致 | 挂载时间各异 | AppliedFrame 决定首 tick 偏移，后续每 N 帧一次 |
| RuntimeBuffId 回滚漂移 | 无保护 | NextRuntimeBuffId 入快照，恢复后继续自增 |
| MISMATCH 日志不覆盖 Buff 域 | 只打印位置+属性 | 一致性检查扩展：打印 Buff 数量/ID 列表差异 |

---

## 完成标准

1. 领域映射表完成，所有参考仓库核心模块都有 TEngine 对应关系
2. 目录基线锁定，所有新增文件命名约定明确
3. 核心数据结构（BuffState、NumericModifier、NumericState、BuffCommands）签名定义完成
4. **属性权威性决策已锁定**：现有 int 字段是基础值，NumericState 是修饰器容器
5. **同步策略已锁定**：Buff 全量进快照，属性走脏同步
6. **命令模型已对齐**：sealed class + FrameIndex + 帧对齐消费
7. **RuntimeBuffId 回滚已闭环**：NextRuntimeBuffId 入快照
8. PlayerState 扩展规划明确，不影响现有字段
9. IBuffCommandSink 接口归属已确定
10. 所有非确定性风险点已标注替代方案
11. 团队评审通过

---

## 风险与应对

| 风险 | 影响 | 应对 |
|---|---|---|
| NumericState 重算时机 | 重算晚于哈希会导致不一致 | Tick 末尾固定顺序：Recalculate → ApplyTick → Save |
| Buff 全量快照带宽 | 30Hz 全量广播开销 | MVP 阶段可接受，v0.4a 引入脏标记增量 |
| BuffState readonly struct + List 查找 | List\<readonly struct> 查找需遍历 | MVP-b 先用 List + 线性查找，v0.4a 按需优化 |
| 命令队列容量 | 高频 Buff 施加可能溢出 | MVP 阶段用固定容量 + 断言，工程化阶段做动态扩容 |
| BuffFlags 组合爆炸 | Flag 组合语义不够直观 | 配置表约束合法组合，运行时只做 Flags 判断 |

---

## 验证方式

- **文档评审**：所有映射关系和数据结构由 reviewer 检查
- **一致性检查**：数据结构与已锁定的技术决策（帧计时、纯函数、命令化）无矛盾
- **可执行性检查**：每个数据结构的签名能直接指导 MVP-a 的代码实现
- **自测类别规划**（MVP-a/b 逐步补充）：
  - 快照 round-trip：保存/恢复后 NumericState 修饰器列表和属性值一致
  - Hash 影响验证：Buff 增删后 StateHasher 结果可预测变化
  - Dirty merge：客户端预测帧的 Buff 修饰器与服务端权威快照正确合并
  - RuntimeBuffId 连续性：回滚后恢复 NextRuntimeBuffId，后续分配不漂移
  - 同帧多 Buff 顺序稳定性：同帧施加 N 个 Buff，ApplyTick 处理顺序确定
  - 修饰器累计确定性：同组 Flat/Percent 修饰器，累加结果在双端一致
