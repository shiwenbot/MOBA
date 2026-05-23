# 战斗Buff系统-MVP-c计划

## 目标

打通技能触发 Buff 的端到端闭环：SkillGraph 节点通过 `IBuffCommandSink` 发出 Buff 命令，命令走帧对齐管线消费，双端执行结果一致。

## 前置依赖

- **v0.0a**（已完成）：BuffSystem + NumericState + DamageSystem + IBuffCommandSink + 快照/协议扩展
- **技能节点编辑器 v0.8-A**（已完成）：SkillGraphRunner Step/Snapshot/Restore 内核

## 范围

1. **SkillContext 扩展**：暴露 `IBuffCommandSink` 给节点处理器
2. **新增运行时节点处理器**：
   - `ApplyBuffNodeHandler`：通过 IBuffCommandSink.EnqueueApplyBuff 发命令
   - `RemoveBuffNodeHandler`：通过 IBuffCommandSink.EnqueueRemoveBuff 发命令
   - `BuffConditionNodeHandler`：通过 IBuffCommandSink.HasBuff / GetBuffStackCount 做条件判断
3. **编辑器节点**：ApplyBuff、RemoveBuff、BuffCondition 三个可视化节点
4. **服务端集成**：Tick 中 SkillGraph 执行时传入 BattleLogic（IBuffCommandSink 实现）
5. **客户端集成**：Tick 中 SkillGraph 执行时传入 BattleSimulation（IBuffCommandSink 实现）
6. **Lockstep 约束**：三个新节点只在 Lockstep 模式下生效，LocalOnly 模式下跳过命令入队

## 非目标

- 叠加/互斥/驱散规则（v0.2a）
- Buff 数值效果验证（已有 DamageSystem 覆盖）
- Luban 配置表接入
- 表现层（BuffViewManager 等）

---

## 已锁定的关键架构决策

### 决策 1：Buff 操作走 IBuffCommandSink，不走 ISkillRuntimeServices

ISkillRuntimeServices 是偏表现层的异步接口（Log、DelayAsync、PlayAnimationAsync）。Buff 命令是帧对齐的确定性操作，走独立的 IBuffCommandSink。这与 v0.0a 已锁定决策一致。

### 决策 2：SkillContext 新增 BuffCommandSink 属性

SkillContext 已有 CasterId / TargetId / Blackboard / Runtime。新增 `IBuffCommandSink? BuffCommandSink` 属性，由 Tick 驱动方（BattleLogic / BattleSimulation）在创建 SkillContext 时注入。

### 决策 3：新节点只在 Lockstep 模式下入队命令

Lockstep 模式：节点处理器正常调用 IBuffCommandSink 入队命令。
LocalOnly 模式：节点处理器直接跳过（不执行任何 Buff 操作），因为 Buff 是服务端权威的。

---

## 前置契约（开工前必须钉死）

> 以下 4 条契约由 Codex cross-agent review 提出，经代码验证确认。实现前必须达成一致，实现时不可违反。

### 契约 1：Tick 阶段执行顺序与 FrameIndex 语义

**现状**（已验证代码）：
- 服务端 `BattleLogic.Tick`（`:142`）：`ProcessBuffCommands → 输入处理/移动 → ApplyBuffTicks`
- 客户端 `BattleSimulation.ApplyLocalPrediction`（`:617`）：`ProcessBuffCommands → 移动/物理 → ApplyBuffTicks`

**契约**：
- SkillGraph 在 **ProcessBuffCommands 之前** 执行（即 Tick 最开头）
- SkillGraph 执行产生的 Buff 命令，`FrameIndex = 当前 Tick 的 frameIndex`
- 同帧内 `ProcessBuffCommands` 消费这些命令，保证"SkillGraph 在帧 N 发出 → 帧 N 消费"，不延迟到帧 N+1
- 如果后续需要改为"帧 N 发出 → 帧 N+1 消费"，必须**同时改服务端和客户端**的执行顺序

**违反后果**：客户端预测与服务端权威帧语义不一致，导致回滚/不同步

### 契约 2：Lockstep 技能图预加载，禁止 Tick 内资源加载

**现状**（已验证代码）：
- `SkillExecutor.CastSkill`（`:26`）中 `await LoadSkillGraph` 走异步加载
- 底层还会走 `LoadAssetAsync` / `File.ReadAllText`

