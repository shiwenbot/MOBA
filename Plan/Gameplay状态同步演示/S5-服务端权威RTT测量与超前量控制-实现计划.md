# S5：服务端权威 RTT 测量与超前量控制 实现计划

> **状态**：未开始。执行编号 **S5**，见 [00-总索引.md](00-总索引.md)。
> **前置**：S1~S4 已完成；**T1（proto 往返通路）应先做**。基线 HEAD `a2b8006a`，行号按此核对。
> **相关**：[设计教训-两类静默失效.md](设计教训-两类静默失效.md) —— 本阶段的判据设计直接受其约束，**动手前读第一节**。

## 本阶段做什么

建立服务端独立测量 RTT 的通路，并把客户端超前量控制的前馈项数据源从客户端自算 RTT 换成服务端实测值。

**服务端 RTT 不是反作弊设施，是状态同步的核心原语。** 三个消费者：

1. **客户端超前量控制的前馈项** —— 已经在跑，当前数据源不可信，见 Context
2. lag compensation（按 RTT/2 回退命中判定）—— 未实现，本阶段为其铺路
3. jitter buffer 自适应定长 —— 未实现

第 1 项是本阶段的实际价值：不是新增一层机制，而是给一条已经在跑的核心机制换掉一个可被客户端影响的数据源。

**不做判定与拒收。** 输入帧号的越界只打告警，gameplay 决策权仍由既有两道硬闸独占。理由见决策四。

## Context

以下事实均已在 `a2b8006a` 上逐条核对。

### 服务端没有 RTT，一行都没有

`C2B_PingHandler.cs` 全文 17 行，把客户端时间戳原样塞回 Pong 就结束：

```csharp
pong.SendTimestampMs = message.SendTimestampMs;   // C2B_PingHandler.cs:13
session.Send(pong);
```

不记录发出时刻、不计算差值、不按 session 留存。服务端侧 grep `rtt` 零命中。**本阶段近一半工作量是从零建这条通路。**

### 超前量控制的前馈项当前用客户端自算 RTT

`RefreshBaselineLeadFrames`（`BattleSimulation.cs:589-603`）：

```
leadFrames = JitterBufferFrames(2) + InputSendSafetyFrames(1)
           + ceil(_rttEmaMs / 2 / FixedDeltaMilliseconds)
_baselineLeadFrames = clamp(leadFrames, MinLeadFrames(3), MaxLeadFrames(20))
```

`_rttEmaMs`（`:59`、`:193-198`，EMA `alpha=0.2`，初值 100ms）由 `ProcessPong` 用**客户端自己塞进 Ping 的时间戳**算出，从不上行。

即：超前量前馈项 ← 客户端自算 RTT ← 客户端自己的时钟与时间戳。客户端调时钟或改时间戳即可影响自己的超前基线。**这是本阶段要换掉的东西。**

### 超前量由两条独立路径改写，不是一个值

| 路径 | 性质 | 行为 |
|---|---|---|
| `RefreshBaselineLeadFrames`（`:589-603`） | **feedforward**，按 RTT 前馈 | 算出 `_baselineLeadFrames`，**只在 `_leadFrames < _baselineLeadFrames` 时抬升**（`:599-602`）。是**下限**，不是目标 |
| `UpdateLeadFramesFromAcceptedInput`（`:606-653`） | **feedback**，按服务端缓冲深度闭环 | 缺量时可独立冲到 `MaxLeadFrames`（`:617-621`）；超量时每 `LeadDecreaseCooldownSnapshots=6` 个快照才降 1 帧（`:630-633`） |

**feedback 环的输入 `LatestAcceptedInputFrame` 是服务端填的**（`BattleComponent.cs:212`），即它本来就在消费服务端数据。**所以本阶段的改动范围仅限 feedforward 项**，这一点决定了决策五的语义。

闭环把稳态超前量拉进 `[MinAcceptedInputBufferFrames=4, MaxAcceptedInputBufferFrames=6]`（`InputBufferTuning.cs:12-13`）。

### 既有输入帧号约束

`BattleLogic.SubmitInput`（`:105-163`）两道硬闸，本阶段不动：

| 位置 | 判据 | 计数器 |
|---|---|---|
| `:116` | `frameIndex <= LastFrameIndex` → 丢弃（受 `_hasProcessedFrame` 门控，允许首 tick 前的 frame 0） | `LateInputDropCount` |
| `:128-135` | `frameIndex > LastFrameIndex + MaxFutureInputFrames(24)` → 拒收 | `FutureInputRejectCount` |

**既有接受区间 `observedLead ∈ [1, 24]`。这个数字是本阶段告警上界设计的硬约束。**

`IsFutureFrameRejected`（`:556-559`）用符号位判帧号环绕，是本阶段该照的范例：

```csharp
return unchecked(inputFrameIndex - maxAcceptedFrame) < 0x80000000 && inputFrameIndex > maxAcceptedFrame;
```

### 无头测试的 `SimulatedClient` 不会回探测包

`SimulatedClient` 全文 41 行，被动壳子，不驱动 `BattleSimulation`、不处理任何下行消息类型（`ApplySnapshot` 直呼 `RestoreSnapshot`）。服务端向它发探测包**永远收不到回包**，RTT 恒无样本。

**这是最容易造成全线回归失败的一处**：测量层若把无样本当 `rtt=0`，5 个 scenario 会立刻开始刷告警。必须显式走无样本分支（决策四）。

## 目标

1. **服务端独立测量 RTT** —— 服务端发起探测、服务端计时，客户端无法通过伪造时间戳影响结果。
2. **前馈项数据源换成服务端实测值**，语义为 baseline 下限 + 软上限（决策五）。
3. **RTT 可视化** —— 调试面板显示测得值与超前量，弱网注入时曲线可见。这是本阶段唯一的画面产出。
4. **超前量越界时打一行告警**，复用 S3 已有的告警通道。

## 非目标

- 不做判定体系、拒收模式、per-player 计数归因（决策四）。
- 不做时钟同步。全程在帧号空间比较，不比较两端墙钟。
- 不改 `BattleLogic`。它在确定性内核里，动它会波及哈希基线。
- 不动既有 `C2B_Ping` / `S2C_Pong` 的收发（决策五改的是它的消费方）。
- 不做封禁、信誉分、输入签名、防重放。

## 决策一：服务端发起探测，时间戳不进协议，挑战值不可预测

**新增 `S2C_RttProbe(ProbeNonce)` / `C2B_RttProbeAck(ProbeNonce)` 一对单向消息，服务端按 nonce 在本地表里查发出时刻。**

- 协议里**不传时间戳**，发出时刻只存服务端内存
- 挑战值是**密码学随机 64 位 nonce**

服务端侧 `Dictionary<ulong, long> _probeSentAtMsByNonce`（按 session 持有），收到 Ack 时 `rtt = clock.NowMs − sentAt`。

### 挑战值必须随机，不能自增

