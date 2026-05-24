# 输入缓冲区-InputBuffer 计划

## Context

参考 [hakiSheep 的 InputBuffer 设计模式](https://gitee.com/hakiSheep/hakisheep--DesignMode/tree/master/%E7%BC%93%E5%86%B2%E5%8C%BA%E7%90%86%E5%BF%B5-InputBuffer)，为项目引入操作手感层的输入缓冲机制。

**当前现状**：`BattleClientController` 用 `_queuedSkillId` 做技能输入缓存——`Update` 中 `GetKeyDown` 设值，`Tick` 中一次性读取并清零。这是一个 1 帧窗口的即时消费模式。

**v1 目标**：构建通用的 `InputBuffer<TKey, TValue>` 容器，替换 `_queuedSkillId`，为后续"按仿真状态条件延迟消费"打基础。v1 行为与现有逻辑等价（下个 Tick 即消费），不改帧语义。

**v1 不提供**：手感容错窗口。真正的容错价值需要 v2——当 `PlayerState` 具备确定性"可释放技能"判定后，InputBuffer 才能按条件延迟消费。在此之前强行加容错窗口，会让 skillId 关联到错误的帧号，破坏回滚/重放一致性。

## 阶段状态

- 状态：`待实现`
- 前置依赖：无

---

## 目标

1. 实现通用 `InputBuffer<TKey, TValue>` 纯 C# 容器，支持单一缓冲 + 队列缓冲，帧数驱动衰减
2. 替换 `_queuedSkillId` 为 InputBuffer 实例，行为与现有逻辑等价
3. 单元测试覆盖：录入/消费/衰减/过期/打断/队列 FIFO

## 非目标

- 不改移动输入（`_cachedDx/_cachedDy` 是连续态，不适合 one-shot buffer）
- 不改 `InputBufferTuning`（网络预测帧配置，与操作手感层无关）
- 不改帧同步协议、服务端逻辑、`BattleSimulation.Tick()` 签名
- 不实现"条件消费"（需要 PlayerState 具备确定性可释放判定，属于 v2）
- 不实现连招/Combo 匹配

---

## 帧语义约束

这是本计划最关键的设计约束。

`BattleSimulation` 中 skillId 是**每帧一次性命令**：
- `Tick()` 将 `(dx, dy, skillId)` 写入 `_inputHistory[frameIndex]`（`:563`）
- 回滚重播时 `ApplyLocalPrediction()` 从历史按帧恢复（`:554`）
- 服务端按帧号校验输入（`C2B_PlayerInput.FrameIndex`）

因此 **InputBuffer 不能改变 skillId 与帧号的关联关系**。在 v1 中：
- 缓冲在 Update 录入，在 Tick 消费
- 消费时机 = 下一个 Tick（与现有 `_queuedSkillId` 行为一致）
- skillId 关联的帧号不变

v2 的"条件消费"需要额外设计：
- 消费条件必须来自**仿真状态**（PlayerState / BattleWorldState），而非 Unity 表现层（动画状态、MonoBehaviour）
- 条件判定必须确定性、可回滚、同帧可重现
- 延迟消费意味着 skillId 关联到更晚的帧号，需要确认服务端和回滚逻辑都能正确处理

---

## 自动化输入兼容

现有 `IBattleAutomationInputSource` 可在指定帧精确注入技能请求（`:137`-`:150`）。优先级：

1. **Automation 输入 > 手动缓冲输入**
2. Automation `TryGetSkillRequest` 命中时，直接使用 automation 值，不从 InputBuffer 消费
3. Automation 未命中时，才从 InputBuffer `TryConsume`
4. InputBuffer 中未消费的输入保留到下个 Tick（不被 automation 覆盖丢弃）

这样保证：
- 自动化验收仍按指定帧精确触发
- 手动输入不会因为 automation 未注入而被意外丢弃

---

## 实现步骤

### Step 1：创建 `InputBuffer<TKey, TValue>`

**文件**：`UnityProject/Assets/GameScripts/HotFix/GameShared/InputBuffer/InputBuffer.cs`

纯 C# 类，不依赖 Unity。核心结构：

- `Slot<TValue>`：`T Value` + `int RemainingFrames` + `bool IsActive`
- 单一缓冲：`Record(key, value, frames)` / `TryConsume(key, out value)` / `Has(key)`
- 队列缓冲：`Enqueue(key, value, frames)` / `TryDequeue(key, out value)`
- `TickDecay()`：递减所有槽/队列头的 remainingFrames，过期移除
- `Interrupt(key)` / `Clear()`

### Step 2：单元测试

**文件**：`UnityProject/Assets/GameScripts/HotFix/GameShared/Tests/InputBuffer/InputBufferTests.cs`

覆盖：
- Record → TryConsume 基本流程
- TickDecay 衰减与自动过期
- 过期后 TryConsume 返回 false
- 多次 Record 同一 key 只保留最新
- 队列 Enqueue → TryDequeue FIFO 顺序
- 队列最大长度溢出时丢弃最旧
- Interrupt 清除指定 key
- Clear 清除全部
- 空缓冲 TryConsume/TryDequeue 返回 false

### Step 3：集成到 `BattleClientController`

**文件**：`UnityProject/Assets/GameScripts/HotFix/GameLogic/Battle/BattleClientController.cs`

改动范围（最小化）：
- 新增 `InputBuffer<int, int>` 成员（key=输入类型枚举值，value=skillId）
- `Update()` 中：`_queuedSkillId = ...` 改为 `_inputBuffer.Record(SkillInput, skillId, BufferFrames)`
- `Tick()` 中技能输入读取逻辑改为：
  1. `_inputBuffer.TickDecay()`
  2. 先检查 automation → 有则用 automation 值
  3. automation 未命中 → `_inputBuffer.TryConsume(SkillInput, out int skillId)`
- 删除 `_queuedSkillId` 字段
- `BufferFrames` 暂定为 1（与现有行为等价）

### Step 4：验证

1. **编译通过**
2. **单元测试全部通过**（Step 2 的测试用例）
3. **回滚一致性**：运行现有帧同步场景，确认回滚后不会重复放技能或漏放技能（skillId 关联的帧号未变）
4. **自动化验收兼容**：运行现有自动化测试，确认 automation 注入仍按指定 frame 精确触发
5. **手动测试**：按键放技能，行为与改动前一致

---

## 关键文件

| 文件 | 操作 |
|------|------|
| `UnityProject/Assets/GameScripts/HotFix/GameShared/InputBuffer/InputBuffer.cs` | 新建 |
| `UnityProject/Assets/GameScripts/HotFix/GameShared/Tests/InputBuffer/InputBufferTests.cs` | 新建 |
| `UnityProject/Assets/GameScripts/HotFix/GameLogic/Battle/BattleClientController.cs` | 修改（集成） |

## 与原版 InputBuffer 的差异

| 维度 | 原版（hakiSheep） | 本实现 |
|------|-------------------|--------|
| 基类 | MonoBehaviour + 单例 | 纯 C# 类，实例化 |
| 时间单位 | 秒（Time.deltaTime） | 帧数（30Hz 固定帧） |
| 泛型 | 枚举 key + 固定 bool 值 | 泛型 `TKey, TValue` |
| 更新驱动 | Unity Update | 逻辑 Tick |
| 依赖 | Odin Inspector / Sirenix | 无外部依赖 |
| 消费条件 | bool Buffered 标记 | v1 无条件（下帧消费）；v2 仿真状态判定 |

## 参考资源

原始 InputBuffer 实现已 clone 到本地，实现 agent 可直接访问：

- **参考源码**：`Plan/输入缓冲区-参考/InputBuffer.cs`（hakiSheep 原版实现）
- **参考测试**：`Plan/输入缓冲区-参考/Test/PlayerJump.cs`（原版使用示例）
- **原仓库**：https://gitee.com/hakiSheep/hakisheep--DesignMode/tree/master/%E7%BC%93%E5%86%B2%E5%8C%BA%E7%90%86%E5%BF%B5-InputBuffer

原版是 MonoBehaviour + 单例 + 秒级窗口，本实现改为纯 C# + 帧数窗口，思路参考但架构不同。

## v2 路线（本计划不含，仅记录）

当 `PlayerState` 具备确定性"可释放技能"判定后：
- InputBuffer `BufferFrames` 从 1 提升到 4~6（~133~200ms 容错窗口）
- 消费条件改为"仿真状态判定可释放"
- 需要设计：延迟消费时 skillId 关联帧号的处理、服务端对延迟技能命令的容忍度
