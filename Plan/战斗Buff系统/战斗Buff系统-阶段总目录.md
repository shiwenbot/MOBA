# 战斗Buff系统-阶段总目录
## Context

当前 TEngine 已有：
- `状态帧同步` 版本线（已锁定 30Hz、固定 dt、确定性验证要求）
  - `ServerTickDriver` / `ClientTickDriver`：30Hz 帧驱动
  - `BattleLogic` / `BattleSimulation`：服务端权威 + 客户端预测
  - `C2B_PlayerInput` / `S2C_FrameSnapshot`：协议已通
  - `SnapshotManager` + `SnapshotBuffer`：快照机制
  - `MoveSystem`：纯函数移动逻辑（GameShared）
- `技能节点编辑器 v0.8` 的 Step/Snapshot/Restore 运行时内核
  - `SkillGraphRunner`：帧步进执行
  - `ISkillNodeHandler` + `SkillNodeHandlerRegistry`：节点扩展机制
  - `SkillContext`：CasterId / TargetId / Blackboard
  - `Lockstep` / `LocalOnly` 同步模式

当前缺口在于：战斗域（属性、伤害、Buff 生命周期、技能触发到 Buff、生效结果同步与回滚）仍未形成可联调闭环。

本文件作为战斗 Buff 版本线总目录，只定义阶段目标、边界、依赖和验收口径。

> 版本线说明：`战斗Buff系统 v0.x` 与 `状态帧同步 v0.x` 并行推进。
> 其中 `技能节点编辑器 v0.8-A（Step 内核）` 是本版本线关键前置依赖。

### 参考仓库分析结论（NKGMobaBasedOnET）

1. 战斗主链路是 `技能输入 -> 命中上下文 -> Add/Remove Buff -> BuffManager Tick -> 属性变化/伤害结算 -> 帧末脏同步`。
2. Buff 核心实现是 `BuffFactory + IBuffSystem/ABuffSystemBase + BuffTimerAndOverlayHelper + BuffManagerComponent`。
3. 同步主链路是 `LSF Tick + 帧命令 + Buff/属性同步命令 + 一致性检查 + 回滚重播`。
4. 可直接借鉴：Buff 状态机、叠加/刷新 helper、全量快照+差分同步、命令分发+Ticker 两层抽象。
5. 需要补强：死亡/驱散策略矩阵、Buff 运行时实例 ID、统一快照域（Skill + Buff + Attribute）。

### 已锁定的技术决策

1. **逻辑帧率固定**：30Hz（dt = 33.333ms）。
2. **追帧硬约束**：只增加单渲染帧内 Tick 次数，不允许修改仿真 dt。
3. **网络与异步**：Fantasy + FTask；Luban 仅用于配置，不用于运行时帧消息。
4. **战斗时间语义**：Buff 持续时间、CD、延迟均以帧计时，不依赖 wall clock。
5. **技能触发约束**：Lockstep 模式下 Action 节点只发命令，不直接驱动非确定性表现。
6. **同步模型**：服务端权威 + 客户端预测 + 一致性检查 + 回滚重播。
7. **Buff 状态融入快照**：Buff 状态作为 PlayerState 的一部分，随 `S2C_FrameSnapshot` 一起广播。MVP 阶段使用全量快照（30Hz，~30 KB/s/客户端），后续 v0.4a 引入增量快照优化带宽（目标 ~3-5 KB/s/客户端）。
8. **Buff 实例标识**：引入 RuntimeBuffId，禁止仅靠 BuffId 区分运行时实例。
9. **快照范围**：快照必须覆盖 Skill 执行态、Buff 运行态、属性容器三域。
10. **BuffSystem 纯函数**：Buff 逻辑放在 `GameShared/FrameSync/Battle/`，客户端和服务端共用同一份确定性代码。
11. **命令化副作用**：SkillGraph 节点通过 `ISkillRuntimeServices.ApplyBuff()` 发出命令，不直接操作 BuffSystem。
12. **验证要求**：每阶段必须有确定性验证，不可后置。

