# S1：预测误差平滑与 Ghost 可视化 实现计划

> 执行编号 **S1**（旧别名 `P4a` / HTML 优先级 `#4`）。编号体系见 [00-总索引.md](00-总索引.md)。
> 紧接的下一项是 **S2**（物理层重写），本阶段一行都别碰物理层。

## Context

当前 demo 的权威修正是**可见硬跳**：`BattleClientController.SyncRendering()`（`:432-448`）把逻辑坐标直接写进 `sphere.transform.position`，中间没有任何插值层。每次 `ReconcileAuthoritativeSnapshot()` 应用权威快照并重放后，球会瞬移到新位置。

这带来两个问题：

1. **已完成的工作看不见**。预测、回滚重放、哈希对账、脏同步都已实现，但画面上只能看到「球在动」和「球偶尔跳一下」。面试时无法用画面讲解，只能读日志。
2. **误差不可观测**。Source 教材第八章的判断是：「预测误差是个不可见的量，不打印出来你根本不知道有没有、有多大」，并把误差可视化列为「不是『以后再优化』的事，是『没有它就无法开发』的事」。当前项目正处于这个状态。

本阶段对应 Timeline 的 P4 前半部分，拆出来单独做，因为它是后续所有同步工作的观测基础。

## 为什么排在占位分离改造之前

上一轮分析曾建议先把 `ResolvePlayerOccupancy` 搬到输入端（照 TF2 `AvoidPlayers` 把分离量写进 `CUserCmd` 的做法）。核实 Source 源码后**调整了顺序**，理由如下：

TF2 能把分离量做成纯客户端输入，前提是 `tf_avoidteammates=1` 时**队友之间根本不 solid**（`c_tf_player.cpp:7707` 的 `ShouldCollide` 直接返回 false）。推力只是「软避让」，算错了后果仅是推得多一点少一点，不违反任何约束。

本项目的占位分离是**硬约束**——玩家不允许重叠。硬约束下客户端拿旧的远端位置去算，必然偶尔算错，而且错了不能放任（会出现重叠）。Source 对 solid 实体的处理方式是：**服务端权威 + 客户端预测 + 误差平滑掩盖残差**，而不是把约束挪进输入。

所以正确顺序是先有平滑层，再决定占位分离的语义（硬约束保持服务端权威，或降级为 TF2 式软避让）。没有平滑层时，任何一种选择都会表现为硬跳。

## 目标

1. 权威修正在画面上表现为**平滑收敛**而非瞬移，同时**不改变仿真结果**。
2. 自身球的最近权威位置以半透明 ghost 显示，预测与权威的偏差**肉眼可见**。
3. 误差量级可读（数值面板），为后续弱网调试提供依据。

## 非目标

- 不做远端玩家插值（远端目前直接用权威快照位置，属 P4 后半部分）。
- 不做轨迹线显示。
- 不改任何仿真逻辑、快照结构、哈希输入或网络协议。
- 不动 Box2D 去留（独立决策，见 `Box2D确定性分析/README.md`）。

## 核心设计

### 关键约束：平滑只存在于渲染层

这是整个方案的前提。仿真侧的修正必须保持**即时、精确、逐位可复现**，否则会破坏哈希对账和回滚重放。平滑量是一个**纯视觉偏移**，不回写任何参与 `StateHasher` 的状态。

对应 Source 的做法：`prediction.cpp` 的 `CheckError()` 只比位置一个字段，算出的是「视觉平滑量」，仿真本身已经被修正到权威值。

因此：

- 逻辑位置（`PlayerState.X/Y`，`Fixed64`）—— 修正后立即等于权威值，不受平滑影响
- 渲染位置（`transform.position`，`float`）—— 等于逻辑位置 + 衰减中的误差偏移

### 数据流

```
权威快照到达
  → ApplyAuthoritativeSnapshot + 重放到预测帧   （仿真侧，即时精确）
  → 记录「修正前渲染位置」与「修正后逻辑位置」之差 = 初始误差偏移
  → 后续每个渲染帧，偏移按时间衰减趋近 0
  → SyncRendering 写入 transform 时叠加当前偏移
```

