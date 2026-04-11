# 状态帧同步-MVP-a计划：帧驱动基础设施

## Context

帧同步系统的一切逻辑都建立在稳定的固定帧率 Tick 循环之上。在网络层（v0.0a）和物理确定性（v0.0b）均已验证通过的前提下，本阶段聚焦于构建帧驱动基础设施——让客户端和服务端都能以 30Hz 稳定推进逻辑帧。

关键背景：
- 仿真层（GameShared）必须是纯 C# 代码，不依赖 Unity API 和 Fantasy 框架，确保双端共享。
- 驱动层各端独立实现：客户端用 MonoBehaviour.Update，服务端用 Fantasy Scene Update。
- 所有仿真层代码禁止使用 DateTime/Stopwatch/Random 等非确定性 API，这是从第一行代码就必须遵守的硬约束。
- GameShared 通过 Entity.csproj 的 `<Compile Include>` 链接到服务端。

前置依赖：
- v0.0a（已完成）：Fantasy 消息收发验证，确认 30Hz 消息频率可行。
- v0.0b（已完成）：Box2D 确定性验证，确认固定 dt 下物理结果 bit-exact。

## 阶段状态

- 状态：`未开始`
- 更新日期：`2026-04-11`

---

## 目标

- 建立 Xenko 风格的时间累积器，将不稳定的渲染帧 deltaTime 转化为稳定的固定步长 Tick。
- 建立 Tick 分发机制，支持多个 ITickable 按固定优先级顺序执行。
- 实现基于帧号的计时器系统，为后续 Buff 持续时间、技能 CD 等提供确定性计时基础。
- 实现泛型命令对象池，为后续 30Hz 高频命令创建场景减少 GC 压力。
- 从第一行代码就建立确定性约束规范，防止后续阶段引入非确定性代码。
- 客户端和服务端均能稳定运行 30Hz Tick 循环，帧号连续递增无跳帧。

---

## 范围

- Xenko 时间累积器（TickAccumulator）。
- Tick 分发器（TickDispatcher）+ ITickable 三阶段接口定义。
- 帧计时器服务（FrameTimerService + FrameTimer）。
- 泛型命令对象池（CommandPool\<T\> + IResettable）。
- 确定性约束工具（DeterminismRules）。
- 客户端驱动器（ClientTickDriver）。
- 服务端驱动器（ServerTickDriver）。

---

## 非目标

- 不实现网络同步（MVP-b 处理）。
- 不实现输入采集和命令上行。
- 不实现 Box2D 物理集成（v0.4a 处理）。
- 不实现状态快照/回滚逻辑（v0.3a 处理）。
- 不实现可视化渲染或 UI。
- ITickable 的 RollBack/CheckConsistency 只定义接口，不实现逻辑。

---

## 技术决策

### 1. 仿真层纯 C# 约束

| 决策 | 说明 |
|------|------|
| GameShared/FrameSync/ 内禁止 `using UnityEngine` | 确保服务端可编译 |
| GameShared/FrameSync/ 内禁止 `using Fantasy` | 仿真层不依赖网络框架 |
| 日志输出通过注入式 IFrameSyncLogger 接口 | 驱动层各自注入实现，仿真层无 `#if` 分支 |

### 2. 时间累积器设计（Xenko 风格）

| 参数 | 值 | 说明 |
|------|-----|------|
| fixedDeltaTime | 1.0f/30 (≈0.03333s) | 固定仿真步长，绝不改变 |
| maxDeltaTime | 0.5s | deltaTime 超过此值直接截断为 0.5s |
| maxTicksPerUpdate | 15 | 由 maxDeltaTime / fixedDeltaTime 得出（0.5/0.0333≈15），两个参数数学一致 |

说明：maxDeltaTime 是唯一主约束，maxTicksPerUpdate 由它推导而来，不独立配置。

### 3. ITickable 三阶段接口（预留回滚扩展）

MVP-a 只实现 Tick 阶段，RollBack 和 CheckConsistency 提供空默认实现：