### 设计原则

1. **先跑闭环再补复杂度**：优先打通最小可联调链路，再补叠层、互斥、驱散等规则。
2. **规则与执行分离**：Buff 配置规则、运行时状态机、同步编码分别建模。
3. **命令化副作用**：技能、Buff、属性变化统一走命令通道，避免旁路写状态。
4. **快照随功能走**：每引入一个新域（Numeric / Buff / Skill），立即纳入快照，不后置到单独阶段。
5. **先可观测再优化**：先保证事件流可追踪、状态可比对，再做对象池和压测优化。
6. **跨线依赖显式化**：凡依赖帧同步/技能编辑器产物，均在阶段文档写明前置版本。

### 核心架构

#### 逻辑层（GameShared，双端共用）

```
GameShared/FrameSync/Battle/          ← 纯 C# 共享逻辑（无 Unity/Fantasy 依赖）
├── MoveSystem.cs                     ← 已有：纯函数 Apply(state, dx, dy, dt)
├── BuffSystem.cs                     ← 新增：纯函数 AddBuff / ApplyTick / RemoveBuff
├── NumericSystem.cs                  ← 新增：属性修饰器（固定值/百分比）
├── DamageSystem.cs                   ← 新增：伤害计算与结算
├── PlayerState.cs                    ← 扩展：ActiveBuffs + Numeric 字段
└── DeterminismRules.cs               ← 已有：战斗常量
```

#### 表现层（仅客户端）

```
GameLogic/Battle/View/                ← 仅客户端，不参与帧同步
├── BuffViewManager.cs                ← 帧间差异驱动：对比快照中 Buff 变化，播/停特效
├── BuffEffectPlayer.cs               ← 根据 buffId 查配置表播放对应特效/音效
└── BuffBarUI.cs                      ← Buff 图标、层数、倒计时 UI 显示
```

- 表现层只读 PlayerState 数据，不写逻辑状态
- 通过 `BattleClientController.SyncRendering()` 每渲染帧调用
- 回滚时表现层自动修复（对比前后帧差异，不需要知道发生了回滚）

#### SkillGraph 节点扩展

```
├── ApplyBuffNodeHandler              ← 新增：技能图 ApplyBuff 节点
├── RemoveBuffNodeHandler             ← 新增：技能图 RemoveBuff 节点
├── BuffConditionNodeHandler          ← 新增：技能图 Buff 条件判断节点
└── ISkillRuntimeServices             ← 扩展：ApplyBuff / RemoveBuff / HasBuff 接口
```

#### 帧执行时序

```
逻辑帧 Tick（客户端和服务端同一份代码）：
  1. ProcessInputs(frameIndex)
  2. MoveSystem.Apply(state, dx, dy, dt)
  3. 消费命令队列（ApplyBuffCmd / RemoveBuffCmd）
  4. BuffSystem.ApplyTick(state.Buffs, state, frameIndex, dt)
  5. SnapshotManager.Save(frameIndex)

客户端领先服务端若干帧（leadFrames ≈ RTT/2 ÷ fixedDt），本地预测执行。
收到服务端权威快照时校验一致性，不一致则回滚重播。

渲染帧 SyncRendering（仅客户端，每渲染帧）：
  1. 读取 BattleWorldState 中所有 PlayerState
  2. 位置同步：capsule.transform.position = ToWorldPosition(player.X, player.Y)
  3. Buff 表现同步：BuffViewManager.Refresh(player.ActiveBuffs) 对比帧间差异
     - 新增 Buff → 播特效、创建图标
     - 移除 Buff → 停特效、移除图标
     - 层数/时间变化 → 更新图标显示
```

---

## 版本路线（总览）

### 第一阶段：基座对齐

| 阶段 | 聚焦点 | 核心产出 |
|------|--------|---------|
| v0.0a | 参考实现映射与目录规范 | 领域模型映射表 + GameShared 目录锁定 + 数据结构定义 |

