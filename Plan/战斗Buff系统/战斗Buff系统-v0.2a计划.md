# 战斗Buff系统-v0.2a计划

> 当前状态：实现已完成，代码级回归与 Unity 双端联调已通过（2026-05-24）。

## 目标

在现有 BuffSystem 纯函数基础上引入叠加（Overlay）、刷新（Refresh）、互斥（MutexGroup）三类规则引擎，使 AddBuff 能根据配置表定义做出确定性判定，为后续帧同步 v0.5a（Buff 脏同步）提供稳定的 Buff 语义模型。

## 前置依赖

- MVP-c（已完成）：SkillGraph 到 Buff 的命令闭环已通 ✅

## 范围

1. **BuffOverlayType 枚举**：Independent / Stack / Refresh 三种叠加模式
2. **互斥组机制**：MutexGroupId + Priority，高优先级替换低优先级，同帧同优先级由命令队列排序键决定赢家
3. **叠层效果**：StackCount 倍增 NumericModifier 的 Value
4. **配置接口**：IBuffConfigProvider 抽象 + DefaultBuffConfigProvider 代码内实现
5. **无头自测用例**：7 个 roundtrip 测试
6. **双客户端自动化场景**：3 个场景（buff-stack / buff-refresh / buff-mutex）
7. **验收指南**

## 非目标

- Luban 配置表接入（预留表结构，v0.2a 用 DefaultBuffConfigProvider 过渡）
- 多来源实例管理（v0.2b）
- 死亡/驱散/免疫（v0.2c）
- 快照/回滚集成（帧同步 v0.5a 范围）
- Buff 视觉表现层

---

## 已锁定的关键架构决策

### 决策 1：配置来源抽象

**引入 IBuffConfigProvider 接口解耦配置来源，v0.2a 用代码内 DefaultBuffConfigProvider 过渡。**

```
interface IBuffConfigProvider：
  bool TryGetBuffConfig(int buffId, out BuffConfig config)

DefaultBuffConfigProvider：
  内部 Dictionary<int, BuffConfig>
  构造时注册测试用 Buff 配置
  放在 GameShared 程序集，双端共享同一份配置
```

理由：
- v0.2a 阶段不需要 Luban 基础设施，用代码 Dictionary 即可
- 后续 Luban 接入只需替换实现，BuffSystem 本身不变
- 帧同步确定性要求：配置在运行时是只读的，不随帧变化

### 决策 2：叠加模型（三种模式）+ Stackable 迁移桥接

**用枚举替代现有 BuffFlags.Stackable 的简单布尔判断，改为三种明确的叠加模式。**

```
enum BuffOverlayType : byte：
  Independent = 0   — 独立实例，每次 AddBuff 创建新 BuffState（当前默认行为）
  Stack       = 1   — 叠层，找到同 BuffId 实例增加 StackCount，上限 MaxStack
  Refresh     = 2   — 刷新，找到同 BuffId 实例重置 RemainingFrames，不增层数
```

各模式行为：

| 模式 | 同 BuffId 已存在时 | 不存在时 |
|------|-------------------|---------|
| Independent | 创建新实例 | 创建新实例 |
| Stack | StackCount < MaxStack → 加层 + 刷新时间；已满 → 仅刷新时间 | 创建新实例，StackCount=1 |
| Refresh | 重置 RemainingFrames 为配置值（或命令指定值），不改 StackCount | 创建新实例 |

**Stackable 迁移桥接规则**（受控升级，不宣称与旧运行时逐字节兼容）：

```
配置查询优先级：
  1. IBuffConfigProvider 有配置 → 使用配置中的 OverlayType
  2. 无配置 && Flags.HasFlag(Stackable) → 按 Stack 模式解释（MaxStack = int.MaxValue，无上限）
  3. 无配置 && !Flags.HasFlag(Stackable) → 按 Independent 模式解释
```