探测周期固定、序号自增的话，客户端观察一轮就能预测下一个序号与大致发送时刻，于是可以在探测包实际到达之前抢发 Ack，让服务端算出接近零的 RTT。**链路是全双工的，Ack 在因果上并不必然晚于下行探测到达。**

随机 nonce 从根上断掉这条：不收到探测包就不知道该回什么值，伪造 nonce 查表必然未命中。

| 位宽 | 是否够 | 说明 |
|---|---|---|
| `uint32` 自增 | ❌ | 可预测 |
| `uint32` 随机 | ❌ | 4 字节空间在长会话里不足以排除碰撞与暴力尝试 |
| **`uint64` 密码学随机** | ✅ | 盲猜命中率可忽略 |

### 随机源与确定性的冲突

**这是真实矛盾**：挑战值需要 `RandomNumberGenerator`，而项目其余随机源统一用 `DeterministicRandom`（S4 决策四禁止不可复现随机）。

解法是随机源做成可注入接口，与 S4 的 `IBattleClock` 同构：

```
IProbeNonceSource
  ├─ CryptoProbeNonceSource   ← 生产用，RandomNumberGenerator.GetBytes
  └─ SeededProbeNonceSource   ← 无头用例用，DeterministicRandom.NextU64()，种子记进 report
```

**不要为确定性在生产用 `DeterministicRandom`** —— 它是 xoroshiro128++，观察若干输出即可反推内部状态并预测后续 nonce，等于回到自增序号。**也不要为安全在用例里用 crypto 源** —— 会失去可复现性。

### 抗篡改边界

| 客户端能做的 | 后果 | 是否可缓解 |
|---|---|---|
| 不回 Ack | 无样本 → 前馈项退回兜底值 | 拦不住，但可计数 |
| 延迟回 Ack | RTT 测大 → 超前量放宽 | **部分**，见下 |
| 提前回 Ack | 压低 RTT | 随机 nonce 天然拦住 |
| 伪造 nonce | 查表未命中，计 `UnknownAckCount` | 天然拦住 |

**「延迟回 Ack 换更宽超前量」是本方案的已知上限。** 它在本阶段的实际危害有限——本阶段不 enforce，超前量放宽不会让任何本来会被拒的输入变成被接受，既有硬闸 `[1, 24]` 始终独占 gameplay 决策权；且客户端本来就能直接延迟自己的输入发送。彻底解决需在传输层测量（KCP 层 ARQ 时序），而服务端 Fantasy 是 NuGet 包无源码（`Entity.csproj:13`）。**写进验收指南，不判 FAIL。**

### 为什么不复用 `C2B_Ping` / `S2C_Pong`

现有通路是**客户端发起**的，服务端拿到的时间戳是客户端给的。在这条链路上只能测「Ping 到达间隔」，那是客户端发包节奏，不是 RTT。

## 决策二：探测包与 Ack 双向都过 gate

**S4 的下行注入不是自动拦截，而是每类消息显式接线**：快照走 `TryAcceptSnapshotMessage`（`BattleNetworkGate.cs:73`）+ `EnqueueConvertedSnapshot`（`:78`），Pong 走 `TryAcceptPong`（`:96`）。**新消息类型不接线就完全不受注入影响。**

若探测包一到达就立刻回 Ack：

```
注入 100ms 下行 + 100ms 上行
探测下行：不过 gate → 0ms
Ack   上行：过 gate → 100ms
服务端测得 RTT ≈ 100ms，而非期望的 200ms
```

而验收判据是「测得值落进 `[200, 200 + 2×FixedDeltaMilliseconds]`」，测出 100ms 直接失败——或者更糟，有人照实测值把判据改成 100ms，就永久锁死了一个错误行为。

**新增 `TryAcceptRttProbe(nowMs, nonce, Action<ulong> deliver)`**，语义与 `TryAcceptPong` 完全一致（都是「延迟一个标量的可见时刻」）。**Ack 必须在下行释放的回调里才生成**，不能在消息到达时生成。

> 可考虑把 `TryAcceptPong` 与 `TryAcceptRttProbe` 归并为泛型标量下行队列，但**不要顺手重构 `TryAcceptPong`** —— 它有 S4 的用例在守，改签名会波及那些用例。新增同构方法更安全。

### 两处必须提前知道的时序偏差

- **上行泵在 `Tick` 之后**（S4 决策，`BattleClientController.cs:226`）—— Ack 至多晚一帧释放
- **下行泵在 `Tick` 之前** —— 探测包释放同样至多晚一帧

**两项叠加：测得 RTT 比注入值高约两帧（≈67ms）。** 写用例必须留两帧余量，断言区间用 `[注入值, 注入值 + 2 × FixedDeltaMilliseconds]`。**漏掉会让延迟用例间歇性红。**

## 决策三：控制值用非对称滤波（升快降慢）

**放宽方向走快通道，收紧方向走慢通道。**

| 方向 | 用哪个估计 | 为什么 |
|---|---|---|
| **RTT 升 → 放宽超前量** | **EMA（`alpha=0.2`）立即响应** | 放宽是**安全方向**：宽一点只是多缓冲几帧，代价是输入延迟略增。窄了则输入迟到，服务端复用旧输入、角色卡顿且客户端无从知道原因 |
| **RTT 降 → 收紧超前量** | **窗口最小值，限速回落** | 收紧是**危险方向**，必须确认延迟真的持续降低了才收。抖动时不跟着抽 |

实现即一个上升快、下降慢的包络：

```
controlRttMs = max(emaMs, decayingEnvelope)
  decayingEnvelope 按不超过 TightenRateMsPerSec 的速率向 minMs 回落
```

**下游只消费 `controlRttMs`。** `MinMs` 仍然算、仍然出口，但用途是链路下界估计与诊断（「EMA 与 Min 长期贴近且都很高」是可疑信号）。

### 为什么不能直接用窗口最小值作控制基准

**这是本决策的核心约束，不满足它会造成真实的 gameplay 缺陷。**

窗口最小值对**上升沿有结构性滞后**：窗口长度 × 探测周期。16 样本配 1Hz 探测就是最坏 16 秒——连接从 5ms 阶跃到 200ms 后，窗口里的旧低值仍在，测得值仍报 5ms，这期间超前量按 5ms 算而真实链路要 200ms，**输入持续迟到**。

它还与决策五的上限语义耦合：若 target 作上限，估计器滞后期间上限被压在低位 → 客户端无法抬高 lead → 输入持续迟到。**这正是决策四里「代价不对称所以不做拒收」的那个失效模式，从判定路径绕回控制路径。**

而窗口最小值换来的抗篡改收益很小：本阶段不 enforce，滤波器只改变「延迟回 Ack 换更宽超前量」这条已知上限生效的**速度**，不改变它是否成立（决策一）。

**升快降慢同时满足**：上升沿不滞后、cap 语义安全、抖动时不来回抽。

### 探测频率