> 注：v0.0b（战斗 Tick 骨架）和 v0.0c（战斗协议骨架）已由状态帧同步版本线完成，不再重复。

### 第二阶段：最小战斗闭环

| 阶段 | 聚焦点 | 核心产出 |
|------|--------|---------|
| MVP-a | 属性与伤害闭环 | NumericSystem + DamageSystem + 属性快照纳入 |
| MVP-b | Buff 生命周期最小实现 | BuffSystem 纯函数 + Buff 状态快照纳入 |
| MVP-c | 技能触发 Buff 闭环 | SkillGraph 新节点 + ISkillRuntimeServices 扩展 + 编辑器节点 |

### 第三阶段：Buff 规则完善

| 阶段 | 聚焦点 | 核心产出 |
|------|--------|---------|
| v0.2a | 叠加/刷新/互斥 | Overlay/Refresh/Mutex 规则引擎 |
| v0.2b | 多来源实例管理 | RuntimeBuffId + 索引结构 + 冲突处理 |
| v0.2c | 死亡/驱散/免疫 | 清理策略矩阵 + 边界行为定义 |

### 第四阶段：确定性保障

| 阶段 | 聚焦点 | 核心产出 |
|------|--------|---------|
| v0.3a | 统一快照域验证 | Skill+Buff+Attribute 全域 Snapshot/Restore 往返测试 |
| v0.3b | 回滚重播验证 | 一致性检查 + 指定帧回滚重播（依赖状态帧同步 v0.3c） |

### 第五阶段：工程化

| 阶段 | 聚焦点 | 核心产出 |
|------|--------|---------|
| v0.4a | 性能优化 | 命令/Buff 对象池 + 热路径 Profiling |
| v0.4b | 断线重连 | 战斗态恢复 + 分帧补帧 |
| v0.4c | 安全与防作弊 | 输入归属校验 + 命令频率与合法性校验 |
| v1.0 | 联调封版 | 全链路回归、压测达标、发布基线 |

---

## 阶段文档目录

### v0.0a `战斗Buff系统-v0.0a计划.md`
- **目标**：完成 NKGMobaBasedOnET 到 TEngine 的战斗/Buff 领域映射，锁定术语、目录和数据结构
- **前置依赖**：无
- **范围**：
  - 模块映射表（参考项目 → TEngine 对应关系）
  - `GameShared/FrameSync/Battle/` 目录基线与命名约定
  - `BuffState`、`NumericState`、`ApplyBuffCommand` 等核心数据结构定义
  - PlayerState 扩展字段规划
  - `ISkillRuntimeServices` 接口扩展规划
- **确定性验证**：映射链路必须标注所有非确定性风险点及替代方案
- **工程验证**：评审通过并形成可执行任务拆分
- **完成标准**：输出映射文档 + 目录基线 + 数据结构定义，团队评审通过

### MVP-a `战斗Buff系统-MVP-a计划.md`
- **目标**：建立属性与伤害最小闭环
- **前置依赖**：v0.0a
- **范围**：
  - `NumericSystem`：权威属性容器，支持固定值/百分比修饰器
  - `DamageSystem`：CastDamage / ReceiveDamage 最小链路
  - `PlayerState` 扩展：NumericState 字段
  - 属性状态纳入快照（扩展 `PlayerStateSnapshot` + 哈希计算）
- **非目标**：Buff 对属性的影响、Luban 配置表接入
- **确定性验证**：同输入序列下属性结果一致
- **工程验证**：基础伤害场景稳定运行，快照往返无异常漂移
- **完成标准**：伤害可稳定改变属性并可回放验证，属性参与帧快照

