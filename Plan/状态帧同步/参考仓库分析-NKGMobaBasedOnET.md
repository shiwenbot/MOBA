# NKGMobaBasedOnET 状态帧同步系统分析

## 背景

本文档是对 `D:\unity\Tencent\NKGMobaBasedOnET` 仓库中状态帧同步系统的深度分析，为 TEngine 项目的帧同步开发提供参考和避坑指南。

---

## 1. 系统架构概览

该仓库基于 **ET 框架（ECS 架构）** 实现了一套状态帧同步系统，核心设计：

- **服务端权威**：服务端为最终状态裁决者
- **客户端预测**：输入先本地执行，等服务端确认
- **不一致回滚**：检测到偏差时回滚并重播

### 核心组件

| 组件 | 文件 | 职责 |
|------|------|------|
| LSF_Component | Model/.../LSF_Component.cs | 帧同步核心，管理帧号、命令缓冲、同步状态 |
| LSF_ComponentUtilities | Hotfix/.../LSF_ComponentUtilities.cs | 帧同步算法：Tick、回滚、追帧、异常处理 |
| LSF_TickComponent | .../LSF_TickComponentSystem.cs | Tick/RollBack/CheckConsistency 的分发 |
| LSF_ComponentEvents | .../LSF_ComponentEvents.cs | Ping 变化时调整追帧参数 |
| TimeAndFrameConverter | .../TimeAndFrameConverter.cs | 毫秒/帧数互转 |
| PingComponent | Module/Message/PingComponentSystem.cs | RTT 测量 |

### 关键数据结构

```
LSF_Component 核心字段：
├── CurrentFrame: uint              // 当前帧号
├── ServerCurrentFrame: uint        // 服务端当前帧（客户端推算）
├── FrameCmdsToHandle: SortedDictionary<uint, Queue<ALSF_Cmd>>  // 待处理命令
├── FrameCmdsToSend: Dictionary<uint, Queue<ALSF_Cmd>>          // 待发送命令
├── PlayerInputCmdsBuffer: Dictionary<uint, Queue<ALSF_Cmd>>    // 预测输入缓冲
├── WholeCmds: Dictionary<uint, Queue<ALSF_Cmd>>                // 全局命令记录
├── CurrentAheadOfFrame: int        // 当前超前帧数
├── TargetAheadOfFrame: int         // 目标超前帧数
└── IsInChaseFrameState: bool       // 是否在追帧状态
```

### 帧同步参数

- 逻辑帧率：30Hz（FixedUpdateTargetFPS = 30）
- 最大超前帧：10 帧
- 帧间隔：~33ms

---

## 2. 六大问题域分析

### 2.1 网络延迟与抖动

**Ping 机制**：
- 持续异步循环，测量 C2G（Gate）和 C2M（Map）两轮 RTT
- RTT 变化时发布 PingChange 事件，携带服务端时间戳和帧号

**超前帧计算**：
```
TargetAheadOfFrame = RTT/2 对应帧数 + 1帧缓冲
TargetAheadOfFrame = min(TargetAheadOfFrame, AheadOfFrameMax)
```

**变速追帧**：
- 通过动态调整 FixedUpdate 的 TargetElapsedTime 实现变速
- 频率 = 30 + (TargetAheadOfFrame - CurrentAheadOfFrame)
- 落后时加速，超前时减速

**评价**：
- ✅ 完整的延迟感知→动态调整闭环
- ✅ 变速追帧比跳帧体验更好
- ❌ Ping 没有做平滑处理（EMA），单次抖动就可能导致超前帧波动
- ❌ 变速 Tick 可能导致物理引擎非确定性

### 2.2 预测与回滚

**预测流程**：
1. 客户端输入同时放入 `FrameCmdsToSend`（发服务端）和 `PlayerInputCmdsBuffer`（本地执行）
2. 命令帧号 = CurrentFrame + 1
3. 本地 Tick 时先执行预测输入，再执行正常 Tick 逻辑

**回滚流程**：
1. 收到服务端回包 → 调用 `CheckConsistencyCompareSpecialFrame`
2. 遍历所有 `ILSF_TickHandler` 组件，逐一对比历史快照
3. 不一致时：回退帧号 → RollBack → 从目标帧逐帧重播到当前帧