**采样时机**：偏移必须在**重放完成后**采集，不是应用快照的瞬间。因为重放会把状态从权威帧推进到当前预测帧，玩家实际看到的是重放后的结果。

**采样点的精确位置（2026-08-03 review 修正，别写错）**：`ReconcileAuthoritativeSnapshot` 有**两个出口**，不是一个。

```
:422  _authoritativeSnapshots.Save(...)
:423  ApplyAuthoritativeSnapshot(snapshot)
:424  DiscardPredictionsAtOrAfter(...)
:433  if (replayTargetFrame > snapshot.FrameIndex)
:436  {   AdvancePredictionTo(...)   }
:437  ← ★ 采样代码插这里（两个分支的公共路径上）
:439  if (!logRollback) { return; }      ← 提前返回，绝大多数帧走这里
:443  ...回滚计时/计数/日志（仅 mismatch 时）
:451  }                                  ← 真正的函数尾部
```

**「插在函数尾部」会让采样代码在绝大多数帧上根本不执行。** `logRollback` 实参就是 `mismatch`（`:322`），一致时为 `false`，命中 `:439` 提前返回。

更要命的是：本计划推荐的测试构造模板 `AuthoritativeSnapshotRestoresPhysicsWorld()`（`:339-386`）**自己就落在这个分支** —— 它 `SetJoined` 后没先 `Tick(11,...)` 就直接 `EnqueueServerSnapshot(...,11)`，第 11 帧无预测记录 → `CheckConsistency` 走 `_skippedNoRecord` 分支返回 `false` → `logRollback=false` → 提前返回。照字面理解插在尾部，用例 `error-smoothing-does-not-affect-logic-state` 断言「偏移非零」那一半会必然失败。

**采样必须放在 `:437` 之后、`:439` 判断之前。**

为什么两条 `logRollback=false` 的路径都该采样：

| 路径 | `mismatch` | 该不该采样 |
|------|-----------|-----------|
| 比对过，一致 | false | **该**。上一帧渲染位置可能仍带旧残余偏移，重算得到衰减中的残值，正是期望行为 |
| 无预测记录可比（`_skippedNoRecord`） | false | **该**。`ApplyAuthoritativeSnapshot` 照样覆写了逻辑位置，渲染位置需要平滑跟上 |
| 比对过，不一致 | true | 该。这是最初设计的主路径 |

**与 S5 的接口约定（现在就钉死，避免将来互相踩）**：S5 要在一致路径上跳过重放。**S5 的短路判断必须放在本阶段采样代码之后**，不能放在它之前，否则会把采样一起短路掉。同理 S5 也不能整体在函数最前面 `return`（那还会连 `_authoritativeSnapshots.Save()` 一起跳掉，破坏 S3 依赖的快照历史连续性）。

**衰减方式**：按 `Time.deltaTime` 做指数或线性衰减，时长取一个可调常量（建议 100~150ms 起步，对应 3~5 个逻辑帧）。衰减发生在渲染帧率上，与逻辑帧解耦。

**修正重叠是常态，不是边界情况。** 权威快照 30Hz 到达（每 33ms 一个），而平滑窗口 100~150ms —— 上一次平滑远未走完，下 3~5 次修正就已到达。所以必须明确重叠语义，否则会写成累加（偏移越滚越大）或直接覆盖（残余偏移被丢弃，产生二次跳变）。

**采用「按上一帧渲染位置重设基准」，不累加、不覆盖**：

```
新偏移 = 上一帧实际绘制的位置 − 本次修正后的逻辑位置
衰减计时器重置
```

关键点：「上一帧实际绘制的位置」本身已经含有旧的残余偏移，所以旧偏移被自然吸收进新偏移，无需单独记账。这个公式对首次修正（残余为 0）和重叠修正是同一套逻辑，不需要分支。