理由：
- 现有自测（SnapshotSelfTestSuite.BuffRoundTrip）和旧 SkillGraph 图已使用 `BuffFlags.Stackable` 表达叠层语义
- 直接回退 Independent 会导致旧内容静默从"可叠层设计意图"退化为"独立实例"
- 这是一次**受控行为升级**：旧 `AddBuff` 的重复施加曾经始终创建独立实例，v0.2a 起无配置但显式带 `Stackable` 的 Buff 会进入 Stack 语义
- 桥接规则确保旧内容在没有配置表时仍能显式进入叠层规则，新配置覆盖旧桥接行为
- 后续所有新增 Buff 必须走 IBuffConfigProvider 配置，不再依赖 Flags.Stackable

### 决策 3：叠层数值效果模型

**单 modifier 倍增策略：每个 Buff 实例对每个 BuffEffect 只产生一个 NumericModifier，叠层时 Value = effect.Value × stackCount。**

```
叠层变更流程：
  1. NumericState.RemoveBySource(runtimeBuffId)   — 移除旧 modifier
  2. 按 StackCount × Effect.Value 重新添加        — 新 modifier
  3. NumericState.Recalculate(target)              — 重算属性
```

理由：
- modifier 列表不随叠层膨胀，快照和哈希压力不变
- StackCount 变更时统一走"先删后加"模式，避免 modifier 残留
- 回滚时 BuffState 已有 StackCount，自动恢复正确层数

### 决策 4：互斥组机制

**MutexGroupId 标识互斥组（0 = 无互斥），组内按 Priority 整数比较。**

```
互斥判定流程（AddBuff 时执行）：
  若 config.MutexGroupId == 0 → 跳过互斥检查
  若 config.MutexGroupId != 0：
    先遍历 ActiveBuffs 找同组 Buff，完成整组判定
    若存在任意 Priority(旧) > Priority(新) → 拒绝施加（return 0）
    否则 → 统一移除整组旧 Buff（含 NumericModifier 清理），再施加新 Buff
```

**同帧同优先级互斥决胜语义**（显式锁定）：

同帧两个新 Buff（A、B）互斥时，胜负由**命令队列消费顺序**决定——后消费的命令赢家。命令队列排序键为 `(FrameIndex, TargetId, BuffId)`（见 BattleLogic.cs:466），因此：

- 同帧同目标时，**BuffId 大的后消费，后消费的赢**
- 这不是"新替旧"，而是"BuffId 大的赢"

```
示例：同帧施加 buffId=201（MutexGroup=1, Priority=5）和 buffId=202（MutexGroup=1, Priority=5）
  命令队列排序：201 先消费，202 后消费
  消费 201 → ActiveBuffs 无同组 Buff → 施加成功
  消费 202 → ActiveBuffs 有 buffId=201（同组同优先级）→ 移除 201 → 施加 202
  结果：buffId=202 赢（BuffId 大的赢）
```

设计要点：
- 被互斥替换的 Buff 必须 RemoveBySource 清理 NumericModifier
- 同组可以有多个不同 BuffId 的 Buff，只要 MutexGroupId 相同就互斥
- 客户端和服务端的命令队列排序必须一致，否则互斥结果会分叉
- 实现上必须**先完成整组判定，再统一移除**，避免"先删低优先级、后遇到高优先级又拒绝"造成部分移除

### 决策 5：Stack 到达上限行为

**Stack 模式到达 MaxStack 后，再次施加仅刷新 RemainingFrames，不增层。**

理由：避免丢弃效果（玩家重复释放技能不会浪费），MOBA 常见语义。

### 决策 6：Refresh/Stack 不可变字段规则

**Refresh 和 Stack 操作不改变 AppliedFrame。AppliedFrame 在 Buff 创建时锁定，后续叠加/刷新操作不重置。**

```
字段变更规则（显式锁定）：
  ┌─────────────┬──────────┬──────────────┬────────────────┐
  │ 操作        │ StackCnt │ RemainingFrm │ AppliedFrame   │
  ├─────────────┼──────────┼──────────────┼────────────────┤
  │ 新建        │ 设定值    │ 设定值        │ 当前帧号       │
  │ Stack 加层  │ +1       │ 刷新为配置值  │ 不变           │
  │ Stack 已满  │ 不变     │ 刷新为配置值  │ 不变           │
  │ Refresh     │ 不变     │ 刷新为配置值  │ 不变           │
  └─────────────┴──────────┴──────────────┴────────────────┘
```