```
interface ITickable {
    int Priority { get; }                          // 执行优先级，数值小的先执行
    void Tick(uint frameIndex, float fixedDt);     // 正向推进（MVP-a 实现）
    void RollBack(uint targetFrame) { }            // 回滚到目标帧（v0.3c 实现，默认空）
    void CheckConsistency(uint frameIndex) { }     // 一致性检查（v0.3b 实现，默认空）
}
```

理由：从一开始定义三阶段，后续是"补实现"而非"改架构"。MVP-a 阶段 TickDispatcher 只调用 Tick，不调用 RollBack/CheckConsistency。

### 4. TickDispatcher 执行顺序与重入规则

- 注册时指定 Priority，按 Priority 升序执行。
- Priority 相同时，按注册顺序（内部递增序号）作为二级排序键，保证双端一致。
- Tick 执行期间的 Register/Unregister 放入延迟队列，当前帧结束后生效。
- 禁止 Tick 中直接修改注册表，防止回滚和一致性检查变脆。

### 5. FrameTimerService 重入与触发顺序规则

- 同帧到期的多个 Timer 按 id 升序触发（id 为递增分配）。
- Timer 回调中允许 AddTimer/RemoveTimer，但新增 Timer 不在当前帧触发（延迟到下帧检查）。
- 回调中 RemoveTimer 立即标记为已移除，后续不再触发。

### 6. CommandPool 强制 Reset 协议

- 所有池化对象必须实现 `IResettable.Reset()`。
- `Return()` 时自动调用 `Reset()`，防止跨帧残留字段污染仿真。

### 7. 确定性约束（DeterminismRules）

GameShared/FrameSync/ 内的代码禁止：
- `System.DateTime` / `System.Diagnostics.Stopwatch`（时间依赖）
- `System.Random` / `UnityEngine.Random`（随机数）
- `Dictionary<K,V>` / `HashSet<T>` 的遍历（遍历顺序不稳定）
- `async/await`（回调顺序不确定）

替代方案：
- 时间 → 用帧号
- 随机数 → 用种子确定性随机（后续版本引入）
- Dictionary 遍历 → 用 `SortedDictionary` 或 `List` + 排序
- 异步 → 仿真层内全同步

---

## 实现步骤

### Step 1：定义 ITickable 三阶段接口 + IResettable

目标：建立帧驱动的统一抽象和对象池重置协议。

输出：
- `GameShared/FrameSync/Core/ITickable.cs`
- `GameShared/FrameSync/Command/IResettable.cs`

验证：接口可被服务端和客户端代码引用，双端编译通过。

### Step 2：实现 TickAccumulator

目标：将不均匀的 deltaTime 累积为离散的固定步长 Tick 次数。

核心思路：
- 持有 `_accumulator` 浮点累积量
- `Accumulate(float deltaTime)` 累加后按 `fixedDt` 取整，返回本帧应执行的 Tick 次数
- deltaTime 先 clamp 到 maxDeltaTime，Tick 次数 clamp 到 maxTicksPerUpdate

输出：`GameShared/FrameSync/Core/TickAccumulator.cs`

验证：单元测试覆盖正常帧（60fps→每帧0-1次Tick）、低帧率（15fps→每帧2次Tick）、追帧上限（卡顿1秒→最多15次Tick）。

### Step 3：实现 TickDispatcher

目标：按固定顺序驱动所有 ITickable 完成三阶段调用。

核心思路：
- 维护按 Priority 排序的 `List<ITickable>` 注册表，Priority 相同时按注册序号排序
- `Update(float deltaTime)` 内部调用 TickAccumulator 获取 tickCount，循环执行
- 每次 Tick 调用所有 ITickable 的 `Tick(frameIndex, fixedDt)`（MVP-a 只走 Tick 阶段）
- 内部维护 `_currentFrame`（uint），每次 Tick 后递增
- Register/Unregister 放入延迟队列，帧结束后统一处理
- ITickable 回调中抛出异常时：记录日志并继续执行后续 ITickable（fail-safe），不中断整个 Tick 循环