### MVP-b `战斗Buff系统-MVP-b计划.md`
- **目标**：实现 Buff 生命周期最小闭环
- **前置依赖**：MVP-a
- **范围**：
  - `BuffSystem` 纯函数：`AddBuff` / `ApplyTick` / `RemoveBuff`
  - `BuffState` 数据结构：BuffId、层数、剩余帧数、来源/目标
  - `ApplyBuffCommand` / `RemoveBuffCommand` 命令定义与队列消费
  - `PlayerState` 扩展：ActiveBuffs 字段
  - Buff 状态纳入快照（扩展 `PlayerStateSnapshot` + 哈希计算）
- **非目标**：叠加规则、多来源实例、SkillGraph 触发
- **确定性验证**：Buff 到期帧在双端一致，快照哈希一致
- **工程验证**：单 Buff 连续压测无泄漏，快照窗口容量可控
- **完成标准**：至少 1 种持续型 Buff 正确生效并移除，Buff 参与帧快照

### MVP-c `战斗Buff系统-MVP-c计划.md`
- **目标**：打通技能触发 Buff 闭环
- **前置依赖**：MVP-b + 技能节点编辑器 v0.8-A
- **范围**：
  - SkillGraph 新节点类型：`ApplyBuff`、`RemoveBuff`、`BuffCondition`
  - 节点处理器：`ApplyBuffNodeHandler`、`RemoveBuffNodeHandler`、`BuffConditionNodeHandler`
  - `ISkillRuntimeServices` 接口扩展：`ApplyBuff` / `RemoveBuff` / `HasBuff` / `GetBuffStackCount`
  - 服务端实现：命令写入权威队列 → Tick 中消费
  - 客户端实现：命令写入本地预测队列 → 本地预测执行
  - 编辑器节点：`ApplyBuffNode`、`RemoveBuffNode`、`BuffConditionNode`（SkillGraphEditor 注册）
- **非目标**：叠加规则、复杂 Buff 效果（控制、驱散）
- **确定性验证**：事件流（NodeEnter/CommandIssued/BuffChange）双端一致
- **工程验证**：2 客户端联调同技能触发结果一致
- **完成标准**：技能触发 Buff 的端到端链路可联调，编辑器可拖拽配置

### v0.2a `战斗Buff系统-v0.2a计划.md`
- **目标**：完善叠加/刷新/互斥规则
- **前置依赖**：MVP-c
- **范围**：Overlay、RefreshDuration、MutexGroup、优先级规则
- **确定性验证**：多 Buff 并发下规则执行顺序一致
- **工程验证**：叠加/刷新/互斥测试矩阵通过
- **完成标准**：规则引擎覆盖核心 Buff 类型
- **当前记录**：2026-05-24 已完成实现，8 个代码级自测 + 2 个 Unity Editor 双客户端联调场景通过

### v0.2b `战斗Buff系统-v0.2b计划.md`
- **目标**：解决多来源同 Buff 实例冲突
- **前置依赖**：v0.2a
- **范围**：RuntimeBuffId、索引结构、实例替换/并存策略
- **确定性验证**：多来源重复施加结果一致
- **工程验证**：高频施加场景无错误覆盖
- **完成标准**：实例管理策略稳定

### v0.2c `战斗Buff系统-v0.2c计划.md`
- **目标**：补齐死亡/驱散/免疫边界行为
- **前置依赖**：v0.2b
- **范围**：标签清理策略、死亡时清理、驱散白名单/黑名单
- **确定性验证**：边界场景下清理结果一致
- **工程验证**：异常场景（连击致死/同时驱散）无状态残留
- **完成标准**：策略矩阵文档 + 自动化用例通过

### v0.3a `战斗Buff系统-v0.3a计划.md`
- **目标**：统一快照域验证
- **前置依赖**：v0.2c + 技能节点编辑器 v0.8-A
- **范围**：
  - Skill 执行态 + Buff + Attribute 全域 Snapshot/Restore
  - N 帧快照恢复后继续执行结果验证
  - 快照窗口容量与内存评估