理由：
- AppliedFrame 参与 ActiveBuffs 排序（BuffSystem.cs:199）和 StateHasher 哈希（StateHasher.cs:45）
- 如果 Refresh 重置 AppliedFrame，排序和 hash 都会变，导致快照哈希不一致
- 未来 DoT/HoT Buff 的 tick 相位由 AppliedFrame 决定（首个 tick = AppliedFrame + interval），刷新持续时间只延长尾巴，不重置 tick 相位
- 这保证"客户端和服务端都对，tick 相位也一致"

---

## 新增类型定义

路径前缀：`GameShared/FrameSync/Battle/`

### BuffOverlayType

```
enum BuffOverlayType : byte
  Independent = 0
  Stack       = 1
  Refresh     = 2
```

### BuffEffect

```
readonly struct BuffEffect
  AttributeKind AttributeKind   — 作用属性
  ModifierValueType ValueType   — Flat / Percent
  int Value                     — 单层效果值
```

### BuffConfig

```
readonly struct BuffConfig
  int BuffId                    — 配置表 ID（主键）
  BuffOverlayType OverlayType   — 叠加方式
  int MaxStack                  — 最大叠层数（Independent/Refresh 时为 1）
  int MutexGroupId              — 互斥组 ID（0 = 无互斥）
  int Priority                  — 互斥组内优先级（值越大越高）
  int DurationFrames            — 默认持续帧数
  BuffFlags DefaultFlags        — 默认标志位
  BuffEffect[] Effects          — 属性效果列表
```

### IBuffConfigProvider

```
interface IBuffConfigProvider
  bool TryGetBuffConfig(int buffId, out BuffConfig config)
```

### DefaultBuffConfigProvider

```
sealed class DefaultBuffConfigProvider : IBuffConfigProvider
构造时注册测试用 Buff 配置（stack-test、refresh-test、mutex-low/high 等）
  TryGetBuffConfig 从内部 Dictionary 查询
```

---

## 实现步骤

### Step 1：新增类型文件（5 个新文件）

路径：`GameShared/FrameSync/Battle/`

1. `BuffOverlayType.cs` — 叠加类型枚举
2. `BuffEffect.cs` — 效果结构体
3. `BuffConfig.cs` — 配置记录结构体
4. `IBuffConfigProvider.cs` — 配置查询接口
5. `DefaultBuffConfigProvider.cs` — 默认实现，注册测试用配置

### Step 2：修改 BuffSystem.AddBuff

**文件**：`GameShared/FrameSync/Battle/BuffSystem.cs`

新增重载：
```
AddBuff(PlayerState target, ApplyBuffCommand command, IBuffConfigProvider configProvider)
```

内部流程：
1. 查配置 → 有配置用 OverlayType；无配置时按 Stackable 桥接规则（见决策 2）
2. 互斥检查 → MutexGroupId != 0 时扫描同组 Buff，比较 Priority（同帧同优先级由命令队列排序决定，见决策 4）
3. 叠加规则 → 按 OverlayType 分支处理（Independent / Stack / Refresh），Refresh/Stack 不改 AppliedFrame（见决策 6）
4. NumericModifier 联动 → 叠层变更时 RemoveBySource + 重新添加
5. Recalculate → 最终调用 target.Numeric.Recalculate(target)

新增辅助方法：
- `FindBuffByBuffId(List<BuffState>, int buffId)` → 返回索引
- `FindBuffsByMutexGroup(List<BuffState>, int mutexGroupId)` → 收集匹配索引
- `ApplyBuffEffects(PlayerState, long runtimeBuffId, BuffConfig, int stackCount)` → 按 StackCount 倍增添加 modifier
- `RemoveBuffInternal(PlayerState, int index)` → 移除指定位置的 Buff + 清理 modifier