**`BATTLE_RTT_PROBE_FRAMES` 默认 `10`**（30Hz 下 3Hz）。1Hz 对控制回路太慢，无论用哪种滤波。

代价是探测流量 ×3 —— 每条 `S2C_RttProbe` 只有一个 `uint64`，3Hz 下双向合计约 100 B/s，相对快照流量可忽略。探测表上限 32 条对应约 10 秒未回执，仍然够用。

### 三个必须做的边界

1. **探测表要有上限**（32 条）。客户端不回 Ack 则表单向增长。超限丢弃最旧条目并计数。
2. **窗口最小值用固定长度环形缓冲**，不要用「历史全局最小值」。全局最小会把一次偶发低延迟样本永久固化成基准。
3. **`MinMs` 必须随窗口滑动回升**。这是上一条的另一面，用例专守。

时间源用 `SystemBattleClock`（`GameShared/FrameSync/Core/IBattleClock.cs`，S4 已建），无头用例注入 `FrameBattleClock`。**不引入新的时钟抽象。**

## 决策四：只打一行越界告警，不做判定与拒收

服务端观测到的超前量超出 RTT 能解释的上界时，打一行告警并计一个全局数。**行为上永不影响输入是否被接受。**

```
upperBound = MaxAcceptedInputBufferFrames(6)
           + ceil(WindowMs(200) / FixedDeltaMilliseconds) + 1     ← +1 为 LastFrameIndex 量化容差
           + ceil(controlRttMs / 2 / FixedDeltaMilliseconds)      ← RTT 放宽上界
```

告警格式照 `[Battle][HashMismatch]`（`BattleLogic.cs:275`）风格，带全部判定输入：

```
[Battle][InputLeadOutOfBounds] player=1 claimed=1042 serverFrame=1024 observedLead=18
  upperBound=13 controlRtt=8ms rttEma=9.2ms rttMin=7ms
```

### 为什么不做判定与拒收

- **没有真实作弊样本，阈值无法校准。** 用例只能断言「我写的规则等于我写的规则」，这不是验收。**这是主因。**
- **误判代价不对称。** 拒收诚实高延迟玩家的输入 → 服务端复用旧输入（`BattleLogic.cs:227`）→ 角色卡顿或走错方向，且客户端无从知道被拒，表现为无法解释的预测失配。
- 本 demo 无排名无经济，放过一个作弊者的实际损失为零。

**三条的顺序有讲究**：主因是工程上不可验收，后两条是佐证。反过来讲会变成「因为没人作弊所以不做」，那条推理站不住——按同一标准 S3 的哈希对账也该砍掉（也没有真实 desync 玩家）。

### 上界必须与既有硬闸有非空交集

`upperBound` 锚在 `MaxAcceptedInputBufferFrames(6)`，**不是 `MaxLeadFrames(20)`**。

原因是量纲：`MaxLeadFrames` 是 `clientLead`（客户端**发出时**的超前量）的顶，而 `observedLead` 是**到达服务端时**的超前量，两者相差约 `rtt/2`。闭环真正调节的是后者，稳态目标 `[4, 6]`。锚错会让告警区落在既有硬闸 `[1, 24]` 之外，整个机制永不触发。

**`LeadBoundsCalculator` 构造时必须断言 `MaxAcceptedInputBufferFrames + toleranceFrames < MaxFutureInputFrames(24)`**，即告警区非空。将来有人调宽 `WindowMs` 到让告警区归零，构造会抛异常而不是静默失效。

> 这条约束的完整推导与教训见 [设计教训-两类静默失效.md](设计教训-两类静默失效.md) 第一节。

### 超前量必须按有符号算

`claimedFrameIndex` 与 `LastFrameIndex` **都是 `uint`**。输入迟到时 `claimed <= LastFrameIndex`，裸减法下溢成接近 `uint.MaxValue` 的巨值，会被记成「超前越界」——**每一个正常的迟到输入都刷一条假告警。**

规则：

1. **只在 `claimedFrameIndex > LastFrameIndex` 时算超前量**，否则跳过。迟到输入由 `BattleLogic.cs:116` 的既有闸负责，本阶段不重复处理。
2. 算的时候**先转 `long` 做有符号差**，不在 `uint` 域里减。
3. **迟到输入不得增加 `LeadOutOfBoundsCount`。**

`BattleLogic.cs:116` 的 `_hasProcessedFrame` 门控允许首 tick 前的 frame 0 输入，此时 `LastFrameIndex = 0` 且 `claimed = 0`。规则 1 天然覆盖（`0 > 0` 为假），但用例要显式覆盖这个 case。

### 无样本时的行为

无样本时**不打告警、不参与上界计算**，前馈项退回兜底值。

`SimulatedClient` 永不回 Ack（见 Context），若把无样本当 `rtt=0`，`upperBound` 收紧到 13，5 个 scenario 会立刻开始刷告警。

## 决策五：`targetLeadFrames` 是 feedforward 下限 + 软上限

三种候选语义：

| 语义 | 做法 | 判断 |
|---|---|---|
| **硬目标** | 客户端实际 lead 必须收敛到 target | ❌ 会废掉 feedback 环。而 feedback 环消费的是服务端缓冲深度（`LatestAcceptedInputFrame`），比 RTT 前馈更贴近真实需求 —— 废掉它是退步 |
| **硬上限** | feedback 环不得突破 target | ❌ 估计器上升沿滞后期间上限被压低 → 客户端无法抬高 lead → 输入持续迟到。**任何 cap 语义在估计器上升沿滞后期间都不安全**（决策三） |
| **下限 + 软上限** | target 作 feedforward 下限；静态 `MaxLeadFrames` 换成 `min(MaxLeadFrames, target + slack)` | ✅ 选定 |

决策三的升快降慢消掉了上升沿滞后，cap 才变得可行——但仍要留 `slack`，因为 feedback 环的瞬态是合法的：

```
slack = MaxAcceptedInputBufferFrames(6) − MinAcceptedInputBufferFrames(4)
      + LeadDecreaseCooldownSnapshots(6) / 2
      = 5      ← 容纳闭环一次完整的 deficit 修正 + 冷却期

effectiveMaxLead = min(MaxLeadFrames(20), targetLeadFrames + slack)
```

**这是「软」上限**：只替换 `Math.Clamp` 的上界参数（`:597`、`:621`），不新增拒绝路径，客户端不会因为撞到它而失去输入。

### 口径：改的是前馈项数据源，不是控制权归属

**本阶段不是「控制权从客户端转到服务端」。** feedback 环本来就在消费服务端数据（Context 已列）。真实改动范围是：

> **feedforward 项的数据源** —— 从客户端自算 `_rttEmaMs`（客户端时钟）换成服务端实测 RTT（服务端时钟）。feedback 环不动。

**文档、完成标准与面试讲解都按这个口径。** 说成「控制权切换」会与代码结构不符，被追问就露。