**契约**：
- Lockstep 模式下，SkillGraph **必须在战斗开始前预加载并缓存**，Tick 内只做同步查表取图
- 新增 `SkillGraphCache` 或复用现有缓存机制，战斗初始化阶段加载所有参战技能图
- Tick 内调用路径禁止出现 `await`、`LoadAssetAsync`、`File.ReadAllText` 等异步/IO 操作
- 服务端同样需要能在启动时加载技能图（JSON 文件或内嵌资源），Tick 内同步取用

**违反后果**：Tick 内异步操作破坏确定性，服务端无法同步执行

### 契约 3：RemoveBuffNode 语义 — MVP 只支持按 BuffId 全删

**现状**（已验证代码）：
- `RemoveBuffCommand` 同时支持 `RuntimeBuffId`（精确删一个）和 `BuffId`（删同 Id 全部实例）
- `BuffSystem.RemoveBuff`（`:108`）：`runtimeBuffId > 0` 精确删，`runtimeBuffId <= 0` 按 buffId 全删
- `RuntimeBuffId` 是运行时动态分配的，SkillGraph 静态属性无法预先知道

**契约**：
- MVP 阶段 `RemoveBuffNode` 只暴露 `BuffId` 属性，`RuntimeBuffId` 留空（`<= 0`）
- 语义为"移除目标身上所有该 BuffId 的实例"
- 后续版本可通过"ApplyBuff 节点把 runtimeBuffId 写回 blackboard/变量"来支持精确删除
- 编辑器节点的 tooltip/描述必须明确说明"移除所有同类型 Buff 实例"

**违反后果**：图作者误以为精确删一个，实际删了全部，造成难以排查的逻辑 bug

### 契约 4：Lockstep 图中依赖 Runtime 的节点必须隔离

**现状**（已验证代码）：
- `DebugNodeHandler`（`SkillHandlers.cs:34`）直接调用 `context.Runtime.Log(...)`
- `ActionNodeHandler` 调用 `context.Runtime.PlayAnimationAsync(...)`
- `DelayNodeHandler` 调用 `context.Runtime.DelayAsync(...)`
- 服务端 BattleLogic 只注入 `IBuffCommandSink`，不注入 `ISkillRuntimeServices`

**契约**：
- 服务端必须注入一个 no-op `ISkillRuntimeServices`（Log 为空实现、DelayAsync 立即返回 true、PlayAnimationAsync 立即返回 true），防止空引用崩溃
- 或者：Lockstep 图导出/加载阶段做节点校验，标记 `Debug` / `Action` / `Delay` 为"仅 LocalOnly"，若 Lockstep 图中包含这些节点则导出警告/报错
- MVP 选其一实现，推荐前者（注入 no-op）更简单

**违反后果**：包含 Debug/Action/Delay 节点的 Lockstep 图在服务端 Tick 直接崩溃（NullReferenceException）

---

## 实现步骤

### Step 0：验证前置契约可实现性

**目的**：确认 Tick 执行顺序调整和预加载机制不会破坏现有测试

**思路**：
- 在 `BattleLogic.Tick` 中验证"SkillGraph 执行点在 ProcessBuffCommands 之前"是否可行（需要确认 SkillGraph 是否需要先读输入）
- 确认 `SkillGraphCache` 的缓存 key 设计（skillName → RuntimeSkillGraph）
- 确认服务端加载路径（直接从 JSON 文件读取，不走 YooAsset）

**验证**：现有无头 CLI 测试全部 PASS（回归）

---

### Step 1：SkillContext 扩展 + 注册新处理器类型

**文件**：
- `GameShared/SkillGraph/SkillContext.cs` — 新增 `IBuffCommandSink? BuffCommandSink` 属性
- `GameShared/SkillGraph/RuntimeSkillGraph.cs` — RuntimeNodeTypes 新增 `ApplyBuff` / `RemoveBuff` / `BuffCondition`
- `GameShared/SkillGraph/SkillHandlers.cs` — RegisterDefaults 注册三个新处理器

**思路**：
- SkillContext 加一个可空属性，不破坏现有构造逻辑
- RuntimeNodeTypes 是静态常量类，新增三个字符串常量

**验证**：编译通过，现有 SkillGraph 测试不受影响

---

### Step 2：实现 ApplyBuffNodeHandler 和 RemoveBuffNodeHandler

**文件**：
- `GameShared/SkillGraph/SkillHandlers.cs`（或新文件 `BuffSkillHandlers.cs`）