因此渲染层每帧写完 transform 后，要把「本帧实际绘制的自身位置」缓存下来，供下次修正采样使用。这个缓存是纯视觉用途，不进快照、不进哈希。

**大误差直接放弃平滑**：误差超过阈值时（传送、重生、初次进场）直接硬跳，因为平滑一个巨大的偏移比瞬移更难看。Source 的 `cl_smooth` 同样有最大距离限制。

**阈值取 `6.0`，做成命名常量，并在注释里标注「待 P2 击退距离定稿后复核」。**

这个值不能随便取，因为 P2 已确定加入**撞击击退**且**服务端权威、客户端故意不预测**（见 `物理层与玩法形态-讨论记录-20260803.md` 决策 1 与 §4.6）。也就是说击退产生的位置修正**正是本平滑层要吸收的最大一类误差** —— 本阶段从「让回滚不那么难看」升级成了「P2 击退演示能否成立的前置条件」。

约束是：**阈值必须大于单次最大击退距离**。否则击退会走硬跳分支，直接毁掉讨论记录 §2 描述的目标场景：

> 两客户端对撞，各自预测自身 Dash、均不预测击退，服务端裁决后两端同时被修正；左侧硬跳变、右侧平滑恢复，并排对比

击退超阈值的话，「右侧平滑恢复」也变成硬跳，对比就没了。

参考尺度：房间半宽 12、半高 8、玩家半径 0.45（`GameplayRoomSettings.cs:10-12`）。`6.0` 是房间半宽的一半，对当前只有移动的场景足够宽松，同时给击退留了余量。P2 定稿击退距离后必须回来核这个值。

### 为什么平滑量不能放 GameShared

`GameShared/` 是双端共享的确定性逻辑层，服务端通过通配符全量链接（`Entity.csproj:31`）。把一个 float 平滑器放进去，风险是后续有人把它纳入快照或哈希，破坏确定性。

平滑器放 `GameLogic/Battle/`（客户端侧），与 `BattleSimulation.cs` 同级。

### 可测性设计

平滑逻辑必须能在无头环境验证，因此拆成一个**不依赖 UnityEngine 的纯 C# 类**：输入是「初始误差、已过时间、总时长」，输出是「当前偏移」。Unity 侧只负责喂 `Time.deltaTime` 和把结果加到 `transform`。

注意 `Entity.csproj:32-33` 是**逐文件显式链接** GameLogic 源码，不是通配符。新增的平滑器文件必须手动加一行 `Compile Include`，否则服务端无头测试编译不到。这是本方案最容易踩的坑。

## 实现步骤

### Step 1：纯逻辑平滑器

新建 `GameLogic/Battle/PredictionErrorSmoother.cs`，纯 C#，无 UnityEngine 引用。

职责：持有当前误差偏移与剩余时间；提供「注入新误差」「按 dt 推进衰减」「读当前偏移」三个操作；内置大误差阈值判定（超阈值则偏移直接归零，等价于硬跳）。

两个常量都要命名并带注释：平滑时长（建议 120ms 起步）、大误差阈值（`6.0`，注释标注「待 P2 击退距离定稿后复核，必须大于单次最大击退距离」）。

在 `Entity.csproj` 增加对应 `Compile Include` 行。

### Step 2：接入对账路径采集误差

在 `BattleSimulation.ReconcileAuthoritativeSnapshot()`（`:415`）里，**`:437` 之后、`:439` 的 `if (!logRollback) return;` 之前**，计算自身玩家「修正前的渲染位置」与「修正后逻辑位置」之差，注入平滑器。

**插错位置是本方案第二大坑**（第一大是漏加 `AllCaseNames`）。详见「核心设计」的采样点段落——放在函数真正的尾部会让采样在绝大多数帧上不执行，且推荐的测试模板恰好命中那条提前返回。

需要在 `BattleSimulation` 里缓存上一次渲染用过的自身位置，作为「修正前」基准。这个缓存是纯视觉用途，不进快照。

对外暴露只读的当前偏移与误差量级，供渲染层和调试面板读取。