### 服务端权威不产生新的检测能力

值得先说清，避免重复计数：作弊客户端根本不会理下发值，服务端要察觉偏离只能去测 `observedLead`，而那**就是决策四那行告警本身**。

**本阶段的检测能力全部来自决策四那一行，仅此。** 服务端权威改变的是前馈项数据源的可信度，不是检测面。

### 下发载体：`S2C_FrameSnapshot` 加顶层字段

**`S2C_FrameSnapshot` 已经是 per-session 构造的** —— `BattleComponent.cs:167` 进 `foreach`，`:175` 在循环体内 `new`。加一个顶层 `TargetLeadFrames` 字段天然 per-session，**零额外接线**：不用新 handler、不用新 gate、天然按帧对齐。

独立消息 `S2C_LeadDirective` 的代价是三处接线加一个「下发值与快照帧号不同步」的边界情形，换来的只是语义洁净。

而「快照不该带控制面语义」这个反对站不住：`PlayerSnapshot.LatestAcceptedInputFrame`（`:212`）本来就是控制面数据，feedback 环正在消费它。

### 消费必须在下行 gate 释放的回调里

照 `selfLatestAcceptedInputFrame` 的既有写法（`BattleClientController.cs:457-467`）：

```
OnSnapshotMessage
  ├─ :451  TryAcceptSnapshotMessage(nowMs)                ← 下行丢包判定
  ├─ :457  ConvertSnapshot(...) + 拷出 TargetLeadFrames    ← 与转换同时取值
  └─ :464  EnqueueConvertedSnapshot(nowMs, snapshot,
             value => { 应用 targetLead; EnqueueServerSnapshot(value, ...); })
                                                           ↑ 闭包捕获，释放时才生效
```

**若在消息到达时立即应用 target，快照被延迟而 target 提前生效**，弱网下两者错位，延迟类用例会间歇性红。

### 客户端自保

- `MinLeadFrames(3)` / `MaxLeadFrames(20)` 的 clamp **必须保留**，服务端下发值异常（版本不一致、恶意服务端）时自保。
- **`TargetLeadFrames = 0` 显式当「未启用」，退回客户端自算 RTT** —— 否则 `BATTLE_RTT_AUTHORITATIVE_LEAD=0` 或旧版服务端会把 baseline 压到下界，表现为持续输入迟到。

## 决策六：RTT 上 HUD，这是本阶段的画面产出

本阶段是纯后台逻辑，**除 HUD 外画面上看不见任何东西**。而 demo 的用途之一是录屏展示，所以 HUD 是交付物不是附赠品。

显示内容：`rttEma` / `rttMin` / `controlRtt`、下发的 `targetLead`、客户端实际 `leadFrames`、探测样本数。

**`controlRtt` 与 `rttMin` 分开显示是有意的**：阶跃上升时两者会明显分离（前者立即跟上、后者滞后），这一屏直接演示决策三的非对称滤波在做什么。

**`targetLead` 与实际 `leadFrames` 并列也是有意的**：它们不相等正是「target 是下限而非硬目标」的可视证据（决策五）。

弱网注入时这些值会一起动，是讲 S4 注入能力时最好的配图：注入 200ms → RTT 抬升 → 超前量放宽 → 输入仍被接受。**一屏说完整条因果链。**

面板归属见 S9，本阶段只做最小显示，不做布局打磨。

## 架构

```
新增：GameShared/FrameSync/Network/ServerRttTracker.cs      ← 探测表 + EMA + 窗口最小值 + 非对称包络
      GameShared/FrameSync/Network/IProbeNonceSource.cs     ← 生产 crypto / 用例 seeded
      GameShared/FrameSync/Network/LeadBoundsCalculator.cs  ← 纯函数算上界，可无头单测
      GameServer/Server/Entity/Battle/BattleRttConfig.cs    ← 环境变量
      GameServer/Server/Hotfix/Battle/Handler/C2B_RttProbeAckHandler.cs
      proto: S2C_RttProbe / C2B_RttProbeAck / S2C_RttStats
             + S2C_FrameSnapshot.TargetLeadFrames（唯一改既有消息处）

服务端每帧 Tick：
  BattleComponent.Tick(frameIndex, dt)
    ├─ SendRttProbesIfNeeded(nowMs)     ← 每 10 帧对每个 session 发一次，记 sentAt
    ├─ CleanupStaleProbes(nowMs)        ← 超时未回条目清掉，防表无界增长
    └─ _battleLogic.Tick(...)           ← 零改动

服务端收输入：
  C2B_PlayerInputHandler → BattleComponent.SubmitInput(session, input)
    ├─ claimed > LastFrameIndex 时算 observedLead（转 long）→ 越界则告警 + 计数
    └─ _battleLogic.SubmitInput(...)    ← 原调用不变，永不拦截

客户端：
  OnRttProbeMessage → 只取 ProbeNonce（ulong 标量）
                    ├─ gate.TryAcceptRttProbe(nowMs, nonce, 回调)    ← 下行排队
                    └─ 回调（下行释放时才执行）→ WrapSendRttProbeAck → 上行排队 → 真正 Send

  OnSnapshotMessage → 拷出 TargetLeadFrames → 与 converted snapshot 一起在
                      EnqueueConvertedSnapshot 释放回调里生效
```

`BattleLogic` **零改动**。`LeadBoundsCalculator` 与 `ServerRttTracker` 放 GameShared，走 `Entity.csproj:24` 的 `GameShared/**` 通配进服务端，同时被 `BattlePredictionSelfTestSuite` 直接单测——**理由与 S4 决策五相同**：接线层（`BattleComponent`）不在无头覆盖里，所以把可判断的逻辑挪到覆盖得到的地方，接线层只剩调用顺序。

## 实现步骤

### Step 1：proto 新增消息

`OuterMessage.proto` **末尾追加**（不要插中间——opcode 按声明顺序分配，插中间会让后续消息全部改号）：

```proto
// 服务端发起的 RTT 探测（单向）。nonce 为密码学随机 64 位，发出时刻仅存于服务端内存
message S2C_RttProbe // IMessage
{
	uint64 ProbeNonce = 1;
}

// 客户端回执（单向）。原样回传 nonce，不带任何时间戳
message C2B_RttProbeAck // IMessage
{
	uint64 ProbeNonce = 1;
}

// 服务端 RTT 观测值（单向，per-session 构造）
message S2C_RttStats // IMessage
{
	uint32 FrameIndex = 1;
	bool   Enabled = 2;
	bool   HasSample = 3;
	double RttMinMs = 4;          // 链路下界估计，诊断用
	double RttEmaMs = 5;
	double ControlRttMs = 6;      // 非对称滤波后的控制值（决策三），HUD 与 report 看这个
	int32  RttSampleCount = 7;
	int32  LeadOutOfBoundsCount = 8;
}
```

**并给既有 `S2C_FrameSnapshot` 加一个顶层字段**（决策五的下发载体）：