**ApplyBuffNodeHandler 思路**：
- 从 RuntimeSkillNode.Properties 读取 BuffId、DurationFrames、StackCount
- CasterId / TargetId 从 SkillContext 获取
- 构建 ApplyBuffCommand，调用 BuffCommandSink.EnqueueApplyBuff()
- Lockstep 检查：非 Lockstep 模式直接返回 Success，不入队

**RemoveBuffNodeHandler 思路**：
- 从 Properties 读取 TargetId（或用 context.TargetId）、BuffId
- **不暴露 RuntimeBuffId**（遵守契约 3），RuntimeBuffId 留 0，语义为"按 BuffId 全删"
- 构建 RemoveBuffCommand，调用 BuffCommandSink.EnqueueRemoveBuff()
- Lockstep 检查同上

**验证**：编译通过 + 无头 CLI 自测（见下方验收流程）

---

### Step 3：实现 BuffConditionNodeHandler

**文件**：同 Step 2

**思路**：
- 从 Properties 读取 BuffId、TargetId
- 调用 BuffCommandSink.HasBuff() 或 GetBuffStackCount()
- 根据条件结果决定是否执行后续节点（通过 SkillExecuteResult 控制流向）
- Lockstep 检查：非 Lockstep 模式下，条件判断仍可执行（只读操作）

**验证**：编译通过 + 无头 CLI 自测

---

### Step 4：双端 Tick 集成

**文件**：
- `GameServer/Server/Entity/Battle/BattleLogic.cs` — SkillGraph 执行时注入 IBuffCommandSink（this）
- `GameLogic/Battle/BattleSimulation.cs` — 同上（this）

**思路**：
- **Tick 执行顺序**（遵守契约 1）：SkillGraph 在 ProcessBuffCommands 之前执行，发出的命令本帧消费
- **预加载**（遵守契约 2）：战斗初始化阶段预加载所有 SkillGraph，Tick 内同步取用
- **触发时机**：输入命令中携带 `skillId`，BattleLogic/BattleSimulation 在对应帧消费输入时解析出技能请求，从缓存取图并同步执行。具体路径：
  - 玩家输入 → `PendingInput` 扩展 `skillId` 字段（或复用已有技能状态机驱动）
  - Tick 消费输入时检查是否有技能请求 → 有则查 `SkillGraphCache` 取图 → 创建 SkillContext（注入 BuffCommandSink）→ 同步执行 SkillGraphRunner.Step
  - 服务端和客户端使用**同一条触发路径**：输入驱动，帧号对齐
- 在 SkillContext 构建时，设置 BuffCommandSink = this（BattleLogic / BattleSimulation 都已实现 IBuffCommandSink）
- 服务端注入 no-op `ISkillRuntimeServices`（遵守契约 4），防止 Debug/Action/Delay 节点空引用
- 确保 SkillGraph 在 Tick 内同步执行（不走异步），保证帧对齐

**验证**：无头 CLI 自测 + 真实客户端验收测试

---

### Step 5：编辑器节点 + Exporter 注册

**文件**：
- `Assets/Editor/SkillGraph/Nodes/` — 新增 ApplyBuffNode.cs / RemoveBuffNode.cs / BuffConditionNode.cs
- `Assets/Editor/SkillGraph/SkillGraphNodeFactory.cs` — 注册新节点
- `Assets/Editor/SkillGraph/Nodes/SkillNodeTypes.cs` — 枚举扩展
- `Assets/Editor/SkillGraph/Export/SkillGraphExporter.cs` — **必须同步补**：节点类型映射、属性校验、端口白名单（`IsValidInputPort` / `IsValidOutputPort`）、lockstep 风险分析标记

**思路**：
- 每个编辑器节点定义自己的属性面板（BuffId、DurationFrames 等）
- 拖拽到画布后可编辑属性
- 构建时序列化为 RuntimeSkillNode 的 Properties

**验证**：Unity Editor 中可拖拽新节点、编辑属性、保存/加载

---

### Step 6：新增验收场景 + 自测用例

**文件**：
- `GameServer/Server/Entity/TestHarness/TestRunner.cs` — 新增 `skill-trigger-buff` 自测场景
- `GameServer/Server/Entity/Battle/BattleAutomationServerConfig.cs` — 新增 `two-client-skill-buff` 场景配置
- `Assets/StreamingAssets/BattleAutomation/Puerts/` — 新增 `two-client-skill-buff.js.txt` Puerts 脚本
- `GameShared/FrameSync/Snapshot/SnapshotSelfTestSuite.cs` — 新增 `skill-buff-roundtrip` 快照往返测试