**一致性检查**：
- 每个组件在 `OnLSF_TickEnd` 中保存历史快照（如 `HistroyMoveStates`）
- 收到服务端回包时逐字段对比（float 阈值 0.001）
- 服务端只发送"脏数据"（状态变化超过阈值），节省带宽

**评价**：
- ✅ 完整的预测-回滚-重播流程
- ✅ 服务端脏数据过滤节省带宽
- ✅ `ILSF_TickHandler` 接口设计良好
- ❌ 没有全局状态哈希，只在收到回包时检查
- ❌ 组件各自维护历史状态，容易遗漏字段
- ❌ 历史快照无清理机制，内存持续增长

### 2.3 断线重连

**当前实现**：
- `WholeCmds` 记录全局所有命令（用于重播恢复）
- 超前帧超过 `AheadOfFrameMax` 时判定断线
- 断线后 `ShouldTickInternal = false`，停止模拟

**评价**：
- ❌ **核心流程未实现**。重连代码是 `await TimerComponent.WaitAsync(3000)` 的 TODO
- ❌ 没有重连协议（Proto 文件中无 M2C_Reconnect）
- ❌ 重连后没有分帧追帧逻辑
- **评分：3/10，仅骨架代码**

### 2.4 帧率与时间管理

**FixedUpdate 实现**：
- 来自 Xenko 引擎的时间累积器，独立于 Unity 物理 FixedUpdate
- 在 Update 中手动调用，避免与 Unity 物理帧率冲突
- 支持动态调整 TargetElapsedTime
- 最大累积时间 500ms，防止长时间卡顿后帧爆炸

**评价**：
- ✅ 帧同步逻辑独立于 Unity 物理循环，自主控制
- ✅ Xenko 时间累积器成熟可靠
- ❌ 30Hz 逻辑帧率对 MOBA 偏低（常见 60Hz）
- ❌ 追帧时 dt 不稳定，Box2D 可能非确定性

### 2.5 命令系统

**命令流程**：
```
客户端输入 → AddCmdToSendQueue → FrameCmdsToSend + PlayerInputCmdsBuffer
  → C2M_FrameCmd → 服务端 C2M_FrameCmdHandler → AddCmdToHandleQueue
  → 服务端 Tick → Handle(cmd) → 广播 M2C_FrameCmd
  → 客户端 M2C_FrameCmdHandler → 一致性检查 → 可能回滚 → Handle(cmd)
```

**命令类型**：
```
Move=1, CreateSpiling=3, CommonAttack=4, SyncFSMState=5,
SyncAttribute=6, CreateCollider=7, SyncBuff=8,
ChangeBlackBoardValue=101, PlayerSkillInput=20001+
```

**评价**：
- ✅ 命令类型系统清晰，自动注册
- ✅ KCP 可靠传输，防丢失
- ✅ SortedDictionary 保证帧序处理
- ❌ 每个命令单独广播，无帧内聚合
- ❌ 帧号完全由客户端指定，服务端未做修正
- ❌ PlayerInputCmdsBuffer 无超时清理

### 2.6 安全性

**评价**：
- ❌ 客户端指定帧号，恶意客户端可伪造
- ❌ 命令无签名或完整性校验
- ❌ 无输入频率限制

---

## 3. 总体评分

| 维度 | 评分 | 说明 |
|------|------|------|
| 架构设计 | 7/10 | 整体清晰，TickHandler 抽象良好，但缺乏全局状态快照 |
| 延迟处理 | 6/10 | 完整的 Ping→调整→变速闭环，但缺乏平滑和边界处理 |
| 预测回滚 | 7/10 | 流程完整，但依赖组件自行实现快照，易遗漏 |
| 断线重连 | 3/10 | 仅骨架代码（TODO 状态） |
| 确定性 | 4/10 | 全 float 运算，无状态哈希，物理引擎未处理 |
| 命令系统 | 7/10 | 类型系统清晰，序列化可靠，但无帧聚合 |
| 完成度 | 6/10 | 核心流程可运行，多个关键部分 TODO |

**总结**：学习/演示级别的实现，架构思路正确，但工程化不足。适合作为架构参考，但具体实现需要大幅改进。

---