**新增状态必须在 `SetJoined()` 里重置。** `SetJoined`（`:207-246`）是重新加入战斗（含断线重连）时的完整状态重置点，现有代码在那里重置了 `_leadFrames`、`_selfPredictions`、`_authoritativeSnapshots` 等全部状态。本阶段新增的两样东西要跟上：

- 「上一帧绘制位置」缓存 —— 不重置的话，重连后第一次对账会拿**上一局**的陈旧渲染位置去算偏移，注入一个与真实误差无关的量
- 平滑器的当前偏移与衰减计时器 —— 同理

大误差阈值能把这种情况兜底成硬跳（不会发散），但那是撞在保护网上，不是正确行为。

### Step 3：把 transform 写入搬到渲染帧率

**这一步是方案里最容易做错的地方。**

现状：`SyncRendering()` 由 `BattleClientController.Tick()` 调用（`BattleClientController.cs:192`），而 `Tick()` 来自 `ClientTickDriver` 的 dispatcher —— 它把 `Time.deltaTime` 累积到 30Hz 才 `TickOnce()`（`ClientTickDriver.cs:84`）。也就是说 `SyncRendering()` **跑在 30Hz 逻辑帧上，不是渲染帧率**。

如果平滑衰减和 transform 写入都留在这里，平滑本身会被切成 33ms 一跳，等于没平滑。

**还有一层：一次渲染帧内 `Tick()` 会被调用多次。** `ClientTickDriver.Update()`（`:77-98`）除了按时间累积触发，后面还有一段独立的追帧循环：

```csharp
_dispatcher.Update(Time.deltaTime);        // 按累积时间触发 0~N 次
int catchUpCount = Math.Min(frameGap, MaxCatchUpTicksPerFrame);  // 上限 8
for (int i = 0; i < catchUpCount; i++) { _dispatcher.TickOnce(); }
```

而追帧目标由 `BattleClientController.Tick()` 反向设置（`:195-198` 的 `SetTargetFrame`），形成反馈环。追帧只在客户端落后于服务端目标帧时触发 —— 也就是**弱网场景**，恰好是本方案要验证平滑效果的那个场景。

**次数上限别当硬边界**（2026-08-03 review 修正）：弱网追帧路径确实是 8 次上限，但 `_dispatcher.Update(Time.deltaTime)` 那一步自己还有独立上限 —— `TickAccumulator` 的 `DefaultMaxDeltaTime = 0.5f`（`TickAccumulator.cs:9`）除以 33.333ms 约 15 次（`:41` `_maxTicksPerUpdate`，`:73-75` 钳制）。两段叠加理论上限接近 **23**，不是 9。日常弱网只走追帧路径，9 这个数字对讨论追帧场景够用，但**不要拿它当断言的边界值**。

这对「重设基准」的影响：同一渲染帧内若发生多次对账，每次注入都会读同一个「上一帧绘制位置」缓存（因为渲染层还没来得及刷新它）。**这是正确行为，不要试图在 Tick 内更新该缓存** —— 缓存的语义是「玩家上一次实际看到的位置」，一个渲染帧内玩家只看到一次画面，所以同帧多次对账共享同一基准是对的。最终偏移由最后一次对账决定，中间几次被覆盖，这正是期望结果。

因此职责要拆开：

- **`Tick()`（30Hz）**：更新各玩家的逻辑目标位置缓存；对账发生时注入误差偏移
- **`Update()`（渲染帧率，MonoBehaviour 原生）**：用 `Time.deltaTime` 推进衰减，并写 `transform.position`

自身球写「逻辑目标 + 当前偏移」。远端球本阶段仍直接写逻辑目标（不插值），所以远端保持 30Hz 步进 —— 这是已知缺口，归入 S9。

**怎么拆那个循环（review 补充）**：现有 `SyncRendering()`（`:432-462`）单个循环里同时做三件事 —— 写位置（`:446`）、按需创建球体（`GetOrCreateSphere`，`:445`）、以及不在场玩家的失活处理（`:450-461`）。