### Step 3：服务端注入 IBuffConfigProvider

**文件**：`GameServer/Server/Entity/Battle/BattleLogic.cs`

- 构造函数创建 DefaultBuffConfigProvider
- ProcessBuffCommands 调用新重载 AddBuff(target, command, _buffConfigProvider)

### Step 4：客户端注入 IBuffConfigProvider

**文件**：`GameLogic/Battle/BattleSimulation.cs` 或 `BattleClientController.cs`

- 创建与服端相同的 DefaultBuffConfigProvider 实例
- 客户端本地预测 AddBuff 时传入 configProvider

### Step 5：SnapshotSelfTestSuite 新增 7 个用例

**文件**：`GameShared/FrameSync/Snapshot/SnapshotSelfTestSuite.cs`

| 用例 | 验证点 |
|------|--------|
| stack-overlay-roundtrip | 叠 3 层 → StackCount=3, 属性正确 → 到上限再叠仅刷新时间 → 快照往返一致 |
| refresh-overlay-roundtrip | 施加 → 10帧后刷新 → RemainingFrames 恢复 → AppliedFrame 不变 → 快照往返一致 |
| mutex-replace-roundtrip | 低优先级被高优先级替换 → 旧 Buff modifier 清理 → 快照往返一致 |
| mutex-reject-roundtrip | 高优先级在位，低优先级被拒绝 → 属性不变 |
| mutex-same-priority | 同优先级新替旧 → 只有后消费的 Buff 存在 |
| mutex-same-frame-two-commands | 同帧同目标两个互斥 Buff（buffId=201 vs 202，同组同优先级）→ BuffId 大的赢（202 赢）→ hash 一致 |
| stackable-bridge-upgrade | 无配置但 Flags=Stackable → 进入 Stack 语义（受控升级）→ 不再静默回退 Independent |

### Step 6：自动化测试框架扩展

**文件**：`GameLogic/Battle/Automation/BattleAutomationRuntime.cs`

新增 3 个 BattleAutomationScenarioKind：
- `buff-stack`：连续施加 3 次同 BuffId（Stack 模式），验证 StackCount 和属性
- `buff-refresh`：施加后 20 帧再次施加（Refresh 模式），验证时间刷新
- `buff-mutex`：先施加低优先级再施加高优先级，验证替换

同步扩展：
- `BattleAutomationScenarioPlan.Create` 新增 case 分支
- `BuiltinBattleAutomationBridge.Evaluate` 新增判定逻辑
- `BattleAutomationServerConfig` 新增环境变量配置

### Step 7：验收指南

**文件**：`Tools/AutomationAcceptance/战斗Buff系统-Buff规则验收测试指南-<日期>.md`

---

## Luban 配置表预留结构

v0.2a 不接入 Luban，但锁定表结构供后续使用：

**表名**：`TbBuffConfig`

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | int | BuffId（主键） |
| OverlayType | int | 0=Independent, 1=Stack, 2=Refresh |
| MaxStack | int | 最大层数，默认 1 |
| MutexGroupId | int | 互斥组 ID，0=无 |
| Priority | int | 互斥组内优先级 |
| DurationFrames | int | 默认持续帧数 |
| Flags | int | BuffFlags 位掩码 |
| Effects | list\<BuffEffect> | 效果列表 |

**子结构 BuffEffect**：

| 字段 | 类型 | 说明 |
|------|------|------|
| AttributeKind | int | 1=Health, 2=MaxHealth, ... |
| ValueType | int | 0=Flat, 1=Percent |
| Value | int | 效果值 |

---

## 不需要修改的文件

| 文件 | 原因 |
|------|------|
| `BuffState.cs` | 已有 StackCount、WithStackCount、WithRemainingFrames，无需变动 |
| `PlayerState.cs` | ActiveBuffs / Numeric / NextRuntimeBuffId 已完备 |
| `PlayerStateSnapshot.cs` | 快照结构不变 |
| `StateHasher.cs` | 已覆盖 StackCount、RemainingFrames，新规则通过修改这些字段间接生效 |
| `NumericState.cs` | AddModifier/RemoveBySource/Recalculate 已完备 |
| `BattleWorldState.cs` | 快照/恢复逻辑不变 |