## 4. 对 TEngine 项目的启示

### 4.1 可借鉴的设计

1. **ILSF_TickHandler 接口抽象**：Tick/RollBack/CheckConsistency 统一接口，组件化注册
2. **预测-回滚-重播流程**：整体思路正确，可以沿用
3. **Xenko 时间累积器**：独立于 Unity 物理循环的 FixedUpdate
4. **脏数据过滤**：服务端只发送变化数据，节省带宽
5. **命令类型枚举 + 自动注册**：扩展方便

### 4.2 需要改进的方面

1. **确定性保证**：使用 Box2D 确定性物理引擎（与参考仓库 Box2D Sharp 方案一致），配合固定 dt 保证物理确定性
2. **全局状态哈希**：每帧计算整体状态哈希，用于一致性校验
3. **统一状态快照**：通用快照/恢复机制（ISnapshotable 接口），避免组件遗漏
4. **内存管理**：历史快照滑动窗口，定期清理
5. **断线重连**：完整实现重连协议和分帧追帧
6. **命令聚合**：一帧内命令打包发送
7. **帧号安全**：服务端权威帧号分配
8. **Ping 平滑**：EMA 或中值滤波
9. **帧计时器**：基于帧数的计时器（参考仓库的 LSF_TimerComponent），用于 Buff 持续时间、技能 CD 等，不依赖 wall clock
10. **命令对象池**：命令对象复用（参考仓库的 ReferencePool），30Hz 高频创建命令场景下减少 GC 压力

### 4.3 需要重新设计（适配 TEngine）

1. **网络层**：参考仓库用 ET 框架 + KCP，TEngine 用 Fantasy 框架
2. **ECS 适配**：参考仓库基于 ET 的 ECS，TEngine 需要设计自己的实体管理
3. **异步模型**：参考仓库用 ET 的 async/await，TEngine 用 Fantasy 的 FTask
4. **序列化**：参考仓库用 Protobuf，TEngine 网络消息用 Fantasy 自带序列化或 Protobuf（Luban 仅用于配置表）
5. **热更边界**：帧同步代码需要区分主包和热更部分

---

## 5. 关键技术决策点

以下为已确定和待讨论的技术决策：

### 已确定

| 决策项 | 方案 | 说明 |
|--------|------|------|
| 物理引擎 | Box2D（确定性物理） | 与参考仓库 Box2D Sharp 方案一致，固定 dt 保证确定性 |
| 逻辑帧率 | 30Hz | dt = 33.333ms，与参考仓库一致 |
| 网络框架 | Fantasy | 替代参考仓库的 ET + KCP |
| 异步模型 | FTask（Fantasy 自带） | 替代参考仓库的 ET async/await |
| 网络消息序列化 | Fantasy 自带 / Protobuf | Luban 仅用于配置表 |

### 待讨论

1. **状态快照策略**：全量快照 vs 增量快照 vs 组件级快照？
2. **回滚深度限制**：最多回滚多少帧？如何平衡内存和正确性？
3. **服务端架构**：帧同步服务端与 Fantasy 框架如何集成？
4. **Box2D 追帧约束**：追帧时 Box2D Step 是否必须逐帧调用（不可跳帧），对追帧性能的影响？

---

## 6. 参考仓库中值得关注但计划中未覆盖的机制

以下是参考仓库中存在、但当前计划文档中未明确提及的关键机制，需要在对应版本中考虑：

| 机制 | 参考仓库实现 | 建议纳入版本 |
|------|-------------|-------------|
| LSF_TimerComponent（帧计时器） | 基于帧数的计时器，不依赖 wall clock | MVP-a（基础设施） |
| ReferencePool（命令对象池） | 命令对象复用，减少 GC | MVP-a（基础设施） |
| Xenko 时间累积器 | 独立于 Unity FixedUpdate 的逻辑帧驱动 | MVP-a（帧驱动核心） |
| LSF_TickHandler 三阶段生命周期 | TickStart → Tick → TickEnd | v0.3a（快照机制） |
| 属性注解自动注册 | LSF_MessageHandlerAttribute / LSF_TickableAttribute | v0.2a（命令系统） |
| Box2D 物理世界 Ticker | B2S_WorldComponentTicker | v0.4a（物理集成） |