```proto
// 服务端权威的目标超前帧数。0 表示未启用，客户端退回自算
uint32 TargetLeadFrames = <下一个可用字段号>;
```

**这是唯一一处改既有消息。** 加顶层字段不影响既有字段号，proto3 向后兼容。**不要动 `PlayerSnapshot`** —— 它有 S3 留空不复用的字段号 5/8/9/10。

当前最大 opcode 是 `C2B_StateHashReport = 134227735`（`OuterOpcode.cs:21`）。改完跑 `GameServer/Tools/ProtocolExportTool/Run.bat`，一次刷新服务端与客户端四个生成文件。`uint64` 在本 proto 已有先例（`C2B_Ping.SendTimestampMs`、`C2B_StateHashReport.StateHash`）。

> **T1 必须先做**：T1 让 37 条用例全部走 proto 往返，本步新增的 `TargetLeadFrames` 才会被自动覆盖。顺序反了，这个字段就是又一个未被测到的字段。

### Step 2：`ServerRttTracker` 与 nonce 源

`GameShared/FrameSync/Network/ServerRttTracker.cs`，纯逻辑零引擎依赖。

| 成员 | 说明 |
|---|---|
| `RecordProbeSent(ulong nonce, long nowMs)` | 记入表，超上限丢最旧并计数 |
| `bool TryRecordAck(ulong nonce, long nowMs, out long rttMs)` | 命中则算 RTT、更新 EMA 与窗口、移除条目；未命中返回 `false` 并计 `UnknownAckCount` |
| `void CleanupStale(long nowMs, long timeoutMs)` | 清理超时未回条目，计 `TimedOutProbeCount` |
| `bool HasSample` / `float EmaMs` / `long MinMs` / `int SampleCount` | 观测出口 |
| **`float ControlRttMs`** | **控制出口**：`max(EmaMs, decayingEnvelope)`，包络按 `TightenRateMsPerSec` 向 `MinMs` 限速回落。**下游只用这个值** |

`IProbeNonceSource`：`CryptoProbeNonceSource` 用 `RandomNumberGenerator.GetBytes`，`SeededProbeNonceSource` 用 `DeterministicRandom.NextU64()`。

EMA 口径与客户端一致（`alpha = 0.2`，首个样本直接赋值），便于两侧数值对比。

**包络的时间基准必须来自 `IBattleClock`**，用 `nowMs` 差值 × 速率。不要实现成「每收到一个样本回落固定量」—— 那样探测丢包时回落速率会被动变慢，且无头用例里不可控。

### Step 3：`LeadBoundsCalculator`

纯静态函数 + `readonly struct` 参数：

| 字段 | 默认 | 说明 |
|---|---|---|
| `WindowMs` | `200` | 容差窗口，换算成帧时 `+1` 帧量化余量 |
| `FixedDeltaMilliseconds` | `33.333…` | 由 `DeterminismRules` 推导，**不写死 33**（S4 已踩过：900 帧漂 300ms） |
| `MaxAcceptedInputBufferFrames` | `6` | 取自 `InputBufferTuning.cs:13`，**不复制常量**。上界锚点是这个（决策四） |

返回 `upperBound` 与 `jitterAllowanceFrames`。**必须把算出的上界一起返回**，否则告警无法解释「为什么越界」，排查时只能重算一遍。

**超前量入参必须是有符号的**：签名收 `long observedLead`，或直接收两个帧号并在内部按 `claimed > lastFrame` 门控 + 转 `long` 相减。**不要收 `uint observedLead`** —— 那等于把下溢的机会留给调用方。

参数校验失败**抛异常，不静默 clamp** —— 与 S4 一致，配错参数会让整轮观测无意义。**构造时断言告警区非空**（决策四）。

### Step 4：服务端接线

`BattleRttConfig` 照 `BattleBandwidthConfig.cs` 的范本（`GetEnvBool` `:48` / `GetEnvUInt` `:76`，支持 `1/true/yes/on`）：

| 环境变量 | 默认 | 说明 |
|---|---|---|
| `BATTLE_RTT_PROBE` | `1` | 总开关。关闭时零开销，连探测都不发 |
| `BATTLE_RTT_WINDOW_MS` | `200` | 容差窗口 |
| `BATTLE_RTT_PROBE_FRAMES` | `10` | 探测间隔，30Hz 下 3Hz（决策三） |
| `BATTLE_RTT_PROBE_TIMEOUT_MS` | `5000` | 探测条目超时 |
| `BATTLE_RTT_TIGHTEN_RATE_MS_PER_SEC` | `20` | RTT 下降时收紧速率。宁大勿小，收太快会在抖动网络里反复抽紧 |
| `BATTLE_RTT_AUTHORITATIVE_LEAD` | `1` | 关闭时客户端退回自算 RTT |

**不照抄它的饿汉式静态 `Current`**（`BattleBandwidthConfig.cs:15`）—— 做成可变实例便于用例切换与 S9 运行时开关。

`BattleComponent` 改动：

1. 新增 `Dictionary<long, ServerRttTracker> _rttTrackersBySessionId`，`Join`（`:52`）建、**`CleanupDisconnectedPlayers`（`:119`）清** —— 漏了就是 session 级内存泄漏。该函数已在清 `_playerIdBySessionId` 与 buff 基线，照同一处加。
2. `Tick`（`:112`）内、`_battleLogic.Tick` 之前发探测与清理过期条目。
3. `SubmitInput`（`:82-95`）在 `_battleLogic.SubmitInput`（`:94`）之前算上界并告警，**永不拦截**。
4. `S2C_RttStats` **在 per-session 循环内按各自 tracker 构造**。不能照抄带宽统计的写法 —— 那是 `BattleComponent.cs:266` 构造一次后广播，带宽是全局量所以没问题，**但 RTT 是 per-session 量**，共用一条消息会让两个客户端收到同一份数据，弱网下只给一端注入延迟就完全看不出来。

### Step 5：客户端回执与消费

`BattleClientController`：

1. `RegisterSnapshotHandler`（`:425-441`）加 `S2C_RttProbe` 与 `S2C_RttStats` 的 handler，照 `_pongHandler` / `_bandwidthStatsHandler` 的三步写法（建字段、注册、置 `_registered` 标志），**并在 `:107-127` 的注销块里一并加** —— 漏注销会在场景重入时重复注册。那里是「三个 `if` + 统一清标志与字段」的结构，两处都要加。
2. `OnRttProbeMessage`：**只取 `ProbeNonce`（`ulong` 标量）**，不持有 `IMessage` —— 池化对象在回调返回后 `Dispose()`（S4 决策一）。
3. **双向过 gate**（决策二）：`TryAcceptRttProbe` 下行排队 → 释放回调里才生成 Ack → `WrapSendRttProbeAck` 上行再排一次。`_networkGate == null` 时兜底直发，照 `OnPongMessage:479-483`。`WrapSendRttProbeAck` 签名 `Action<ulong>`，与 `WrapSendPing`（`BattleNetworkGate.cs:51-60`）同构。
4. `OnRttStatsMessage`：字段值**拷进本地 struct**，池化对象不可持有。**这条消息只供 HUD 与 report，不承载 `targetLead`。**
5. **`TargetLeadFrames` 在 `OnSnapshotMessage` 里消费**（决策五）：`:457` 与 `ConvertSnapshot` 同时拷出，`:464` 的 `EnqueueConvertedSnapshot` 回调里与快照一起生效。
6. `RefreshBaselineLeadFrames`（`BattleSimulation.cs:589-603`）改为消费下发值作 baseline；`Math.Clamp` 上界改为 `effectiveMaxLead`。clamp 保留，`TargetLeadFrames = 0` 退回自算。
7. HUD 显示（决策六）。