输出：`GameShared/FrameSync/Core/TickDispatcher.cs`

验证：注册 2 个 mock ITickable（不同 Priority），驱动 3 帧，确认调用顺序和帧号递增正确。

### Step 4：实现 FrameTimerService

目标：提供基于帧号的定时器，替代 wall-clock Timer，保证确定性。

核心思路：
- `FrameTimer` 数据结构：id、触发帧号、回调、是否循环、间隔帧数
- `FrameTimerService` 实现 ITickable，在 Tick 中检查并触发到期 Timer
- 提供 `AddTimer(delayFrames, callback, repeat)` 和 `RemoveTimer(id)` 接口

输出：
- `GameShared/FrameSync/Timer/FrameTimer.cs`
- `GameShared/FrameSync/Timer/FrameTimerService.cs`

验证：注册 delay=3 的单次 Timer 和 interval=2 的循环 Timer，驱动 10 帧，确认触发时机正确。

### Step 5：实现 CommandPool\<T\>

目标：提供命令对象的池化复用，避免帧内频繁 GC。

核心思路：
- `CommandPool<T> where T : IResettable, new()`
- `Rent()` 从池中取或 new，`Return(T)` 调用 Reset() 后归还
- 内部用 `Stack<T>` 存储空闲对象

输出：`GameShared/FrameSync/Command/CommandPool.cs`

验证：Rent 10 个对象，Return 后再 Rent，确认对象被复用且 Reset 被调用。

### Step 6：实现 DeterminismRules

目标：集中定义确定性约束检查工具。

核心思路：
- 提供 `float` → `int bits` 的标准化转换（复用 v0.0b 的 `BitConverter.SingleToInt32Bits` 策略）
- 提供 `AssertFixedDt(float dt)` 断言
- Debug 模式启用，Release 编译为空操作（`[Conditional("DEBUG")]`）

输出：`GameShared/FrameSync/Determinism/DeterminismRules.cs`

验证：传入非法值时抛出明确异常，Release 模式下无性能开销。

### Step 7：实现 ClientTickDriver

目标：在 Unity 侧通过 MonoBehaviour.Update 驱动 TickDispatcher。

核心思路：
- `Update()` 中调用 `TickDispatcher.Update(Time.deltaTime)`
- 持有 TickDispatcher 和 FrameTimerService 的引用
- 提供 Start/Stop 控制

输出：`GameLogic/FrameSync/ClientTickDriver.cs`

验证：挂载到场景 GameObject，注册一个打印帧号的 ITickable，Console 输出稳定 30Hz 帧号序列。

### Step 8：实现 ServerTickDriver

目标：在 Fantasy Scene Update 中驱动服务端帧循环。

核心思路：
- 在 Fantasy Scene 的 Update 回调中驱动 TickDispatcher
- 用 Stopwatch 计算 deltaTime（仅驱动层，不进入仿真）
- 调用链与 ClientTickDriver 对称

输出：`GameServer/Server/Entity/FrameSync/ServerTickDriver.cs`

验证：服务端启动后 Console 输出稳定 30Hz 帧号序列，与客户端帧号推进逻辑一致。

---

## 文件结构

```
UnityProject/Assets/GameScripts/HotFix/GameShared/
  FrameSync/
    Core/
      ITickable.cs              — 三阶段接口（Tick/RollBack/CheckConsistency）
      TickAccumulator.cs        — Xenko 时间累积器
      TickDispatcher.cs         — Tick 分发器（固定顺序 + 延迟变更队列）
    Timer/
      FrameTimer.cs             — 帧计时器数据结构
      FrameTimerService.cs      — 计时器管理服务（ITickable）
    Command/
      IResettable.cs            — 重置接口
      CommandPool.cs            — 泛型对象池
    Determinism/
      DeterminismRules.cs       — 确定性约束断言工具

UnityProject/Assets/GameScripts/HotFix/GameLogic/
  FrameSync/
    ClientTickDriver.cs         — 客户端驱动器（MonoBehaviour）

GameServer/Server/Entity/
  FrameSync/
    ServerTickDriver.cs         — 服务端驱动器（Fantasy Scene）

UnityProject/Assets/GameScripts/HotFix/GameShared/Tests/
  FrameSync/
    TickAccumulatorTests.cs     — 累积器单元测试
    TickDispatcherTests.cs      — 分发器单元测试
    FrameTimerServiceTests.cs   — 帧计时器单元测试
    CommandPoolTests.cs         — 对象池单元测试
```