拆分方式：**创建与失活留在 `Tick()`，只把位置写入搬到 `Update()`。** 理由是创建/失活由「玩家集合发生变化」驱动，那是逻辑帧事件，30Hz 足够；而位置写入需要渲染帧率。

这样拆的好处是 `Update()` 只做「读缓存 + 叠偏移 + 写 transform」，不含任何创建逻辑，避免了「球体在两处都可能被创建」和「`Update()` 先跑但球体还没创建」这两类边界问题。`Update()` 里遍历时对找不到球体的 playerId 直接跳过即可 —— 下一个逻辑帧 `Tick()` 会补上。

注意 `Update()` 里已有输入采集逻辑（`:145-161`），追加渲染写入时不要打断原有顺序。

### Step 4：Ghost 显示

复用 `GetOrCreateSphere()`（`:772`）的创建思路，新增一个半透明 ghost 球，只为自身玩家创建。材质半透明，与实心预测球区分。ghost 不参与任何逻辑。

**ghost 必须用独立字段，且必须在 `DisposeController()` 里显式销毁。** 自身 playerId 已经占用了 `_playerSpheres` 字典的槽位，ghost 只能挂一个单独的 `GameObject` 引用字段。而 `DisposeController()`（`:85-143`）现在只遍历 `_playerSpheres` 执行 `Destroy`（`:128-136`）—— 新字段不加一行显式销毁，退出战斗或重连时会有一个 ghost 球残留在场景里。

**位置数据需要新增对外入口。** ghost 要显示「最近一次权威快照里自身玩家的坐标」，该数据不需要新增网络字段（快照里已有），但 `_authoritativeSnapshots` 是 `BattleSimulation:38` 的私有字段，**当前没有任何对外读取入口**（`public` 面只有 `EnqueueServerSnapshot`）。

所以要在 `BattleSimulation` 加一个只读入口，暴露「最近一次已应用的权威快照中自身玩家的位置」，照现有 `public X => _x` 的属性模式（`:102-116`）。建议直接缓存该坐标而不是暴露整个 `SnapshotBuffer`——后者会把内部结构泄漏给渲染层，且调用方还得自己找帧号和玩家。

### Step 5：调试数值

沿用 `BandwidthStatsUI` 的既有模式（`IBattleUI` 事件 + UI 订阅），补充误差相关字段：最近一次修正量级、平滑剩余时间、累计回滚次数（`_rollbackCount` 已存在）、最近回滚帧。

本阶段只做误差相关字段，P4 完整面板（ServerFrame / RTT / TargetLeadFrames / LastMismatchField 等）留后续。

### Step 6：自测用例

注册两处（见交接说明第六节，`TestRunner.cs` 不用改）：`BattlePredictionSelfTestSuite.cs` 的 `AllCaseNames` 数组（`:10-30`）和 `RunCase()` 的 switch（`:47`）。**漏加 `AllCaseNames` 会导致用例静默不跑而 `--scenario=all` 仍报 PASS。**

### 两条硬约束：只追加，且断言必须是相对的

**S2** 要拿掉 Box2D 改手写定点物理（`物理层与玩法形态-讨论记录-20260803.md` 决策 2），它的连带影响明确写了「`BattlePredictionSelfTestSuite` 中依赖物理层的用例需复核」。本阶段和它会碰同一个文件，所以：

**1. 只在 `AllCaseNames` 末尾追加、只加新的 switch 分支，不修改任何既有用例。** 现有 19 个用例一行都别动。物理重写时那些用例要复核，两边各改各的才不会互相踩。

**2. 四条新用例全部写成相对断言，不硬编码绝对坐标。** 具体指：偏移是否归零、位置在衰减前后是否相同、偏移是否等于「上一帧绘制位置 − 新逻辑位置」——都是同一次运行内前后比较，不写死任何具体的 `m_rawValue` 数值。