### Step 6：无头用例

**注册两处，漏一处静默漏测**：`AllCaseNames`（`:18-57`）+ `RunCase` 的 switch（`:109-116`）。**只追加，不改既有 37 条。**

测量层（10 条）：

| 用例 | 断言什么 |
|---|---|
| `rtt-tracker-ack-roundtrip-measures-latency` | `RecordProbeSent(n, 0)` → `TryRecordAck(n, 120)` 得 `rtt=120`；未知 nonce 返回 `false` 且计 `UnknownAckCount` |
| `rtt-tracker-window-min-ignores-outlier-spike` | 窗口内注入一次高值，`MinMs` 不受影响；**且旧样本滑出窗口后 `MinMs` 会回升**（守 Step 2 边界 2、3） |
| `rtt-tracker-stale-probe-cleanup-bounds-table` | 只发不回 64 次，表不超上限，`TimedOutProbeCount > 0`，不抛异常 |
| `rtt-nonce-source-is-unpredictable-and-seeded-is-reproducible` | `CryptoProbeNonceSource` 连续 1024 个 nonce 无重复且非单调递增；`SeededProbeNonceSource` 同种子两次跑序列逐值相等 |
| `rtt-control-value-rises-fast-and-falls-slow` | **守决策三**。5ms → 200ms 阶跃：`ControlRttMs` 在两个探测周期内跟上。再回到 5ms：按 `TightenRateMsPerSec` 缓慢回落，**不得一步到位** |
| `rtt-lead-bounds-region-is-non-empty` | 0 / 200 / 400ms 三档下 `upperBound < MaxFutureInputFrames(24)`；**且告警区归零时构造抛异常** |
| `rtt-honest-lead-never-warns` | 闭环稳态 lead `[4,6]` 在三档 RTT 下全部不告警 —— **零误报是硬判据** |
| `rtt-no-sample-does-not-warn` | 无样本时不告警、不参与上界计算（守决策四那条全线回归风险） |
| `late-input-does-not-count-as-lead-out-of-bounds` | **守决策四下溢**。`claimed < LastFrameIndex` 与 `claimed == LastFrameIndex == 0` 两种输入，`LeadOutOfBoundsCount` 都不增加 |
| `rtt-injected-delay-raises-measured-rtt` | **端到端**。经 S4 gate 注入双向各 100ms，测得值落进 `[200, 200 + 2 × FixedDeltaMilliseconds]`（含决策二两帧偏差） |

`rtt-probe-passes-downlink-gate` 单列，**守决策二**：只注入**下行** 100ms（上行 0），断言测得值 `>= 100`。若探测包没过下行 gate，这条会测出约 0ms 而失败。

超前量语义层（6 条）：

| 用例 | 断言什么 |
|---|---|
| `authoritative-target-lead-is-applied-and-clamped` | 非零 target 确实改变 `_baselineLeadFrames`；`target < MinLeadFrames(3)` 与 `> MaxLeadFrames(20)` 都被 clamp |
| `zero-target-lead-falls-back-to-client-rtt` | `TargetLeadFrames = 0` 时 baseline 仍按客户端 `_rttEmaMs` 算，**不被压到下界** |
| `target-lead-soft-cap-allows-feedback-transient` | feedback 环因 deficit 冲高时 `effectiveMaxLead = target + slack(5)` 允许瞬态通过；超过才 clamp。**且被 clamp 不产生输入丢失** |
| `target-lead-decrease-does-not-drop-actual-lead-immediately` | target 降低时实际 `_leadFrames` 按既有 `LeadDecreaseCooldownSnapshots(6)` 节奏回落，**不瞬跳**（守下限语义，非硬目标） |
| `snapshot-target-lead-is-applied-after-downlink-gate` | **守决策五消费路径**。注入下行延迟后 target 的生效时刻与快照释放时刻一致；在消息到达时立即应用则失败 |
| `authoritative-lead-switch-off-clears-stale-target` | 开关由开转关后客户端不残留上一个 target |

合计 **17 条**，用例总数 **37 → 54**。

端到端用例照 `netsim-uplink-loss-causes-server-reuse-input`（`:1230-1310`）的 server+client 合成回路写法。**探测发送与 Ack 回执需在用例里手工接线**（`BattleComponent` 不在 `Entity.csproj` 链接列表里），这是分层的直接代价，接受。

**异常消息必须带全部判定输入**（claimed / serverFrame / bounds / rtt），照 `FixedPhysicsBitExact`（`:1068-1103`）把实际值写进消息。

### Step 7：既有用例回归与 report

**37 条既有用例的行为与两个哈希基线都不应变化。** 依据：`BattleLogic` 零改动、用例不经 `BattleComponent`、`SimulatedClient` 走无样本分支、proto 新增字段不影响既有序列化。

必须不变：`fixed-physics-bit-exact` = `0xD2120B5F0F5A5FE5`、Determinism scenario = `0xD01E17BC0F96A3EB`。**若变了，优先怀疑无样本分支没做对（把无样本当 `rtt=0`）或探测发送影响了 `Tick` 内执行顺序。**

report 侧：

1. `BattleAutomationClientSnapshot`（`BattleAutomationRuntime.cs:1723`）加 `rttProbeAcksSent`、`serverControlRttMs`、`appliedTargetLeadFrames`。
2. 新增场景 `two-client-rtt-probe`。**场景名要同时登记三处**（`TestRunner.cs:49` 的 `Execute` switch、`:1378` 的 `NormalizeScenario`、`:1463` 的 `TestScenario` 常量表）—— 只登记一处会让 `--scenario=all` 假 PASS，既有验收指南已列为 FAIL 判据。
3. `Run-BattleAcceptance.ps1` 加开关参数，塞进 `-CustomArgs`（`:105` 的 `ConvertTo-CustomArgsString`、`:194` 的 `Get-ScenarioCustomArgs`）。

**必须有一条断言是「测量确实在跑」**（`rttSampleCount > 0`）。否则总开关没生效时，所有判据都会以「一切正常」的姿态通过——与 S4 Step 7 同一类陷阱。

## 验证方式

