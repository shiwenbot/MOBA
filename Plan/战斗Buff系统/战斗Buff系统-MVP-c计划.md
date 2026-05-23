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

## 实现步骤

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
- 从 Properties 读取 TargetId（或用 context.TargetId）、BuffId、RuntimeBuffId
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
- 在 SkillContext 构建时，设置 BuffCommandSink = this（BattleLogic / BattleSimulation 都已实现 IBuffCommandSink）
- 确保 SkillGraph 在 Tick 内同步执行（不走异步），保证帧对齐

**验证**：无头 CLI 自测 + 真实客户端验收测试

---

### Step 5：编辑器节点

**文件**：
- `Assets/Editor/SkillGraph/Nodes/` — 新增 ApplyBuffNode.cs / RemoveBuffNode.cs / BuffConditionNode.cs
- `Assets/Editor/SkillGraph/SkillGraphNodeFactory.cs` — 注册新节点
- `Assets/Editor/SkillGraph/Nodes/SkillNodeTypes.cs` — 枚举扩展

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

| 风险 | 影响 | 应对 |
|---|---|---|
| SkillGraph 在 Tick 内同步执行超时 | 仿真卡顿 | SkillGraph 执行是单帧步进，耗时可控；若超时则 MaxExecutionSteps 兜底 |
| Lockstep/LocalOnly 判断遗漏 | LocalOnly 模式下误入队命令 | 新节点处理器统一在入口做 IsLockstepMode 检查 |
| BuffCondition 读取瞬态一致性 | 条件判断和命令入队不在同一帧 | 条件判断在 Tick 内执行，读到的是本帧初状态，命令在后续帧消费——可接受 |
| 编辑器节点序列化兼容 | 旧图不包含新节点 | 新节点是可选的，旧图打开不受影响 |