第 2 条本来是为了绕开那个自相矛盾的断言（见下面用例三），现在多一层收益：物理重写改的是位置的**产生方式**（去掉 `Fixed64 → float → Fixed64` 往返），不改本阶段边界上的**类型**（仍是 `Fixed64`）。所以相对断言能原样存活，不必跟着重写。

反例参照：既有的 `room-boundary-clamps-player` 断言了具体 `m_rawValue`，去掉 float 往返后那个值会变，属于物理重写要复核的对象。别学它。

四条用例：

- `error-smoothing-decays-to-zero`：注入固定误差，推进足够时长后偏移必须**精确归零**（不是渐近趋零）。
- `error-smoothing-large-error-snaps`：超阈值误差注入后偏移立即为 0。

  **注入值要明显超过阈值，不要贴着边界。** 阈值 `6.0` 就用 `20.0` 之类。理由：P2 定稿击退距离后阈值可能上调，贴边界写的话那次调参会让这条用例莫名变红，排查时容易误判成平滑器坏了。
- `error-smoothing-does-not-affect-logic-state`：**这条是本方案的核心保护，但断言方式要注意。**

  不能写成「注入误差 vs 未注入误差，两者逻辑状态相同」——注入误差的前提是发生了对账，而对账本身就会改变逻辑状态，所以「未注入」的对照组不存在，那样写要么写不出来要么恒真。

  正确断言是**在同一次运行内验证两件事同时成立**：

  1. 对账 + 重放完成后，自身玩家逻辑位置的 `Fixed64.m_rawValue` 与该权威快照中的对应值**逐位相等**（证明修正精确落在权威值上，没被平滑量污染）
  2. 同一时刻平滑器的当前偏移**非零**（证明平滑确实在生效，第 1 条不是因为平滑没启动而恰好通过）

  两条缺一不可：只有第 1 条会在平滑器根本没工作时假通过；只有第 2 条不能证明逻辑未被污染。

  **构造前提：必须让重放真实发生，即 `replayTargetFrame > snapshot.FrameIndex`。** `AdvancePredictionTo` 是条件调用（`BattleSimulation.cs:433`）。如果构造出「预测帧 == 权威帧」的场景，重放根本不发生，「重放完成后」这个时刻不存在，于是第 1 条恒真（没有偏差需要修正）、第 2 条恒假（没有误差被注入），用例失去意义还可能被误判成「平滑器坏了」。

  照抄 `AuthoritativeSnapshotRestoresPhysicsWorld()`（`:339-386`）的构造方式即可 —— 它让 `currentFrame(12) > snapshot.FrameIndex(11)`，产生 1 帧重放。

  > **不要用 `_leadFrames > 0` 当判据**（2026-08-03 review 修正，本计划先前的说法是错的）。`_leadFrames` 从 `SetJoined`（`:216`）起就恒为 `[MinLeadFrames=3, MaxLeadFrames=20]` 区间内的值（`InputBufferTuning.cs:9-10`，所有更新点都过 `Math.Clamp`），**永远不会是 0**，拿它验证构造是否正确会因恒真而查不出问题。真正决定重放是否发生的是 `ApplyPendingServerSnapshot` 里逐快照算出的 `replayTargetFrame`（`:308-316`），`_leadFrames` 只在 while 循环结束后（`:335-340`）参与计算反馈给 `ClientTickDriver` 的追帧目标，不参与循环内每个快照的重放判断。

逐位断言的**技巧**参照 `room-boundary-clamps-player`（`:122-133`），但**场景构造**要参照 `AuthoritativeSnapshotRestoresPhysicsWorld()`（`:339-386`）——前者直接 new 一个 `FrameSyncPhysicsWorld` 就测，不涉及 `BattleSimulation`、对账和重放，照抄它的场景搭不起来。

另外补一条覆盖重叠场景（重叠是常态，见「核心设计」）：

- `error-smoothing-overlapping-corrections-rebaseline`：连续两次修正间隔小于平滑窗口时，第二次的偏移必须等于「上一帧绘制位置 − 新逻辑位置」，而不是两次误差之和。