- **无头**：`--mode=test --scenario=all`，54 条用例（37 + 17）全通过，退出码 `0`
- **既有基线**：两个哈希未变，37 条既有用例行为不变
- **确定性扫描**：`--mode=validate --duration-seconds 30 --update-hz 60`，Issues 零
- **弱网交叉**：`two-client-weaknet-delay` 下测得 RTT 与注入值一致（留两帧余量），诚实客户端零告警
- **阶跃响应**：运行中把注入延迟 0 → 200ms → 0，上升沿两个探测周期内跟上，下降沿按限速回落
- **per-session 归因**：只给一端注入延迟时，两个客户端 report 里的 `serverControlRttMs` 必须不同
- **HUD**：弱网注入时 RTT 与超前量可见变化（录屏素材）

## 完成标准

1. 服务端能独立测出每个 session 的 RTT，**协议里不传时间戳**，**挑战值是密码学随机 64 位 nonce**。
2. **探测包与 Ack 双向都过 gate** —— `rtt-probe-passes-downlink-gate` 通过，只注入下行也能测到延迟。
3. **控制值升快降慢** —— `rtt-control-value-rises-fast-and-falls-slow` 通过：200ms 阶跃在两个探测周期内跟上，回落按 `TightenRateMsPerSec` 限速。
4. `ServerRttTracker` / `LeadBoundsCalculator` 零 `UnityEngine` / `Fantasy` 引用，编进服务端参与无头测试。
5. **37 条既有用例行为与两个哈希基线全部未变。**
6. `rtt-no-sample-does-not-warn` 通过 —— 无样本不告警，5 个 scenario 不受影响。
7. `rtt-honest-lead-never-warns` 在 0 / 200 / 400ms 三档全部通过 —— **零误报是硬判据**。
8. **迟到输入不刷假告警** —— `late-input-does-not-count-as-lead-out-of-bounds` 通过，超前量按有符号算。
9. **feedforward 项数据源已换成服务端实测 RTT**，语义为**下限 + 软上限**（非硬目标、非硬上限）。六条语义用例全部通过：应用与 clamp、零值退回、软上限容纳闭环瞬态、target 降低不瞬跳、**target 与快照同时生效**、开关关闭不残留旧值。
10. 探测表与 RTT 窗口都有上限；session 断开时 tracker 一并清理。
11. `S2C_RttStats` **per-session 构造**，两个客户端的观测值可独立归因。
12. HUD 显示 `controlRtt` / `rttMin` 分离与 `targetLead` / 实际 `leadFrames` 并列。
13. 能说清测量拦得住什么（伪造时间戳、提前 Ack、伪造 nonce）、拦不住什么（**延迟回 Ack 换更宽超前量**、**沉默即无样本**），以及**为什么不做判定（不可验收，非不重要）**。

## 风险与对策

| 风险 | 对策 |
|---|---|
| **探测包不过下行 gate** | S4 下行注入是每类消息显式接线，新消息不接就不受影响。只包上行 Ack 会让 200ms 用例测出 100ms 并「通过」。需 `TryAcceptRttProbe`，且 **Ack 在下行释放回调里才生成**（决策二）；`rtt-probe-passes-downlink-gate` 专守 |
| **无 RTT 样本被当成 `rtt=0`** | 5 个 scenario 的 `SimulatedClient` 永不回 Ack，会全线刷告警。必须显式无样本分支（决策四）；`rtt-no-sample-does-not-warn` 专守 |
| **窗口最小值作控制基准** | 上升沿有结构性滞后（窗口长度 × 探测周期），永久延迟上升后输入持续迟到。用非对称滤波（决策三）；`rtt-control-value-rises-fast-and-falls-slow` 专守 |
| **包络按「每样本回落固定量」实现** | 探测丢包时回落速率被动变慢，且无头用例不可控。用 `IBattleClock` 的 `nowMs` 差值 × 速率（Step 2） |
| **`observedLead` 用裸 `uint` 减法** | 迟到输入下溢成巨值，**每个正常迟到输入都刷一条假告警**。只在 `claimed > LastFrameIndex` 时算，转 `long` 做有符号差。照 `IsFutureFrameRejected`（`BattleLogic.cs:556-559`）的符号位范例（决策四） |
| **上界锚在 `MaxLeadFrames`** | 量纲错（发出时超前量 vs 到达时超前量），告警区会落在既有硬闸 `[1,24]` 之外，机制永不触发。锚在 `MaxAcceptedInputBufferFrames(6)`，构造加非空自检（决策四）。**完整教训见 [设计教训-两类静默失效.md](设计教训-两类静默失效.md)** |
| **target 作硬上限** | 估计器上升沿滞后期间上限被压低 → 输入持续迟到 → 服务端复用旧输入、角色卡顿且客户端无从知道原因。选下限 + 软上限，slack 容纳闭环瞬态（决策五） |
| **target 作硬目标** | 会废掉 feedback 环，而它消费的服务端缓冲深度比 RTT 前馈更贴近真实需求（决策五） |
| **声称「控制权已切换到服务端」** | 与代码结构不符：baseline 只是下限，feedback 环随时可突破，且它本来就在消费服务端数据。口径是「feedforward 项换数据源」（决策五） |
| **服务端下发值异常 / 字段默认 0** | 客户端保留 clamp 自保；**`TargetLeadFrames = 0` 显式当「未启用」退回自算**，否则 baseline 被压到下界，表现为持续输入迟到（决策五） |
| **`targetLead` 在消息到达时立即应用** | 快照被 gate 延迟而 target 提前生效，弱网下两者错位。必须在 `EnqueueConvertedSnapshot` 释放回调里生效（决策五）；`snapshot-target-lead-is-applied-after-downlink-gate` 专守 |
| **断言 `rtt == 注入值`** | 下行泵在 Tick 前、上行泵在 Tick 后，各至多晚一帧，测得值偏高约 67ms。**留两帧余量**（决策二） |
| **探测表无上限** | 客户端不回 Ack 则单向增长。给 32 上限 + 超时清理 |
| **session 断开不清 tracker** | 内存泄漏。在 `CleanupDisconnectedPlayers`（`BattleComponent.cs:119`）里随 session 清 |
| **把测量塞进 `BattleLogic`** | 墙钟进确定性路径，哈希可复现性变可疑。`BattleLogic` 零改动 |
| **统计消息构造一次后广播** | 带宽是全局量所以能那么写（`:266`），RTT 是 per-session，共用会让两端 report 相同、无法归因（Step 4） |
| **新消息插在 proto 中间** | opcode 按声明顺序分配，后续消息全部改号 |
| **合成时钟用 33 而非 33.333** | 900 帧漂 300ms，与 `RefreshBaselineLeadFrames` 口径冲突 |
| **忘记 `LastFrameIndex` 量化容差** | 消息可在一帧内任意时刻到达，±1 帧误差恒存在。容差显式 `+1` 帧 |
| **新场景名只登记一处** | `--scenario=all` 假 PASS。三处都要登记 |
| 客户端 handler 漏注销 | 场景重入时重复注册。`:114` 附近一并加 |
| 参数校验静默 clamp | 配错参数让整轮观测无意义。校验失败抛异常 |
| 沿用饿汉式静态 `Current` | S9 要运行时切换，做成可变实例省一次返工 |
| 服务端运行中报 `MSB3027` | 先停服务端，或只 build `Entity.csproj` |