**思路**：
- 无头场景：构建含 ApplyBuff 节点的 SkillGraph，Tick 执行后验证 Buff 出现在目标 PlayerState
- 真实客户端场景：服务端通过 SkillGraph 给玩家施加 Buff，两个客户端观察 Buff 出现和消失
- 快照往返：Skill 执行态 + Buff 状态一起 Snapshot/Restore 后继续执行，结果一致

**验证**：见下方验收流程

---

## 验收流程（双层强制验收）

每个 Step 完成后，必须通过以下两层验收才能标记为完成：

### Layer 1：无头 CLI 逻辑验收

```bash
# 在 GameServer 目录运行
dotnet run --project GameServer/Server/Main -- --mode=test --scenario buff-roundtrip
dotnet run --project GameServer/Server/Main -- --mode=test --scenario buffs-affect-hash
dotnet run --project GameServer/Server/Main -- --mode=test --scenario runtime-buff-id-roundtrip
dotnet run --project GameServer/Server/Main -- --mode=test --scenario skill-trigger-buff
dotnet run --project GameServer/Server/Main -- --mode=test --scenario skill-buff-roundtrip
```

**通过标准**：退出码为 0，所有场景 PASS

### Layer 2：真实客户端 Unity Editor 验收

```powershell
# Buff 生命周期（已有场景，回归验证）
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -Scenario 'two-client-buff-lifecycle' -InteractiveEditor

# 技能触发 Buff（新增场景）
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -Scenario 'two-client-skill-buff' -InteractiveEditor
```

**通过标准**：
- PowerShell 退出码为 0
- `combined-report.json` 中两个客户端 `Passed = true`
- 两个客户端都能观察到技能触发后 Buff 出现
- Buff 到期后两个客户端都能观察到 Buff 消失

### Layer 3：编辑器手动冒烟（仅 Step 5）

- 在 Unity Editor SkillGraph 窗口中拖入 ApplyBuff / RemoveBuff / BuffCondition 节点
- 编辑属性后保存
- 重新加载后属性不丢失

---

## 完成标准

1. ApplyBuffNodeHandler / RemoveBuffNodeHandler / BuffConditionNodeHandler 实现并通过编译
2. SkillContext 正确暴露 IBuffCommandSink，双端 Tick 注入无误
3. Lockstep 模式下命令正确入队并按帧消费
4. 编辑器可拖拽新节点、编辑属性、保存/加载
5. **Layer 1 无头 CLI 验收全部 PASS**（含新增 skill-trigger-buff 和 skill-buff-roundtrip）
6. **Layer 2 真实客户端验收 PASS**（two-client-buff-lifecycle 回归 + two-client-skill-buff 新场景）
7. 快照往返测试通过：Skill 执行态 + Buff 状态一起恢复后继续执行，哈希一致

---

## 风险与应对

| # | 风险 | 严重度 | 影响 | 应对 |
|---|---|---|---|---|
| 1 | SkillGraph 在 Tick 内同步执行超时 | 中 | 仿真卡顿 | SkillGraph 执行是单帧步进，耗时可控；若超时则 MaxExecutionSteps 兜底 |
| 2 | Lockstep/LocalOnly 判断遗漏 | 中 | LocalOnly 模式下误入队命令 | 新节点处理器统一在入口做 IsLockstepMode 检查 |
| 3 | BuffCondition 查询瞬态语义不清 | 中 | 图作者困惑："图看起来对，跑起来永远 False" | **作者规则**：`HasBuff` / `GetBuffStackCount` 查询的是当前 world state 中已生效的 Buff，**不包含**同一帧内前面节点刚入队但尚未消费的 pending command。即：同一张图里先 ApplyBuff 再 BuffCondition，后者读不到前者刚加的 Buff |
| 4 | 编辑器节点序列化兼容 | 低 | 旧图不包含新节点 | 新节点是可选的，旧图打开不受影响 |
| 5 | Exporter 未注册新节点类型 | 高 | 编辑器能拖拽但导出报错 `Unsupported runtime node type` | Step 1 必须同步补：节点类型映射、属性校验、端口白名单（`IsValidInputPort` / `IsValidOutputPort`）、lockstep 风险分析标记 |
| 6 | ~~Debug 节点在服务端空引用~~ | — | — | **已升级为契约 4**，不再作为风险跟踪 |