---

## 完成标准

1. BuffOverlayType 三种模式全部实现并通过 SnapshotSelfTestSuite 单元测试
2. 互斥组 Priority 比较确定性正确（高替低、同帧同优先级由命令队列排序决定、低被拒绝）
3. 叠层 NumericModifier 倍增正确（StackCount × Value）
4. Refresh/Stack 操作不改 AppliedFrame（字段不可变规则）
5. Stackable 桥接规则正确（无配置 && Stackable → Stack 模式）
6. SnapshotSelfTestSuite 7 个新用例全部 pass（含同帧双命令互斥 + Stackable 桥接升级）
7. 双客户端 3 个自动化场景 report.json 显示 PASS
8. StateHasher 快照哈希在叠层/互斥操作后双端一致
9. DefaultBuffConfigProvider 注入双端，配置一致

---

## 风险与应对

| 风险 | 影响 | 应对 |
|------|------|------|
| 叠层时 modifier 残留 | 属性计算错误 | 统一走 RemoveBySource → 重新添加模式 |
| 互斥替换后旧 Buff 效果未清理 | 属性值不对 | 移除旧 Buff 时同步 RemoveBySource + Recalculate |
| 同帧多 Buff 互斥判定顺序不确定 | 双端不一致 | 由命令队列排序键 (FrameIndex, TargetId, BuffId) 决定消费顺序（见决策 4） |
| DefaultBuffConfigProvider 双端配置不一致 | hash 不匹配 | 配置代码放在 GameShared 程序集，双端共享 |
| 旧代码使用 BuffFlags.Stackable 表达叠层 | 无配置回退 Independent 导致语义退化 | 桥接规则：无配置 && Stackable → 按 Stack 解释（见决策 2） |
| Refresh/Stack 重置 AppliedFrame | 排序和 hash 不一致 | AppliedFrame 创建后不可变（见决策 6） |

---

## 验证方式

**确定性验证**：StateHasher 在叠加/互斥操作后双端 hash 一致

**工程验证**：
1. 无头 CLI：`SnapshotSelfTestSuite` 7 个新用例 pass
2. 双客户端：`Run-BattleAcceptance.ps1 -Scenario 'buff-stack','buff-refresh','buff-mutex'`
3. report.json `overallResult: "PASS"`

---

## 实现完成记录（2026-05-24）

### 已落地实现

- `BuffSystem` 已接入 `Independent / Stack / Refresh / MutexGroup` 规则引擎
- `IBuffConfigProvider + DefaultBuffConfigProvider` 已双端注入
- `Stackable` 无配置桥接、`AppliedFrame` 不可变、互斥整组判定已实现
- `SnapshotSelfTestSuite` 已补齐 v0.2a 规则用例
- 自动化框架已补齐 `buff-stack / buff-refresh / buff-mutex` 场景代码

### 本次已通过验证

| 类别 | 场景 | 结果 |
|------|------|------|
| 编译 | 服务端编译 | PASS |
| 编译 | 客户端编译 | PASS |
| 代码级自测 | `snapshot-self` | PASS |
| 代码级自测 | `skill-trigger-buff` | PASS |
| 代码级自测 | `prediction-self` | PASS |
| 代码级自测 | `buff-roundtrip` | PASS |
| 代码级自测 | `skill-buff-roundtrip` | PASS |
| 代码级自测 | `authoritative-snapshot-restores-player-buffs` | PASS |
| Unity 双端 | `two-client-buff-lifecycle` | PASS |
| Unity 双端 | `two-client-skill-buff` | PASS |

### 备注

- 中途遇到 2 个环境问题：ParrelSync clone 冷启动超时、旧服务端 20101 端口占用
- 两个问题均为环境问题，清理并重跑后通过，不属于 Buff 逻辑缺陷