---

## 完成标准

### 功能验证
- 客户端 ClientTickDriver 挂载后，30Hz Tick 循环稳定运行。
- 服务端 ServerTickDriver 启动后，30Hz Tick 循环稳定运行。
- 帧号从 0 开始连续递增，无重复。当 maxTicksPerUpdate 截断时允许仿真时间落后于墙钟时间，但帧号序列本身不跳跃。
- FrameTimerService 的单次/循环计时器在正确帧号触发。
- CommandPool 的 Rent/Return 正确复用对象，Reset 被调用。

### 工程验证
- Tick 间隔稳定性：连续运行 5 分钟，Tick 调用间隔的 p95 < 40ms（理论值 33.3ms）。
- 仿真时间漂移：5 分钟后 `currentFrame * fixedDt` 与墙钟时间差 < 1s（允许截断导致的累积漂移）。
- 对象池：Rent 1000 次 + Return 1000 次，池内对象数量正确，无泄漏。
- 帧号连续性：5 分钟内帧号严格递增（frame[n+1] == frame[n] + 1），断言无触发。
- uint 溢出：frameIndex 使用 uint（最大 ~4.29 亿），30Hz 下可运行约 165 天，MVP 阶段不处理溢出；FrameTimer 的 timerId 使用 uint 递增分配，同理。

### 确定性验证
- GameShared/FrameSync/ 下的代码不包含 `using UnityEngine` 或 `using Fantasy`。
- GameShared/FrameSync/ 下的代码在 Unity（Mono）和服务端（net8.0）均可编译通过。

---

## 关键风险与应对

| 风险 | 影响 | 应对 |
|------|------|------|
| Fantasy Scene Update 频率不稳定 | 服务端 Tick 间隔抖动大 | TickAccumulator 本身就是为不稳定 deltaTime 设计的；若抖动超预期，考虑服务端用独立线程驱动 |
| Fantasy 框架不提供 deltaTime | 无法直接获取帧间隔 | 服务端驱动层用 Stopwatch 自行测量（仅驱动层，不进入仿真） |
| ITickable 默认接口实现在 Unity Mono 下不支持 | 编译错误 | Unity 2022.3 已支持 C# 9 默认接口实现；若不支持则改为抽象基类 TickableBase |
| maxTicksPerUpdate 截断导致仿真时间丢失 | 长时间卡顿后客户端帧号落后 | MVP-a 阶段可接受；MVP-b 网络同步后通过服务端帧号校正 |
| GameShared.asmdef 意外引用 Unity/Fantasy | 服务端编译失败 | 代码规范约束 + 确定性验证检查 |
| Priority 相同时排序不稳定导致双端执行顺序不一致 | 破坏确定性 | 注册序号作为二级排序键，使用稳定排序 |
| ITickable/Timer 回调抛异常导致 Tick 循环中断 | 整个仿真停止 | TickDispatcher 捕获异常并记录日志，继续执行后续 ITickable |

---

## 后续衔接

- MVP-a 的 TickDispatcher + ITickable 是 MVP-b 网络同步的基础：服务端在 Tick 中处理输入并广播状态。
- MVP-a 的 FrameTimerService 直接用于后续 Buff 持续时间（v0.5a）和技能 CD（v0.5b）。
- MVP-a 的 CommandPool 直接用于 MVP-b 的命令对象复用。
- ITickable 的 RollBack/CheckConsistency 在 v0.3a/v0.3b/v0.3c 中实现。
- DeterminismRules 在后续每个阶段持续扩展检查项。