- **确定性验证**：全域快照往返测试通过（恢复后继续执行结果一致）
- **工程验证**：快照窗口容量可控，无内存泄漏
- **完成标准**：统一快照往返测试通过，全域哈希计算正确

### v0.3b `战斗Buff系统-v0.3b计划.md`
- **目标**：回滚重播验证
- **前置依赖**：v0.3a + 状态帧同步 v0.3c
- **范围**：
  - 哈希比对（客户端预测 vs 服务端权威）
  - 差异定位（移动、属性、Buff 三域）
  - 回滚到指定帧并逐帧重播
- **确定性验证**：注入不一致后可恢复到权威状态
- **工程验证**：单次回滚重播耗时 < 16ms
- **完成标准**：回滚链路稳定可复现

### v0.4a `战斗Buff系统-v0.4a计划.md`
- **目标**：性能与带宽优化
- **前置依赖**：v0.3b
- **范围**：
  - Buff/命令对象池、热路径优化、GC 压力治理
  - 增量快照：全量基准帧 + 脏标记差分帧，降低广播带宽
  - 快照频率优化：关键帧 10Hz + 客户端插值填充
- **确定性验证**：优化前后逻辑结果完全一致
- **工程验证**：多人多 Buff 压测达到目标帧时预算，带宽降至 ~3-5 KB/s/客户端
- **完成标准**：性能指标达标并输出 Profiling 报告，带宽优化效果可量化

### v0.4b `战斗Buff系统-v0.4b计划.md`
- **目标**：断线重连与补帧恢复
- **前置依赖**：v0.4a + 状态帧同步 v0.6b
- **范围**：重连态恢复、分帧补帧、Buff 状态重建
- **确定性验证**：重连后与在线玩家状态哈希一致
- **工程验证**：断线 10 秒后恢复，补帧期间不卡顿
- **完成标准**：重连恢复链路验收通过

### v0.4c `战斗Buff系统-v0.4c计划.md`
- **目标**：安全与防作弊
- **前置依赖**：v0.4b + 状态帧同步 v0.8
- **范围**：输入归属校验、频率限制、非法 Buff 命令拦截
- **确定性验证**：防作弊逻辑不影响正常一致性
- **工程验证**：常见伪造输入场景可检测并拦截
- **完成标准**：误报/漏报指标达标

### v1.0 `战斗Buff系统-v1.0计划.md`
- **目标**：联调封版
- **前置依赖**：v0.4c
- **范围**：全链路回归、压测、发布基线冻结
- **确定性验证**：全量关键场景哈希对比通过
- **工程验证**：长时稳定性与性能指标达标
- **完成标准**：所有验收项通过并具备上线条件

---

## 阶段推进规则

1. 每个阶段文档必须包含：目标、前置依赖、范围、非目标、实现步骤、验证方式、完成标准、风险与应对。
2. 每个阶段至少包含 1 项确定性验证与 1 项工程验证。
3. 当前阶段完成标准全部满足后，才能进入下一阶段。
4. 跨版本线依赖（状态帧同步/技能编辑器）必须在阶段文档首部显式声明。
5. 新增需求先归类到现有阶段；仅当影响主路径时新增子阶段文档。

---

## 当前状态

| 阶段 | 状态 |
|------|------|
| v0.0a | `已完成`（覆盖 MVP-a + MVP-b 范围） |
| MVP-a | `已完成`（由 v0.0a 实现） |
| MVP-b | `已完成`（由 v0.0a 实现） |
| MVP-c | `已完成`（双客户端验证通过，2026-05-23） |
| v0.2a | `已完成`（2026-05-24；规则引擎实现完成，8 个代码级自测 + 2 个 Unity 双端场景通过） |
| v0.2b | `未开始` |
| v0.2c | `未开始` |
| v0.3a | `未开始` |
| v0.3b | `未开始` |
| v0.4a | `未开始` |
| v0.4b | `未开始` |
| v0.4c | `未开始` |
| v1.0 | `未开始` |