## 验证方式

- 无头：`--mode=test --scenario=all` 全绿，且四条新用例通过。
- 真机：单客户端移动时画面无抖动；手动触发回滚（`ReconcileAuthoritativeSnapshot` 的 manual 入口，`:271`）时球平滑收敛而非瞬移；ghost 与实心球在无弱网时基本重合。
- 弱网：Clumsy 加 150ms 延迟，ghost 明显落后于实心球，修正时实心球平滑靠拢。

## 完成标准

1. 权威修正不再产生可见瞬移。
2. `error-smoothing-does-not-affect-logic-state` 通过，证明平滑层未污染确定性。
3. 不看日志能从画面区分预测位置与权威位置。
4. 现有 4 个物理/边界用例与全量 `--scenario=all` 保持通过。

## 风险与对策

| 风险 | 对策 |
|------|------|
| 平滑量污染哈希 | 平滑器不进 `GameShared`；专门用例断言逻辑状态逐位不变 |
| 新文件服务端编译不到 | `Entity.csproj` 显式加 `Compile Include`（逐文件链接，非通配符） |
| 采样时机错误导致偏移方向反 | 采样点固定在重放完成后，非应用快照瞬间 |
| **采样点插在函数尾部导致绝大多数帧不采样** | 必须插在 `:437` 之后、`:439` 的 `if (!logRollback) return;` **之前**。推荐的测试模板恰好命中那条提前返回，插错会让新用例必然失败且看起来像平滑器坏了 |
| 重连后拿上一局渲染位置算偏移 | 新缓存与平滑器状态在 `SetJoined()`（`:207-246`）里一并重置 |
| ghost 球退出战斗后残留 | `DisposeController()` 加显式 `Destroy`，现有代码只清 `_playerSpheres` 字典 |
| 平滑衰减跑在 30Hz 上，等于没平滑 | transform 写入必须搬到 `Update()`（渲染帧率），`Tick()` 只更新逻辑目标缓存。见 Step 3 |
| 大误差平滑观感更差 | 阈值内平滑、阈值外硬跳 |
| 用系统 dotnet 编译报 `NETSDK1045` | csproj 是单目标 net9.0，只有 Rider 那套 `C:/Users/shiwe/.dotnet/dotnet.exe`（SDK 9.0.316）能编。命令写全路径 |
| 服务端运行中导致 `MSB3027` 锁文件 | 跑 `Main.csproj` 前先停服务端；只验证编译用 `build Entity.csproj` |
| `AllCaseNames` 漏加导致用例静默不跑 | 注册两处都要做，且 `--scenario=all` 报 PASS 不代表新用例跑了 |
| 追帧时一渲染帧多次 Tick | 同帧多次对账共享同一「上一帧绘制位置」基准，这是正确行为，别在 `Tick()` 里刷缓存 |

## 相关实现文件

- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Battle/BattleSimulation.cs`（对账/回滚，`:415`）
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Battle/BattleClientController.cs`（渲染同步，`:432`）
- `UnityProject/Assets/GameScripts/HotFix/GameLogic/Battle/BattlePredictionSelfTestSuite.cs`（自测注册两处：`AllCaseNames` `:10-30` + `RunCase()` `:47`）
- `GameServer/Server/Entity/Entity.csproj`（源码链接，`:29-33`）

> `TestRunner.cs` **不需要改**。已核实 `room-boundary-clamps-player` 在该文件 grep 零命中，新用例照此办理即可。详见交接说明第六节。

## 验收指南

`Tools/AutomationAcceptance/预测误差平滑-验收测试指南-20260803.md`

## 后续衔接

本阶段完成后，占位分离的语义决策才有意义。届时两条路都能看到真实表现差异：

- 保持硬约束 + 服务端权威 → 平滑层负责掩盖残差
- 降级为 TF2 式软避让（`ShouldCollide` 返回 false + 推力进输入）→ 客户端可完整预测

Source 走的是前者对 solid 实体、后者对队友，本项目按 demo 需要选一种即可。