## 相关实现文件

| 文件 | 改动 |
|---|---|
| `GameShared/FrameSync/Network/ServerRttTracker.cs` | 新建，探测表 + EMA + 窗口最小值 + 非对称包络 |
| `GameShared/FrameSync/Network/IProbeNonceSource.cs` | 新建，`CryptoProbeNonceSource` / `SeededProbeNonceSource` |
| `GameShared/FrameSync/Network/LeadBoundsCalculator.cs` | 新建，纯函数算上界 + 告警区非空自检 |
| `GameShared/FrameSync/Network/BattleNetworkGate.cs` | 加 `TryAcceptRttProbe`（下行）与 `WrapSendRttProbeAck`（上行）。**不改既有 `TryAcceptPong`** |
| `GameServer/Tools/NetworkProtocol/Outer/OuterMessage.proto` | 末尾追加三条消息 + `S2C_FrameSnapshot.TargetLeadFrames`，跑 `Run.bat` |
| `GameServer/Server/Entity/Battle/BattleRttConfig.cs` | 新建，六个环境变量 |
| `GameServer/Server/Entity/Battle/BattleComponent.cs` | `:52` 建 tracker、`:119` 清 tracker、`:112` 发探测与清理、`:82-95` 越界告警、`:167-260` 循环内 per-session 构造统计消息与填 `TargetLeadFrames` |
| `GameServer/Server/Hotfix/Battle/Handler/C2B_RttProbeAckHandler.cs` | 新建，照 `C2B_StateHashReportHandler.cs` 的三行式 |
| `GameLogic/Battle/BattleClientController.cs` | `:107-127` 注销两条、`:425-441` 注册两条、`OnRttProbeMessage`（双向过 gate）、`OnRttStatsMessage`、`:457`/`:464` 消费 `TargetLeadFrames`、HUD |
| `GameLogic/Battle/BattleSimulation.cs` | `RefreshBaselineLeadFrames`（`:589-603`）消费权威值作 baseline，clamp 上界改 `effectiveMaxLead` |
| `GameLogic/Battle/BattlePredictionSelfTestSuite.cs` | **17** 条用例，只追加；注册两处（`:18` 数组 + `:109` switch） |
| `GameLogic/Battle/Automation/BattleAutomationRuntime.cs` | `:1723` 加三个字段；新场景判据 |
| `GameServer/Server/Entity/TestHarness/TestRunner.cs` | 新场景名登记三处 |
| `Tools/AutomationAcceptance/Run-BattleAcceptance.ps1` | 开关参数入 `-CustomArgs` |
| `GameServer/Server/Entity/Entity.csproj` | **无需改** —— 新文件都在 `GameShared/**`，已被 `:24` 通配覆盖 |

**不需要改**：

- **`BattleLogic.cs`** —— 确定性内核零改动。两道既有帧号闸（`:116`、`:128`）保持原样，本阶段只在其之前观测
- `StateHasher.cs` —— 口径不变
- `SimulatedClient.cs` 与 5 个 scenario 的 harness —— 走无样本分支，行为不变
- 既有 `C2B_Ping` / `S2C_Pong` 的收发 —— 决策五改的是消费方
- `NetworkConditionSimulator.cs` —— S4 的注入能力已够用

## 估时

**1.5 天。**

| 部分 | 估时 |
|---|---|
| Step 1 proto + 生成 | 1 小时 |
| Step 2-3 tracker + 非对称包络 + 上界计算 | 3 小时 |
| Step 4-5 服务端接线 + 客户端回执 + HUD | 2 小时 |
| 决策五 `targetLead` 下发与消费（含 gate 释放时序） | 2 小时 |
| Step 6 十一条测量用例 | 2 小时 |
| Step 6 六条语义用例 | 2 小时 |
| Step 7 回归 + report | 1 小时 |

## 验收指南

`Tools/AutomationAcceptance/服务端RTT测量-验收测试指南-<日期>.md`（随实现一并产出）。

必须包含：

1. **测量准确性** —— 三档注入延迟下测得值的期望区间，含两帧偏差的来源说明
2. **阶跃响应** —— 运行中把注入延迟 0 → 200ms → 0：上升沿两个探测周期内跟上，下降沿按 `TightenRateMsPerSec` 限速。**这条必须实机跑**，无头用例只覆盖纯逻辑层
3. **零误报判据** —— 诚实客户端在 0/200/400ms 下告警必须为零
4. **「测量确实在跑」的断言清单** —— 总开关未生效时所有判据都会假通过
5. **per-session 归因验证** —— 只给一端注入延迟时两个客户端观测值必须不同；相同则说明统计消息被广播复用
6. **无样本语义** —— 5 个 scenario 零告警、`rttSampleCount` 为 0
7. **抗篡改边界声明** —— 两条已知上限：「延迟回 Ack 换更宽超前量」、「沉默即无样本」。**都是有意取舍，缺 KCP 层测量不判 FAIL**
8. **语义声明** —— 明确写「target 是 feedforward 下限 + 软上限，不是硬目标」，并说明为什么不做硬上限。**验收 agent 不应按「实际 lead 收敛到 target」判 FAIL**

## 后续衔接

- **S6（跳过重放）** —— 与本阶段无耦合。验收标准三条并列（性能 + `HashNoRecordCount` 仍为 0 + 哈希一致率不变），见总索引
- **S7（击退）** —— 击退是服务端权威，不经输入通路，本阶段不影响它
- **S8（断线重连）** —— 重连后 session 变更，tracker 必须重建。`Join`（`BattleComponent.cs:61-70`）已有 session 复用分支，S8 要复核 tracker 在那条路径上的处理
- **lag compensation** —— 本阶段建的服务端 RTT 是它的前置。若将来做命中判定回退，直接消费 `ServerRttTracker.ControlRttMs`
- **旧计数器归因** —— `LateInputDropCount` / `FutureInputRejectCount` 仍是全局计数不分玩家（`BattleLogic.cs:53-55`）。改它要动确定性内核，刻意不做，记为遗留项
- **传输层 RTT 测量** —— 要堵住「延迟回 Ack」需在 KCP 层测，服务端 Fantasy 无源码（`Entity.csproj:13`）。换网络库或拿到源码后可重启
- **判定与拒收** —— 若将来 demo 变成有排名/经济的真实项目，判定就有了校准信号。**恢复前必读 [设计教训-两类静默失效.md](设计教训-两类静默失效.md) 第一节**
