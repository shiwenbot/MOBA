# S8：Dash / 体力 / Recover / 撞击击退 实现计划

> **状态**：未开始。执行编号 **S8**，见 [00-总索引.md](00-总索引.md)。对应 Timeline 的 **P2**。
> **前置**：S1（误差平滑，击退误差靠它吸收）必须已完成 —— 已于 2026-08-04 落地。S7 正在施工中，本阶段用例基数须在 S7 落地后重数。
> **相关**：[设计教训-两类静默失效.md](设计教训-两类静默失效.md) —— 本阶段新增两个状态机与一条服务端权威通路，**动手前读第一节**。
> **行号约定**：S7 正在改 `BattleSimulation.cs` / `BattleClientController.cs` / `BattleComponent.cs`，本文所引行号会漂。**关键位置一律按方法名定位，动手前重核。**
> **修订（2026-08-06，独立复审）**：经代码核实修正六处，用例 21 → 26，估时 3-4 天 → 4-4.5 天。两处阻断级：**体力必须是两个 `AttributeKind`**（`MaxStamina` 无 kind 则无法独立重算，「Buff 提升上限」落空）、**`NumericSnapshot` 的两个体力 base 必须协议化**（漏则 `RestoreRuntimeState` 里紧跟的 `Recalculate` 当场把体力算成 0）。四处高风险：**`Decay` 必须紧跟 `Apply`**（排在触发后则首帧速度从未参与积分）、**`resetVelocity` 不等于只读**（需显式 `IsKinematicObstacle`）、**`DetermineSeparationNormal` 是 private 需抽出**（否则只能复制，违反本计划自己的约束）、**Dash 按键须走既有 `InputBuffer`**（否则丢按键或长按连发）。另修正验收口径为固定场景定量阈值，并更正「零输入常量方向」的理由（是确定性要求，非抗丢包）。

## 本阶段做什么

把 demo 从「同步 Transform」升级为「同步 Gameplay 行为状态」，并给出一个明确标注「故意不预测」的对照项。

四件事：

1. **Dash** —— Shift 触发，消耗体力，起手帧锁定方向，若干帧内高速位移。**全程客户端预测。**
2. **体力** —— 按逻辑帧恢复，不足则无法 Dash。**全程客户端预测。**
3. **Recover** —— Dash 结束后进入硬直，期间禁止再次 Dash。**全程客户端预测。**
4. **撞击击退** —— 两球相撞由服务端权威裁决。**触发不预测，演化照常预测**（决策三，与 Timeline 原表述有出入，见该决策）。

前三项演示「能预测就预测」，第四项演示「不该预测的别预测」。**本阶段是 S1~S11 里唯一的玩法增量**，也是 S11 录屏的主要素材来源。

## Context

以下事实均已在当前工作区逐条核对。**其中前两条推翻了既有文档的表述。**

### 玩家间碰撞一直在跑 —— S6 决策六的前提是错的

S6 决策六写着「S2 拆掉 Box2D 后 `FrameSyncPhysicsWorld` 只做定点积分，玩家之间无碰撞」。**这条不成立。**

`FrameSyncPhysicsWorld.cs` 现为 **555 行**（S2 计划称缩到 102 行，该数字已过期），其中包含：

| 成员 | 作用 |
|---|---|
| `ResolvePlayerOccupancy` | 圆-圆穿透分离，4 次迭代，按移动意图分配分离量 |
| `RebuildOccupancyContacts` | 每帧重建接触对，进 `PhysicsWorldSnapshot.Contacts` |
| `MinimumPlayerSeparation` | `PlayerRadius × 2 = 0.9` |

`git log -S "ResolvePlayerOccupancy"` 只有一条命中：`f71857a9`（v0.4a Box2D 接入）。**即它从 v0.4a 就在，S2 重写时被保留** —— S2 拆掉的是 Box2D 求解器，不是玩家间分离。

**真实后果是 S6 引入了一处不对称，而 S6 计划因为前提错误没有预见它**：

- **服务端** `BattleLogic` 持有全部玩家的 body，`ResolvePlayerOccupancy` 正常跑，重叠的两球被推开
- **客户端** S6 后 `_worldState` 只剩自己，`_bodies.Count == 1`，该函数在 `_sortedBodyIdsBuffer.Count <= 1` 处直接早退

于是两球接触期间，客户端预测「能走过去」、服务端把它推回来，**每帧失配、每帧回滚**。单人 demo 与当前 5 个 scenario 都看不见（没有两球贴身场景），但 S8 的击退演示必然让两球接触。**这是本阶段必须处理的既有缺口，不是本阶段引入的**，见决策五。

### `ApplyBodyImpulse` 已存在但生产零调用，且语义只够单帧

`FrameSyncPhysicsWorld.ApplyBodyImpulse` 与 `IPhysicsMovementWorld` 上的声明都在，但全仓生产调用为零 —— 唯二命中是 `BattlePredictionSelfTestSuite` 与 `SnapshotSelfTestSuite` 的桩。

它的语义是**当帧速度增量**：`Step` 把 `_pendingImpulses` 与移动输入相加写进 `LinearVelocity`，**末尾 `_pendingImpulses.Clear()`**。

两个直接推论：

1. **它承载不了跨帧击退** —— 下一帧就没了。
2. **它不进快照** —— 回滚重放时凭空丢失。

所以击退的**持续状态必须放 gameplay 层**（`PlayerState` + 快照），每帧由 gameplay 重新喂给物理。`ApplyBodyImpulse` 作为「本帧额外速度」的入口**正好可以复用**，不需要新增物理 API —— 它和移动输入相加的行为就是击退需要的语义，物理层保持无状态。

### SkillGraph 的运行时状态不进快照

`BattleLogic.SubmitInput` 已带 `skillId` 参数（`C2B_PlayerInput.SkillId = 5` 也已在协议里），`QueueSkillRequests` → `_skillGraphRuntime.QueueSkillRequest` 通路是通的。**但 `BattleSkillGraphRuntime`（`GameShared/SkillGraph/RuntimeSkillGraph.cs:455`）的 `_activeExecutionsByCasterId` 没有任何 Capture / Restore 方法**，客户端回滚时只能整体 `Clear()`（`ApplyAuthoritativeSnapshot` 里就是这么做的）。

这违反 Timeline 技术约束 3：「进入预测链路的状态必须纳入快照、哈希、一致性检查、历史缓存和回滚重播」。**所以 Dash 不能挂 SkillGraph**，见决策一。这与 Timeline 把它定为 `PredictableAction`「不命名为技能」的原意一致。

### 属性系统现状：5 项，体力需要新增

`AttributeKind`（`NumericModifier.cs:9-16`）只有 `Health` / `MaxHealth` / `Mana` / `MaxMana` / `Attack`；`PlayerAttributeDirtyFlags` 对应 5 个 bit，`All` 是这 5 位的并。

`Mana` / `MaxMana` 已在协议、脏同步、哈希、Numeric 修饰器全通路上跑，**但 gameplay 没有任何消费方** —— 体力可以复用它，也可以新增第 6 项。见决策二。

### 一致性比较与哈希的覆盖范围不同，新字段要加两处

| 函数 | 覆盖 |
|---|---|
| `ArePlayerSnapshotsEquivalent` | 位置、5 个属性、`NextRuntimeBuffId`、Buff 列表、Numeric 修饰器。**不含 `PhysicsSnapshot`** |
| `StateHasher.Hash` | 上述玩家字段 **+ `PhysicsSnapshot`** 的 body 位置/速度/角速度/旋转/两个 bool + 接触对 |

**任何新增字段必须同时进这两处。** 漏 `ArePlayerSnapshotsEquivalent` → 该字段失配不触发回滚；漏 `StateHasher` → S3 哈希对账放过该字段的错误。两处各配专项用例。

### 平滑阈值与击退距离的硬约束

`PredictionErrorSmoother`：`SmoothingDurationSeconds = 0.12f`、`MaxSmoothingDistance = 6.0f`，`magnitude > MaxSmoothingDistance` 走硬跳。

房间与运动参数：`RoomHalfWidth = 12`、`RoomHalfHeight = 8`、`PlayerRadius = 0.45`、`DeterminismRules.MoveSpeed = 5`。

**`MaxSmoothingDistance` 必须大于单次击退造成的最大瞬时预测偏差**，否则击退走硬跳，毁掉 S1 的并排对比演示。这是 Timeline P2 的「反向约束」，本阶段必须显式验算，见决策四。

### 用例基数

当前 `AllCaseNames` 实际 **54 条**（与 S6 计划一致）。

> **S7 计划里「37 → 56」的 37 是 S5/S6 之前的旧基数**，已于本次一并修正为 54。故 **S7 落地后应为 73 条**，本阶段在 73 上追加。**动手前重数一遍，不要沿用任何文档里的数字。**

## 目标

1. **Dash / 体力 / Recover 全程客户端预测** —— 弱网下不等服务端确认，三者进快照、哈希、diff、回滚重播。
2. **撞击击退服务端权威** —— 触发不预测，误差走 S1 平滑层，不出现硬跳。
3. **修掉决策五那条接触期持续 mismatch** —— 击退演示里回滚次数应与碰撞次数同量级，不是与击退帧数同量级。
4. **mismatch 日志能区分字段** —— `PositionMismatch` / `StaminaMismatch` / `StateMismatch`（Timeline 完成标准明列）。
5. **画面产出** —— Dash 拖影、体力条、击退时 ghost 与实体分离。这是 S11 录屏的主素材。

## 非目标

- 不做 SkillGraph（决策一）、不做技能表现资源。
- 不做伤害、命中、目标选择，不新增 Buff 配置。击退**不走 Buff 系统**（决策三）。
- 不做击退的**触发**预测（刻意选择，非缺陷）。
- 不做远端插值曲线（S10）。本阶段别人的显示位置仍是最新权威值。
- 不动 S3 哈希口径、不动 S7 的 RTT 通路。
- 不做体力 UI 的布局打磨（S10/S11）。

## 决策一：Dash 是 `PlayerState` 的一等状态机，不挂 SkillGraph

`PlayerState` 新增三个字段，全部进快照、哈希、diff：

| 字段 | 类型 | 说明 |
|---|---|---|
| `DashPhase` | 枚举 `None` / `Dashing` / `Recovering` | 当前相位 |
| `DashPhaseRemainingFrames` | `int` | 当前相位剩余帧 |
| `DashDirX` / `DashDirY` | `Fixed64` | **起手帧锁定**的方向 |

### 方向在起手帧锁定，Dash 期间不跟随输入

**这是可预测性的要求，不是手感偏好。** 若 Dash 期间跟随输入转向，则服务端在丢包复用旧输入时（`BattleLogic.cs` 的 `ReusedInputCount` 分支）方向会与客户端不同，**每次上行丢包都产生一次方向级 mismatch**。锁定后 Dash 是「起手帧输入 + 固定帧数」的纯函数，与后续输入是否送达无关。

代价是不能中途转向。对本 demo 无影响。

### 状态机推进顺序：先递减、后起手

顺序错了会有两个具体 bug：

- **先起手后递减** → 起手帧就被扣掉一帧，Dash 实际少一帧
- **不分离两步** → 同一帧「Recover 结束」与「新 Dash 起手」会同时成立，Recover 形同不存在

约定顺序：**递减当前相位 → 相位到期则转移（`Dashing` → `Recovering` → `None`）→ 判定本帧能否起手**。

### 零输入时起手方向定为常量

起手帧输入为零向量时，**不拒绝 Dash**，方向取常量 `(1, 0)`。

理由是**确定性**，不是抗丢包：零向量没有定义良好的归一化结果，两端必须约定同一个值，否则各自的兜底写法一旦不同就是永久分歧。约定成常量则两端必然一致。

> **这条不是抗丢包措施，别那么讲。** `DashRequest` 与方向在同一条 `C2B_PlayerInput` 里，一起到达、一起丢失。丢包时服务端走复用分支，而 `ConsumedInput`（`BattleLogic.cs:615-625`）只存 `Dx`/`Dy`、**不含 `DashRequest`** —— 所以服务端不会复用出一次 Dash，客户端起手而服务端没有，这是丢包的正常后果，靠回滚修正，与方向常量无关。
>
> 手感上「零输入时向右冲」是个可议的选择（也可以改成朝最近一次非零方向），但那是手感问题，**不影响可预测性**。

### 为什么不走 Buff 系统

Buff 系统（`BuffSystem.cs` 503 行）功能上够用，但会引入 `BuffId` 配置、命令队列、相等性判定三层间接，且把 Dash 的时序绑到 `ProcessBuffCommands` 的执行顺序上 —— 多一个出错点，收益为零。三个字段 + 逐帧递减更直接。

## 决策二：体力走新增 `AttributeKind.Stamina`，不复用 `Mana`

两种候选：

| 方案 | 判断 |
|---|---|
| 复用 `Mana` / `MaxMana` | ❌ 协议零改动、通路现成，但**诊断输出会说谎** —— mismatch 日志、HUD、report 里全是 `mp:`，讲解时得每次口头翻译成体力。且将来真要加法力就得再拆一次 |
| 新增 `Stamina` + `MaxStamina` **两个 kind** | ✅ 选定 |

新增的收益是白拿现有属性通路：**脏同步、B1 的 baseline 帧号校验、每 60 帧周期性全量恢复、Numeric 修饰器**（将来「Buff 提升体力上限」直接可用）、以及自动进哈希。

### 必须是两个 `AttributeKind`，不是一个

**当前值与上限在本项目里是两个独立 kind** —— `Mana` / `MaxMana` 就是这么建的（`NumericModifier.cs:9-16`）。`GetBaseValue`（`NumericState.cs:54`）、`SetBaseValue`（`:67`）、`CalculateFinalValue` 的修饰器筛选（`:159`）全部以 kind 为键。

所以只加 `Stamina = 6` 却要两个值是自相矛盾的：`MaxStamina` 没有 kind 就无法被独立重算，也无法被修饰器命中 —— 上面刚说的「Buff 提升体力上限」直接落空。

**定为 `Stamina = 6` 与 `MaxStamina = 7` 两个 kind，脏标记也拆成两位。**

### 改动点清单（漏一处就是静默失效）

| 位置 | 改动 |
|---|---|
| `AttributeKind` | 加 `Stamina = 6`、`MaxStamina = 7` |
| `PlayerAttributeDirtyFlags` | 加 `Stamina = 1 << 5`、`MaxStamina = 1 << 6`，**`All` 必须一并更新** |
| `PlayerAttributeSnapshot` | 加两字段 + 构造参数 + `Default` |
| `PlayerAttributeSync` | `ComputeDirtyMask` 加两项比较、`Merge` 加两项分支 |
| `PlayerState` | 加两属性 + `SetAttributeValue` 的两条 switch 分支 |
| **`NumericState.GetBaseValue`** | **加两条 switch 分支**（漏则 base 恒读 0） |
| **`NumericState.SetBaseValue`** | **加两条分支**，每条都要重新构造完整的 7 字段快照 |
| **`NumericState.Recalculate`** | **加两行**：`maxStamina = Max(0, CalculateFinalValue(MaxStamina))`、`stamina = Clamp(CalculateFinalValue(Stamina), 0, maxStamina)`，照 `Mana` 的写法 |
| **`StateHasher.Hash`** | **玩家段与 `Numeric.BaseAttributes` 段各加两处** |
| **`AreAttributesEqual`** | **加两项比较** |
| `OuterMessage.proto` | `PlayerSnapshot` 末尾追加 25 / 26；**`NumericSnapshot` 末尾追加 `BaseStamina` / `BaseMaxStamina`**（见下节） |
| `BattleSnapshotProtocolMapper` | `PlayerSnapshot` 与 **`NumericSnapshot`** 的 To / From 各加两项 |

**`SetBaseValue` 的写法有个坑**：它每条分支都 `new PlayerAttributeSnapshot(...)` 把其余字段原样传回。加两个字段后**每条既有分支都要多传两个参数** —— 漏改任何一条会让该属性被设值时把体力清零。构造函数加参数会让漏改处编译失败，**这是好事，不要给构造函数加默认值绕过它**。

### `NumericSnapshot` 的 base 也必须协议化

当前协议只有 `BaseHealth` ~ `BaseAttack` 五项（`OuterMessage.proto` 的 `NumericSnapshot`）。**只同步最终 `Stamina` 而不同步 `Numeric.BaseAttributes.Stamina` 会必然失配，不是可能失配。**

链条是：`PlayerState.RestoreRuntimeState`（`PlayerState.cs:106-107`）在 `Numeric.RestoreSnapshot` 之后**立刻调 `Numeric.Recalculate(this)`**，而 `Recalculate` 走 `CalculateFinalValue` → `GetBaseValue` 取的是 **base**。base 是 0 则最终体力被算成 0（无修饰器时 `finalValue = base`），**当场覆盖掉刚同步来的权威体力值**。

所以每次客户端应用权威快照（`RestoreSelfOnly` → `RestoreRuntimeState`）体力都会被清零，然后下一帧又失配。表现为「体力永远是 0 且回滚不停」。

需追加：`NumericSnapshot.BaseStamina` / `BaseMaxStamina` 两字段、mapper To/From 两侧、以及专项用例 `numeric-stamina-base-proto-roundtrip`。

`AttributeBroadcastBaseline` 无需改动：它存的是整个 `PlayerAttributeSnapshot` 结构体，不逐字段拆解，新字段自动跟随。

### 恢复必须是整数节律，且计数器必须进快照

**不要用「每帧加 0.x 点」的小数累加器。** 那样累加器自身也成了逻辑状态，必须进快照与哈希；漏了则回滚后累加器错位、体力逐渐漂移 —— 这类 bug 表现为「偶发的体力 mismatch」，极难定位。

改为整数节律：`StaminaRegenCounterFrames` 每帧 +1，达到 `StaminaRegenIntervalFrames` 时体力 +`StaminaRegenAmount` 并清零计数器。

**该计数器是 `PlayerState` 的一等字段，必须进快照、哈希、diff。** 它不走属性脏同步（否则脏位几乎每帧都置），单独占快照字段。

**Recover 期间照常恢复体力** —— Recover 的作用是禁止连续 Dash，不是中断恢复。

## 决策三：击退是**触发不预测、演化照常预测**

**这一条与 Timeline 的原表述有出入，以本决策为准。**

Timeline P2 写的是「客户端不预测击退」。若按字面实现成「客户端对击退全无感知」，后果是：击退持续 8 帧，则这 8 帧客户端每帧都算出「没被击退」的位置、每帧被权威修正，**画面上是连续 8 次抖动而非一次干净的位移**，且 `rollbackCount` 与 `consistencyMisses` 每次碰撞暴涨 8。演示效果与 S6 刚拿到的收益一起毁掉。

改为拆两段：

| 阶段 | 谁做 | 客户端预测? |
|---|---|---|
| **触发**（判定两球相撞、算冲量方向与大小） | 服务端独占 | ❌ **不预测** |
| **演化**（击退速度逐帧衰减、位移积分、到期清零） | 两端同一份代码 | ✅ **照常预测** |

做法是把击退状态放进快照：`KnockbackVelX` / `KnockbackVelY` / `KnockbackRemainingFrames`。客户端**不实现触发**，但在收到含击退状态的权威快照后，重放时按同一份衰减规则演化。

于是：**触发帧吃一次 mismatch（拿到击退状态），后续帧不再失配。** 回滚从「每击退帧一次」压到「每次碰撞约一次」。

**这让完成标准从「看起来不抖」变成可断言的数字**（见完成标准 6）。

### 讲解口径

对外说的是「**击退的裁决权在服务端，客户端不猜谁撞了谁；但拿到裁决结果后，客户端和服务端跑同一套衰减，所以只在裁决那一帧修正一次**」。

**不要说「客户端完全不预测击退」** —— 与代码不符，被追问「那为什么只回滚一次」就露。

### 为什么不走 Buff 系统

同决策一：会把击退时序绑到 Buff 命令队列的执行顺序上。且击退是**每帧衰减的连续量**，Buff 的 `RemainingFrames` + 修饰器模型表达它要绕一层。

### 复用 `ApplyBodyImpulse`，不新增物理 API

每帧由 gameplay 把当前 `KnockbackVel` 通过 `ApplyBodyImpulse` 喂给物理，物理照旧与移动输入相加、当帧清空。**物理层保持无状态，持续状态全在 gameplay 层且在快照里。**

这样占位分离（`ResolvePlayerOccupancy`）也自动对击退生效 —— 击退把球推向对手时仍会被正确分离。

### 触发按「新接触」判定，不是「正在接触」

`RebuildOccupancyContacts` 每帧重建接触对，**接触持续期间每帧都在集合里**。若按「集合里有就触发」，则贴身期间每帧重新灌满击退速度，**击退永不衰减、两球持续弹开**。

必须维护「上一帧接触对集合」，只对**本帧新出现**的对触发。该集合是服务端 `BattleLogic` 的私有字段，不进快照（客户端不做触发，不需要它）。

## 决策四：击退参数与 S1 平滑阈值联立，且在构造期断言

Timeline P2 的反向约束：`MaxSmoothingDistance = 6.0` 必须大于单次击退造成的最大瞬时预测偏差。

### 参数取值与验算

| 参数 | 取值 |
|---|---|
| `KnockbackSpeed` | `12`（≈ `MoveSpeed × 2.4`） |
| `KnockbackFrames` | `8`（30Hz 下约 0.27s） |
| `KnockbackDecayNumerator / Denominator` | `3 / 4`（每帧 ×0.75，整数比避免定点除法歧义） |

单次击退总位移是等比级数：`12 × (1/30) × (1 + 0.75 + 0.75² + ... )`，8 项和约 `3.75`，故总位移约 **1.5**。

最坏瞬时偏差不是总位移，而是**客户端在收到击退状态前累积的偏差**。上界为击退全程位移 + 该期间客户端按无击退预测的位移差，保守估约 **2.8**（RTT 高到 `MaxLeadFrames = 20` 帧未确认时）。

**2.8 < 6.0，留约 2 倍余量。**

### 断言必须在构造期，不是文档里

`KnockbackTuning` 静态构造里断言 `估算最大偏差 < PredictionErrorSmoother.MaxSmoothingDistance`，**越界抛异常，不静默 clamp**。

理由是这条约束**极易在调参时被破坏且破坏后症状隐蔽** —— 有人为了"手感更爽"把 `KnockbackSpeed` 调到 30，击退就开始走硬跳分支，画面上是瞬移，但**所有无头用例照样全绿**（哈希只管一致性，不管好不好看）。这正是[设计教训](设计教训-两类静默失效.md)第一节那类失效：机制完整自洽、有测试、但判据绕过了真实区间。

**调参后必须重跑该断言。** 写进验收指南。

## 决策五：客户端把别人镜像成只读碰撞 body

这是修 Context 第一条那个既有缺口。四种选择：

| 方案 | 判断 |
|---|---|
| 什么都不做 | ❌ 接触期每帧 mismatch。击退演示必然触发，且会掩盖击退本身的 mismatch 信号 |
| 客户端也不做分离，服务端也关掉 | ❌ 两球可完全重叠，画面上穿模，演示效果差 |
| 别人重新进 `_worldState` | ❌ 直接退回 S6 之前，别人重新参与回滚，S6 白做 |
| **别人作为只读 body 进物理世界** | ✅ 选定 |

做法：每次应用权威快照后，把 `RemotePlayerBuffer` 里每个玩家的最新权威位置写进物理世界对应 body（`SetBodyTransform`，`resetVelocity: true`）。

三条边界：

1. **只读、纯障碍物** —— 不进 `_worldState`、不进 `_selfPredictions`、不参与 `AdvancePredictionTo`、不参与回滚。它只是让自己的球「撞得到」。
2. **位置恒取最新权威值，不插值不推进** —— 插值归 S10。
3. **随 buffer 移除而清理 body** —— 否则留下隐形墙，重开局后撞空气。`RemotePlayerBuffer` 移除条目处同步 `RemoveBody`。

### 「只读」必须在物理层显式建模，`resetVelocity` 不够

**`SetBodyTransform(resetVelocity: true)` 只清速度，不会让 body 免于被推。** `CalculateSeparationShares` 在双方都没有「朝向对方的意图」时 `moveBodyA = penetration * Half` —— 各分一半穿透量。

镜像 body 的 `requestedVelocity` 恒为零（不在 `_pendingLinearVelocities`、也无 impulse），所以 `GetTowardIntent` 恒返回 0、永不 pushing。于是：

- 自己**朝向**对方移动时：自己 pushing → `moveBodyA = penetration` 全由自己承担 → 镜像不动 ✓
- 自己**静止或朝其他方向**、而权威位置更新让两者重叠时：两者都不 pushing → **各分一半 → 镜像 body 被推走** ✗

第二种情形在 `AdvancePredictionTo` 重放 `_leadFrames` 帧的过程中每帧都可能发生，镜像位置会在两次权威快照之间持续漂移。**所以「静态障碍物」这个说法与现有物理层的实际行为不符**，用例名若叫 `remote-body-mirrored-as-static-obstacle` 就是名不副实。

**必须显式建模。** 最小改动：`FixedPhysicsBody` 加 `IsKinematicObstacle` 标志，`CalculateSeparationShares` 里若一方带该标志则**全部穿透量由另一方承担**（等价于无限质量）。该标志需进 `PhysicsBodySnapshot` 与哈希吗？**不需要** —— 服务端从不设它（服务端所有 body 都是真实玩家），它是纯客户端本地概念，不进快照就不影响哈希基线与两端对账。

`FrameSyncPhysicsWorld` 需要一个 `SetBodyKinematicObstacle(int bodyId, bool value)` 入口。**这是本阶段唯一必要的物理 API 新增**（Context 第二条说的「不新增物理 API」指的是不为击退新增，此处是为决策五）。

### 残差有界但不为零

客户端用的是延迟约 RTT/2 的对手位置，分离结果必然与服务端不同。**残差从「完整穿透深度」缩小到「对手在 RTT/2 内的移动距离」**，量级降一档但不归零。

**写进验收指南，不判 FAIL。** 彻底消除需要预测别人，那与 S6 的整个方向相反。

### 与 S6 决策六的关系

S6 决策六的结论「客户端零碰撞代码、不维护远端物理体」**建立在"玩家之间无碰撞"这个错误前提上**，本阶段修正为上述镜像方案。

**但 S6 的核心原则保留且未被削弱**：别人不参与预测、不参与回滚、不进 `_worldState`。改的只是「别人在客户端物理世界里有没有一个只读的碰撞占位」。

**需回写 S6 计划决策六一条勘误。**

## 决策六：击退判定进 `BattleLogic`，哈希基线**必须**变

S3~S7 一律要求「`BattleLogic` 零改动、哈希基线不变」。**本阶段相反。**

击退判定是 gameplay 规则，本身就属于确定性内核。它与 S7 拒绝的东西性质不同：S7 要塞进去的是**墙钟时间**（破坏可复现性），本阶段塞进去的是**纯几何判定**（完全确定性）。

代价是两个哈希基线合法变化：`fixed-physics-bit-exact`（当前 `0xD2120B5F0F5A5FE5`）与 Determinism scenario（当前 `0xD01E17BC0F96A3EB`）。新增快照字段必然改变哈希，**这是预期结果，不是 bug**。

### 验收判据是「变一次后稳定」，不是「不变」

流程：实现完成后跑一次记录新基线 → **再跑一次确认逐位一致** → 更新用例常量与验收指南。

**只跑一次就更新是错的** —— 会把一次偶发结果固化成基准（同 S7 决策三边界 2 的教训）。

### 必须同步改三份既有验收指南

**这是本阶段最容易漏的交付物。** 三份既有指南写的都是「基线必须不变」，其中 S3 那份是明文强判据：

| 指南 | 现状 | 需改成 |
|---|---|---|
| `哈希上报与服务端告警-验收测试指南-20260804.md` | **「与 S1/S2 相反：哈希基线必须不变」** | 注明 S8 是唯一例外 |
| `回滚粒度改造-验收测试指南-20260806.md` | `fixed-physics-bit-exact` 基线必须不变 | 同上 |
| `弱网自动化测试-验收测试指南-20260805.md` | 用例数与基线 | 更新数字 |

漏改的后果是**验收 agent 照指南判 FAIL 而实现是对的**，然后有人回头去"修"一个不存在的 bug。

## 决策七：输入用独立 `DashRequest` 布尔，不复用 `SkillId`

`C2B_PlayerInput.SkillId = 5` 已存在，复用能省一个字段。但：

- Dash **不是技能**（Timeline 明确「不命名为技能」）
- `SkillId > 0` 会被 `QueueSkillRequests` 路由进 SkillGraph，得加排除逻辑
- 回滚时 SkillGraph 走 `Clear()`、Dash 走快照恢复，**两套语义挤在一个字段里**

新增 `bool DashRequest = 6`，与 `SkillId` 并存互不干扰。

### 必须存进 `_inputHistory`，否则 Dash 在重放时消失

`BufferedInput`（`BattleSimulation.cs:1152`）与服务端 `PendingInput` 都要加该字段，`SaveInputHistory` / `_onSendInput` / `Tick` 签名一并更新。

**漏了的症状极具误导性**：首次预测有 Dash，回滚重放时 `_inputHistory` 里没有 → 重放结果与首次预测不同 → **每次回滚都产生一次新 mismatch**，看起来像「随机失配」或「网络问题」，实际是输入历史缺字段。

`BattleLogic.SubmitInput` 有一个 **test-only 的 `float` 重载**，`DashRequest` 要在**两个重载**上都作为末位参数追加 —— 否则测试与生产走不同参数语义，这类错误正好被用例本身放过。

## 架构

```
新增：GameShared/FrameSync/Battle/DashPhase.cs          ← 枚举
      GameShared/FrameSync/Battle/DashTuning.cs         ← Dash / 体力常量
      GameShared/FrameSync/Battle/KnockbackTuning.cs    ← 击退常量 + 与平滑阈值的联立断言
      GameShared/FrameSync/Battle/DashSystem.cs         ← 纯静态：推进相位、判定起手、解析移动速度
      GameShared/FrameSync/Battle/StaminaSystem.cs      ← 纯静态：整数节律恢复
      GameShared/FrameSync/Battle/KnockbackSystem.cs    ← 纯静态：逐帧应用与衰减（两端共用）
      GameServer/…/BattleLogic 内私有：上一帧接触对集合 + 触发判定（服务端独占）

服务端 BattleLogic.Tick 内顺序：
  DashSystem.Advance(state)                  ← 递减 → 相位转移
  DashSystem.TryStart(state, input)          ← 判定起手、扣体力
  移动速度 = DashSystem.ResolveVelocity(...) ← Dashing 用锁定方向 × DashSpeed，否则用输入
  KnockbackSystem.Apply(state, physics)      ← 当前击退速度 → ApplyBodyImpulse
  KnockbackSystem.Decay(state)               ← 紧跟 Apply，见下方「衰减必须紧跟 Apply」
  physics.Step(dt)                           ← 内含占位分离
  SyncPlayerStatesFromPhysics()
  ── 以下服务端独占 ──
  DetectNewContactsAndTriggerKnockback()     ← 新接触 → 写双方 KnockbackVel（本帧不再被 Decay）
  StaminaSystem.Tick(state)

客户端 ApplyLocalPrediction 内：同上，但**跳过 DetectNewContacts**，并在应用权威快照后镜像远端 body
```

### 衰减必须紧跟 Apply，不能放在触发之后

**若 `Decay` 排在 `DetectNewContacts` 之后**，本帧刚写入的 `KnockbackSpeed = 12` 会立刻被衰减成 9，下一帧 `Apply` 用的是 9 —— **12 从未参与任何一帧积分**，决策四的 `12 × dt × (1 + 0.75 + …)` 验算不成立，实际总位移少一档。

把 `Decay` 紧跟 `Apply`（都在 `physics.Step` 之前）即可，**不需要区分「本帧新写入」与「已有状态」**：`Decay` 在触发之前就执行完了，新写入的值天然不会被本帧衰减。

于是时序是：

| 帧 | Apply 用的速度 | 帧末 remaining |
|---|---|---|
| N（碰撞发生） | — （触发在帧末） | 8 |
| N+1 | **12**（完整） | 7 |
| N+2 | 9 | 6 |
| … | … | … |
| N+8 | 12 × 0.75⁷ | 0 |

共 8 帧积分，与 `KnockbackFrames = 8` 对齐，等比级数和 3.75 成立。

**用例必须逐帧断言速度、`remaining` 与位移增量**，只断言「最终归零」会放过整条时序错位。

四个 System 全是纯静态、零引擎依赖，放 `GameShared` 两端共用。**这是 Dash 状态机两端一致的结构性保证** —— 各写一份必然漂移。

## 实现步骤

### Step 1：体力接入属性系统

按决策二的清单改 10 处。`MaxStamina` 默认 100、初值满。

**这一步单独跑一次全量用例。** 它会改动两个哈希基线，也会波及既有的属性与 Buff 用例。先确认「只有基线变了、其他行为不变」，再往下做 —— 否则后面出问题时分不清是哪一步引入的。

若属性或 Buff 用例失败，**是某处映射漏了，改代码不改用例**。

### Step 2：Dash 状态机

新增 `DashPhase` / `DashTuning` / `DashSystem`。默认值：`DashFrames = 6`、`RecoverFrames = 20`、`DashSpeed = 18`、`DashStaminaCost = 30`。

`PlayerState` 与 `PlayerStateSnapshot` 加 Dash 三字段 + `StaminaRegenCounterFrames`，进哈希与 `ArePlayerSnapshotsEquivalent`。

`DashSystem.ResolveVelocity` 是 Dash 与移动的唯一交汇点：`Dashing` 相位返回锁定方向 × `DashSpeed`，否则返回输入方向 × `MoveSpeed`。**不要在 `MoveSystem` 里加 Dash 分支** —— `MoveSystem.Apply` 当前只是转发给 `SetBodyMovementInput`，把 Dash 塞进去会让「移动」和「Dash」的职责混在一起。

### Step 3：输入通路加 `DashRequest`

proto 加字段 → 跑 `GameServer/Tools/ProtocolExportTool/Run.bat` → 客户端采样 Shift → `Tick` / `SaveInputHistory` / `_onSendInput` / `BufferedInput` / `PendingInput` / `SubmitInput` 两个重载。

### Dash 按键必须走既有 `InputBuffer`，不能在 `Tick` 里直接读

`BattleClientController` 已有 `InputBuffer<BufferedInputKind, int>`（`:39`），技能就是这么接的：`Update` 里 `Input.GetKeyDown` → `_inputBuffer.Record(BufferedInputKind.Skill, skillId, SkillInputBufferFrames=2)`（`:215-221`），`Tick` 里 `_inputBuffer.TryConsume`（`:254`）。

Dash 照抄这条通路，**不要另起一套**：

- `BufferedInputKind` 加 `Dash`（该枚举是私有的，`:92`）
- `Update` 里 `GetKeyDown(LeftShift) || GetKeyDown(RightShift)` → `Record`，缓冲帧数沿用 2
- `Tick` 里 `TryConsume`，**每次逻辑 Tick 最多消费一次**

两个具体 bug 是这条通路专门防的：

| 错误做法 | 症状 |
|---|---|
| `Tick` 里直接 `GetKeyDown` | 渲染帧率高于 30Hz 时，按下发生在两次逻辑 Tick 之间就**丢按键** |
| 用 `GetKey`（长按） | Recover 结束瞬间**自动连发 Dash**，体力被一路抽干 |

**自动化输入也要走同一语义**：`_automationInputSource` 已有 `TryGetSkillRequest`（`:248`），需加 `TryGetDashRequest`。注意既有结构里 `TryConsume` 在 `else` 分支 —— **自动化输入存在时不消费键盘缓冲**，Dash 照抄这个结构，否则两个来源会互相干扰。

服务端两处接线点（**行号会漂，按方法名定位**）：`BattleComponent.SubmitInput` 转发处追加参数、`BattleComponent.Tick` 驱动处。

### Step 4：击退

新增 `KnockbackTuning`（含决策四的构造期断言）与 `KnockbackSystem`。`PlayerState` 与快照加 `KnockbackVelX/Y` + `KnockbackRemainingFrames`，进哈希、diff、proto。

服务端加「上一帧接触对集合」与新接触触发。冲量方向取两球中心连线；**完全重合时（`distanceSquared <= OccupancyEpsilon`）必须与占位分离用同一套退化规则** —— 两处不一致会产生极难复现的偏差。

**但 `DetermineSeparationNormal` 目前是 `FrameSyncPhysicsWorld` 的 `private static` 方法，`BattleLogic` 调不到。** 只写「沿用它」的话，实现者唯一的出路是复制一份 —— 正好违反这条约束本身。

**所以本步含一项结构改造**：把法线解析抽成 `GameShared/FrameSync/Battle/SeparationNormalResolver.cs` 的纯静态函数，`ResolvePlayerOccupancy` 与击退触发共同调用。

抽取时注意它现在的第二级退化依赖 `requestedVelocities`（相对速度方向）—— 击退触发发生在 `physics.Step` 之后，此时 `_pendingLinearVelocities` 与 `_pendingImpulses` 已被清空，**拿不到 `requestedVelocities`**。故纯函数签名要允许「无速度信息」的调用方式，此时直接落到第三级退化（bodyId 排序）。**两个调用方在同一输入下必须得到同一结果，这是抽取的全部意义**，用例专守。

### Step 5：远端 body 镜像（决策五）

在 `ApplyAuthoritativeSnapshot` 里，`_remotePlayers.ApplyAuthoritative(...)` 之后、对每个远端玩家 `SetBodyTransform(resetVelocity: true)`。

**唯一的顺序陷阱**：`RestoreSelfOnly` 内部会 `ClearBodies()` 再只重建自己。所以镜像写入**必须在 `RestoreSelfOnly` 之后**。写在前面会被整块清掉，决策五完全失效 —— 而且症状与「没实现决策五」一模一样，排查时容易误判成方案无效。

buffer 移除条目处同步 `RemoveBody`。

### Step 6：表现层

Dash 期间拖影/变色；体力条；击退时 ghost 与实体分离最明显（S11 主素材）；HUD 加 Dash 相位、体力、击退剩余帧。

复用 S1 的 ghost 与既有 HUD 结构，**不做布局打磨**（S10/S11）。

### Step 7：mismatch 日志分字段

`CheckConsistency` 的 MISMATCH 日志已含位置、属性、Buff。追加体力、Dash 相位、击退，并在**首个不一致字段**处打对应标签。

判定顺序：**先 gameplay 语义（体力 → Dash 相位 → 击退），后位置与属性。** 位置失配往往是前三者的**后果**，先报位置会指错方向。

### Step 8：无头用例

**注册两处**（`AllCaseNames` 数组 + `RunCase` 的 switch 表达式），**只追加不改既有**。

Dash 与体力（8 条）：

| 用例 | 断言 |
|---|---|
| `dash-consumes-stamina-and-enters-recover` | 起手扣体力、相位转 `Dashing`、到期转 `Recovering` |
| `dash-blocked-during-recover` | Recover 期间起手请求被忽略，体力不扣 |
| `dash-blocked-when-stamina-insufficient` | 体力不足时不起手、不扣体力、相位不变 |
| `dash-direction-latched-at-start` | 起手后改输入方向，位移仍按起手方向 |
| `dash-zero-input-uses-default-direction` | 零输入起手取常量 `(1,0)`，不拒绝 |
| `dash-phase-advance-order-is-decrement-then-start` | 同一帧 Recover 到期 + 新请求：Recover 完整生效，不被吞 |
| **`dash-request-survives-replay`** | **守决策七**。含 Dash 的帧回滚重放后结果与首次预测逐位相同 |
| **`stamina-regen-counter-restores-on-rollback`** | **守决策二**。回滚后 `StaminaRegenCounterFrames` 恢复到权威值，体力不漂移 |

Dash 输入通路（2 条，守 Step 3）：

| 用例 | 断言 |
|---|---|
| `dash-input-buffered-across-render-frames` | 渲染帧按下、当帧无逻辑 Tick 时，**下一个逻辑 Tick 仍能消费到** |
| `dash-hold-does-not-autofire` | 持续按住 Shift 跨越整个 Dash + Recover 周期，**只起手一次** |

击退（7 条）：

| 用例 | 断言 |
|---|---|
| `knockback-triggers-only-on-new-contact` | 接触持续 5 帧只触发一次；分离后再接触再触发一次 |
| **`knockback-decay-timeline-is-frame-exact`** | **守决策三时序**。逐帧断言速度、`remaining`、位移增量：帧 N+1 速度为**完整** `KnockbackSpeed`、N+2 为 ×0.75、…、N+8 后归零，共 **8 帧**积分。**只断言「最终归零」会放过整条时序错位** |
| `knockback-total-displacement-matches-series` | 总位移与等比级数和（3.75 项）一致，即决策四验算成立 |
| `separation-normal-resolver-agrees-across-callers` | **守 Step 4 抽取**。同一输入下占位分离与击退触发得到同一法线；完全重合时都落到 bodyId 排序退化 |
| **`knockback-state-replays-without-new-mismatch`** | **守决策三核心**。应用含击退状态的权威快照后，重放不产生新 mismatch |
| `knockback-worst-case-deviation-under-smoothing-threshold` | 决策四验算：最坏偏差 < `MaxSmoothingDistance` |
| `client-prediction-never-self-triggers-knockback` | 客户端预测路径不含触发逻辑，孤立跑不产生击退 |

远端 body 镜像（4 条）：

| 用例 | 断言 |
|---|---|
| `remote-body-blocks-self-movement` | 自己撞不进远端玩家占位；远端不在 `_worldState`、`PlayerCount == 1` |
| **`remote-body-not-displaced-by-occupancy`** | **守决策五的 kinematic 建模**。自己静止、两者重叠时连跑多帧，**镜像位置逐位不变**（全部穿透量由自己承担）。**不加 `IsKinematicObstacle` 时这条必挂** |
| **`remote-body-mirror-survives-restore-self-only`** | **守 Step 5 顺序陷阱**。`RestoreSelfOnly` 后镜像仍在 |
| `remote-body-removed-with-buffer-entry` | 玩家消失后 body 一并移除，不留隐形墙 |

字段同步（5 条）：

| 用例 | 断言 |
|---|---|
| `dash-state-proto-roundtrip` | Dash 三字段往返位精确 |
| `knockback-state-proto-roundtrip` | 击退三字段往返位精确 |
| **`numeric-stamina-base-proto-roundtrip`** | **守决策二第二节**。`Numeric.BaseAttributes` 的两个体力 base 往返位精确；**base 不同步时 `Recalculate` 会把体力清零**，这条专门抓它 |
| `stamina-recovers-after-packet-loss` | 守 B1 周期全量对体力生效 |
| `stamina-mismatch-triggers-rollback` | 守决策二 `AreAttributesEqual` |

合计 **26 条**，总数 **73 → 99**（基数须在 S7 落地后重数）。

### Step 9：report 与场景

`BattleAutomationClientSnapshot` 加 `dashCount`、`staminaAtEnd`、`knockbackTriggerCount`、`contactMismatchFrames`。新增场景 `two-client-knockback`。

**场景名登记三处**（`TestRunner.Execute` 的 switch、`NormalizeScenario`、`TestScenario` 常量表）—— 只登记一处会让 `--scenario=all` 假 PASS，既有验收指南已列为 FAIL 判据。

**必须有一条断言是「击退确实发生了」**（`knockbackTriggerCount > 0`）。否则碰撞判定失效时，所有判据都会以「一切正常」的姿态通过 —— 同 S4 Step 7、S7 Step 7 同一类陷阱。

## 验证方式

- **无头**：`--mode=test --scenario=all` 全通过，退出码 `0`
- **基线稳定性**：两个哈希基线变化**一次**后，连续两跑逐位一致（决策六）
- **确定性扫描**：`--mode=validate --duration-seconds 30 --update-hz 60`，Issues 零
- **弱网 Dash**：`two-client-weaknet-delay` 下 Dash 起手不等服务端确认，体力与相位最终收敛
- **弱网击退**：`two-client-knockback` 固定场景（下方定量判据）
- **接触期对比**：`contactMismatchFrames` 的 before/after 数字

### 定量判据必须绑定固定场景

「mismatch 与碰撞次数同量级」这种说法**不可执行** —— 没有网络参数、接触持续时间与上界公式，验收 agent 无法判定。改为在**固定场景**下给死数字：

场景 `two-client-knockback` 固定参数：**注入双向各 100ms 延迟、零丢包、固定种子、两球对撞 3 次、每次接触持续约 4 帧**。

| 判据 | 阈值 |
|---|---|
| 每次服务端击退触发引起的 `StateMismatch` | **≤ 1** |
| 全程 `StateMismatch` 总数 | **≤ 3**（= 碰撞次数） |
| `contactMismatchFrames`（接触期 `PositionMismatch`，决策五残差） | **≤ 6**（3 次接触 × 2 帧余量） |
| 同场景相对决策五实施前的 `contactMismatchFrames` | **下降 ≥ 60%** |

**`StateMismatch` 与 `PositionMismatch` 必须分开统计** —— 前者是击退状态到达引起的（决策三，应恰好每次碰撞一次），后者是镜像位置滞后的残差（决策五，有界但不为零）。混在一个计数里则两者互相掩盖，任何一个退化都看不出来。

阈值按上述固定参数标定。**换网络参数必须重新标定，不要拿别的场景的数字判 FAIL。**
- **HUD / 录屏**：Dash 拖影、体力条、击退时 ghost 与实体分离可见

## 完成标准

1. **Dash / 体力 / Recover 全程客户端预测**，弱网下不等服务端确认；三者及 `StaminaRegenCounterFrames` 全部进快照、哈希、diff、回滚重播。
2. **Dash 方向在起手帧锁定**，零输入起手取常量方向（决策一）。
3. **体力走独立 `AttributeKind.Stamina`**，`AreAttributesEqual` 与 `StateHasher` 两处都已加（决策二）。
4. **体力恢复是整数节律**，计数器进快照；`stamina-regen-counter-restores-on-rollback` 通过。
5. **击退裁决权在服务端**，客户端预测路径不自触发（`client-prediction-never-self-triggers-knockback` 通过）。
6. **击退状态随快照下发并被客户端重放** —— `knockback-state-replays-without-new-mismatch` 通过；固定场景 `two-client-knockback` 下**每次触发引起的 `StateMismatch` ≤ 1、总数 ≤ 3**（决策三）。**这条是本阶段演示价值的核心判据。**
7. **触发按新接触判定**，接触持续期不重复触发（`knockback-triggers-only-on-new-contact` 通过）。
8. **击退衰减时序逐帧正确** —— 首帧用完整 `KnockbackSpeed`、共 8 帧积分、总位移与级数和一致（`knockback-decay-timeline-is-frame-exact` 通过）。**`Decay` 紧跟 `Apply`，不在触发之后。**
9. **击退参数与平滑阈值的联立断言在构造期生效**，越界抛异常（决策四）。**调参后必须重跑。**
10. **法线解析已抽成两端共用的纯函数**，占位分离与击退触发同输入同结果（`separation-normal-resolver-agrees-across-callers` 通过）。
11. **客户端把别人镜像成只读碰撞 body**，不进 `_worldState`、不参与回滚；`RestoreSelfOnly` 后镜像仍在（决策五）。
12. **镜像 body 已显式建模为 kinematic**，自己静止且重叠时镜像位置逐位不变（`remote-body-not-displaced-by-occupancy` 通过）。**仅靠 `resetVelocity` 不够。**
13. **接触期残差有界** —— `contactMismatchFrames ≤ 6` 且相对实施前下降 ≥ 60%；**`StateMismatch` 与 `PositionMismatch` 分开统计**。
14. **Dash 按键走既有 `InputBuffer`** —— 渲染帧按下下一逻辑帧仍可消费、长按不自动连发（两条用例通过）；自动化输入走同一单帧请求语义。
15. **`Numeric.BaseAttributes` 的两个体力 base 已协议化** —— `numeric-stamina-base-proto-roundtrip` 通过。**漏了则每次应用权威快照都把体力清零。**
16. **体力是两个独立 `AttributeKind`**（`Stamina` / `MaxStamina`），`GetBaseValue` / `SetBaseValue` / `Recalculate` 三处都已加分支。**「Buff 提升体力上限」实际可用。**
17. **mismatch 日志按类型区分** `PositionMismatch` / `StaminaMismatch` / `StateMismatch`，判定顺序先 gameplay 语义后位置。
18. **`DashRequest` 进 `_inputHistory` 且重放复现**（`dash-request-survives-replay` 通过）。
19. 四个 System 与 `SeparationNormalResolver` 零 `UnityEngine` / `Fantasy` 引用，两端共用同一份实现。
20. **两个哈希基线已按新字段更新且连续两跑稳定**；**三份既有验收指南的「基线必须不变」已同步改注**（决策六）。
21. **`knockbackTriggerCount > 0` 已进判据**，防碰撞判定失效时假通过。
22. **S6 决策六勘误已回写** —— 说明「玩家间无碰撞」不成立、客户端改为只读镜像、S6 核心原则未被削弱。
23. 能说清**为什么击退触发不预测**（对手位置在本地是过去时，预测它期望值为负）、**为什么演化照常预测**（否则每击退帧一次回滚），以及两者不矛盾。

## 风险与对策

| 风险 | 对策 |
|---|---|
| **沿用 S6「玩家间无碰撞」旧结论** | 会整个漏掉决策五，击退演示里两球接触期持续 mismatch，且该 mismatch 会掩盖击退本身的信号。`ResolvePlayerOccupancy` 从 v0.4a 就在（Context 第一条） |
| **`DashRequest` 漏存 `_inputHistory`** | 重放时 Dash 消失，**每次回滚新增一次 mismatch，看起来像随机失配或网络问题**。`dash-request-survives-replay` 专守（决策七） |
| **体力恢复用小数累加器** | 累加器自身是逻辑状态，漏进快照则回滚后体力漂移，表现为偶发体力 mismatch。用整数节律 + 计数器进快照（决策二） |
| **漏 `AreAttributesEqual` 的体力比较** | 体力失配不触发回滚，永久错位而系统自认一致。`stamina-mismatch-triggers-rollback` 专守 |
| **漏 `StateHasher` 的新字段** | S3 哈希对账放过该字段的所有错误。两处（玩家段 + `Numeric.BaseAttributes` 段）都要加 |
| **击退用 `ApplyBodyImpulse` 承载持续状态** | 它当帧清空且不进快照，回滚后凭空消失。持续状态放 `PlayerState` + 快照，每帧重新喂（Context 第二条） |
| **击退客户端完全不感知** | 8 帧击退 = 8 次回滚，画面连续抖动，S6 收益一起毁掉。触发不预测、演化照常预测（决策三） |
| **触发按「正在接触」判定** | 贴身期间每帧重新灌满击退速度，**击退永不衰减、两球持续弹开**。维护上一帧接触对集合（决策三末节） |
| **重合时法线退化规则另写一套** | 与占位分离不一致会产生极难复现的偏差。**但 `DetermineSeparationNormal` 是 `private static`，不抽出来就只能复制** —— Step 4 含抽成 `SeparationNormalResolver` 的结构改造，`separation-normal-resolver-agrees-across-callers` 专守 |
| **`Decay` 排在触发之后** | 本帧刚写入的 `KnockbackSpeed` 立刻被衰减，**首帧速度从未参与积分**，决策四验算失效、总位移少一档。`Decay` 紧跟 `Apply`；`knockback-decay-timeline-is-frame-exact` 逐帧断言 |
| **击退用例只断言「最终归零」** | 放过整条时序错位（首帧被衰减、积分帧数少一帧）。必须逐帧断言速度、`remaining`、位移增量 |
| **体力只加一个 `AttributeKind`** | `MaxStamina` 无 kind 则无法独立重算、无法被修饰器命中，**「Buff 提升体力上限」直接落空**。必须两个 kind（决策二） |
| **漏 `NumericSnapshot` 的两个体力 base** | `RestoreRuntimeState` 里紧接着调 `Recalculate`，base 为 0 则**当场把刚同步来的体力算成 0**。表现为「体力永远是 0 且回滚不停」。`numeric-stamina-base-proto-roundtrip` 专守（决策二第二节） |
| **`SetBaseValue` 既有分支漏传新参数** | 该属性被设值时把体力清零。**不要给 `PlayerAttributeSnapshot` 构造函数加默认值** —— 让漏改处编译失败 |
| **以为 `resetVelocity: true` 就等于只读** | 它只清速度。双方都无朝向意图时占位分离**各分一半穿透量**，镜像 body 在重放中持续漂移。必须加 `IsKinematicObstacle`（决策五）；`remote-body-not-displaced-by-occupancy` 专守 |
| **Dash 在 `Tick` 里直接 `GetKeyDown`** | 渲染帧率高于 30Hz 时按下发生在两次逻辑 Tick 之间就**丢按键**。走既有 `InputBuffer`（Step 3） |
| **Dash 用 `GetKey` 长按** | Recover 结束瞬间**自动连发**，体力被一路抽干。用 `GetKeyDown` + `Record` / `TryConsume` |
| **「mismatch 与碰撞次数同量级」当判据** | 无网络参数与上界公式，验收不可执行。绑定固定场景给死阈值，且 **`StateMismatch` 与 `PositionMismatch` 分开统计**（验证方式一节） |
| **把零输入常量方向说成抗丢包措施** | `DashRequest` 与方向同消息、同到达同丢失，且 `ConsumedInput` 不含 `DashRequest`（服务端不会复用出 Dash）。它是**确定性**要求（零向量归一化未定义），不是抗丢包（决策一） |
| **镜像 body 写在 `RestoreSelfOnly` 之前** | 被内部 `ClearBodies()` 清掉，决策五完全失效，**症状与「没实现」一模一样**，容易误判成方案无效。`remote-body-mirror-survives-restore-self-only` 专守（Step 5） |
| **远端 body 不随玩家移除** | 隐形墙残留，重开局后撞空气。buffer 移除处同步 `RemoveBody` |
| **把镜像 body 说成「预测别人」** | 它不推进、不插值、不回滚，就是障碍物。口径错了会与 S6 的叙事冲突（决策五） |
| **按「哈希基线必须不变」判 FAIL** | 本阶段是唯一要求基线变化的阶段。**三份既有指南都要改注，尤其 S3 那份是明文强判据**（决策六） |
| **基线只跑一次就更新** | 把偶发结果固化成基准。连续两跑确认后再更新（决策六） |
| **Dash 期间跟随输入转向** | 服务端复用旧输入时方向与客户端不同，**每次上行丢包一次方向级 mismatch**。起手锁定（决策一） |
| **零输入起手判为拒绝** | 「客户端有输入、服务端复用到零」时产生分歧。定成常量 `(1,0)`（决策一） |
| **状态机先起手后递减** | Dash 少一帧；且同帧 Recover 到期 + 新起手会让 Recover 形同不存在。递减在前（决策一） |
| **Dash 挂 SkillGraph** | 其执行状态无快照/恢复，回滚走整体 `Clear()`，违反 Timeline 技术约束 3。走一等字段（决策一） |
| **击退走 Buff 系统** | 把时序绑到 Buff 命令队列顺序，且连续衰减量用 Buff 模型表达要绕一层，收益为零（决策三） |
| **击退参数调猛后走硬跳** | 画面瞬移但**无头用例照样全绿**（哈希只管一致不管好看）。构造期断言 + 调参后重跑（决策四） |
| **新场景名只登记一处** | `--scenario=all` 假 PASS。三处都要登记（Step 9） |
| **无「击退确实发生了」的断言** | 碰撞判定失效时全部判据假通过（Step 9） |
| **proto 新字段插在中间** | opcode / 字段号按声明顺序，后续全部改号。**末尾追加**：体力 25/26、Dash 与击退 27 起 |
| **`SubmitInput` 只改一个重载** | 有 test-only 的 `float` 重载，只改一个会让测试与生产走不同参数语义，**这类错误正好被用例本身放过**（决策七） |
| **服务端运行中报 `MSB3027`** | 先停服务端，或只 build `Entity.csproj` |

## 相关实现文件

| 文件 | 改动 |
|---|---|
| `GameShared/FrameSync/Battle/DashPhase.cs` | 新建，枚举 |
| `GameShared/FrameSync/Battle/DashTuning.cs` | 新建，Dash 与体力常量 |
| `GameShared/FrameSync/Battle/KnockbackTuning.cs` | 新建，含与 `MaxSmoothingDistance` 的联立断言（决策四） |
| `GameShared/FrameSync/Battle/DashSystem.cs` | 新建，纯静态：推进 / 起手 / 解析移动速度 |
| `GameShared/FrameSync/Battle/StaminaSystem.cs` | 新建，纯静态：整数节律恢复 |
| `GameShared/FrameSync/Battle/KnockbackSystem.cs` | 新建，纯静态：逐帧应用与衰减，两端共用 |
| `GameShared/FrameSync/Battle/SeparationNormalResolver.cs` | **新建**，法线解析纯函数，占位分离与击退触发共用（Step 4 结构改造） |
| `GameShared/FrameSync/Battle/NumericModifier.cs` | `AttributeKind` 加 `Stamina = 6`、`MaxStamina = 7` |
| `GameShared/FrameSync/Battle/PlayerAttributeDirtyFlags.cs` | 加两位，**`All` 一并更新** |
| `GameShared/FrameSync/Battle/PlayerAttributeSnapshot.cs` | 加两字段 + 构造参数 + `Default`。**不加默认值参数** |
| `GameShared/FrameSync/Battle/PlayerAttributeSync.cs` | `ComputeDirtyMask` / `Merge` 各加两项 |
| `GameShared/FrameSync/Battle/PlayerState.cs` | Dash 三字段、击退三字段、恢复计数器、两个体力属性 + switch 分支 |
| `GameShared/FrameSync/Battle/PlayerStateSnapshot.cs` | 同上全部字段 |
| `GameShared/FrameSync/Battle/NumericState.cs` | **`GetBaseValue` / `SetBaseValue` / `Recalculate` 三处**各加两条分支（决策二） |
| `GameShared/FrameSync/Snapshot/StateHasher.cs` | **玩家段 + `Numeric.BaseAttributes` 段各加**（最易漏） |
| `GameShared/FrameSync/Battle/FixedPhysicsBody.cs` | 加 `IsKinematicObstacle`（**不进快照**，纯客户端本地概念，决策五） |
| `GameShared/FrameSync/Battle/FrameSyncPhysicsWorld.cs` | 加 `SetBodyKinematicObstacle` 入口、`CalculateSeparationShares` 尊重该标志、`DetermineSeparationNormal` 改调 `SeparationNormalResolver` |
| `GameShared/FrameSync/Battle/RemotePlayerBuffer.cs` | 移除条目处同步 `RemoveBody`（决策五） |
| `GameServer/Tools/NetworkProtocol/Outer/OuterMessage.proto` | `PlayerSnapshot`、`C2B_PlayerInput`、**`NumericSnapshot`** 三处**末尾追加**，跑 `Run.bat` |
| `GameServer/Server/Entity/Battle/BattleLogic.cs` | Tick 内驱动四 System、新接触触发、`SubmitInput` **两个重载**加 `DashRequest`。**本阶段刻意改内核**（决策六） |
| `GameServer/Server/Entity/Battle/BattleComponent.cs` | `SubmitInput` 转发处、`Tick` 驱动处。**按方法名定位，行号会漂** |
| `GameLogic/Battle/BattleSimulation.cs` | `ApplyLocalPrediction` 驱动四 System（**不含触发**）、`ApplyAuthoritativeSnapshot` 镜像远端 body、`BufferedInput` 加字段、`CheckConsistency` 日志分字段 |
| `GameLogic/Battle/BattleClientController.cs` | `BufferedInputKind` 加 `Dash`、`Update` 里 `Record`、`Tick` 里 `TryConsume`、Dash 拖影、体力条、HUD |
| `GameLogic/Battle/BattleSnapshotProtocolMapper.cs` | `PlayerSnapshot` 与 **`NumericSnapshot`** 的 To / From 双向补全（**S5 通路，漏则该字段静默丢失**） |
| 自动化输入源接口 | 加 `TryGetDashRequest`，与 `TryGetSkillRequest` 同构（Step 3） |
| `GameLogic/Battle/BattlePredictionSelfTestSuite.cs` | **26 条**，注册两处 |
| `GameLogic/Battle/Automation/BattleAutomationRuntime.cs` | 四个 report 字段（`StateMismatch` 与 `PositionMismatch` **分开计**）+ 新场景判据 |
| `GameServer/Server/Entity/TestHarness/TestRunner.cs` | `two-client-knockback` 登记三处 |
| `Tools/AutomationAcceptance/` 三份既有指南 | **基线判据改注**（决策六），S3 那份优先 |
| `Plan/…/S6-回滚粒度改造-…-实现计划.md` | 决策六勘误（决策五末节） |

**不需要改**：

- `BuffSystem.cs` / `RuntimeSkillGraph.cs` —— Dash 与击退都不走这两条路（决策一、三）
- `AttributeBroadcastBaseline.cs` —— 存的是整个 `PlayerAttributeSnapshot` 结构体，不逐字段拆解，两个新字段自动跟随
- ~~`FrameSyncPhysicsWorld.cs`~~ —— **击退**不需要新物理 API（`ApplyBodyImpulse` 已够用，Context 第二条），但**决策五需要** `SetBodyKinematicObstacle` 与 `SeparationNormalResolver` 的抽取，见上表
- `NetworkConditionSimulator.cs` —— S4 的注入能力已够
- S7 的 RTT 通路 —— 无交互

## 估时

**4~4.5 天**（初版估 3-4 天，复审后因两个 kind、`NumericSnapshot` 协议化、kinematic 建模、法线抽取、输入缓冲五处上修）。是 S1~S7 里最大的一个：协议 + 内核 + 预测层 + 表现层四处同步改，且新增两个状态机。

| 部分 | 估时 |
|---|---|
| Step 1 体力接入属性系统（两个 kind + `NumericSnapshot` 协议化 + 基线确认） | 6 小时 |
| Step 2 Dash 状态机 | 4 小时 |
| Step 3 输入通路 `DashRequest` + `InputBuffer` 接入 + 自动化源 | 3.5 小时 |
| Step 4 击退 + 服务端触发 + `SeparationNormalResolver` 抽取 | 5 小时 |
| Step 5 远端 body 镜像 + kinematic 建模 | 3 小时 |
| Step 6 表现层 + HUD | 3 小时 |
| Step 7 mismatch 日志分字段 | 1 小时 |
| Step 8 二十六条用例 | 6.5 小时 |
| Step 9 report + 场景 + 基线更新 + 三份指南改注 | 2.5 小时 |

**建议先写用例再改实现的五条**（风险集中处，症状都具误导性）：

1. `numeric-stamina-base-proto-roundtrip` —— 漏了则体力恒为 0，症状像「体力功能没生效」
2. `dash-request-survives-replay` —— 漏了则症状像「网络不稳」
3. `knockback-decay-timeline-is-frame-exact` —— 漏了则症状是「击退比预期弱一点」，几乎不会被注意
4. `knockback-state-replays-without-new-mismatch` —— 本阶段演示价值的核心
5. `remote-body-not-displaced-by-occupancy` —— 漏了则症状像「决策五方案无效」

## 验收指南

`Tools/AutomationAcceptance/Dash与击退-验收测试指南-<日期>.md`（随实现一并产出）。

必须包含：

1. **哈希基线变化是预期行为** —— 这是与 S3~S7 相反的一条，**放在最前面**。判据是「变一次后连续两跑一致」，不是「不变」
2. **三份既有指南的改注位置** —— 验收 agent 可能同时手持旧指南
3. **弱网 Dash 判据** —— 起手不等确认、体力与相位最终收敛
4. **击退 mismatch 的定量阈值** —— 绑定固定场景（双向 100ms、零丢包、固定种子、3 次对撞）：每次触发 `StateMismatch ≤ 1`、总数 `≤ 3`。**必须写明「换网络参数需重新标定」**，否则验收 agent 会拿别的场景的数字判 FAIL
5. **`StateMismatch` 与 `PositionMismatch` 分开统计** —— 前者是击退状态到达（决策三），后者是镜像滞后残差（决策五）。混计则两者互相掩盖
6. **接触期 before/after 对比** —— `contactMismatchFrames ≤ 6` 且下降 ≥ 60%（决策五）
7. **「击退确实发生了」的断言清单** —— 碰撞判定失效时所有判据会假通过
8. **残差声明** —— 镜像 body 用的是延迟 RTT/2 的对手位置，分离残差**有界但不为零**，**不判 FAIL**（决策五）
9. **参数断言** —— 越界即抛异常；调参后必须重跑（决策四）
10. **口径声明** —— 「触发不预测、演化照常预测」。**验收 agent 不应按「客户端完全不知道击退」判 FAIL**
11. **真机观测项** —— Dash 拖影、体力条、击退 ghost 分离；**长按 Shift 不自动连发**。无头测不出「看起来如何」

## 后续衔接

- **S9（断线重连）** —— 重连后须恢复 Dash 相位、体力、击退剩余帧。**`StaminaRegenCounterFrames` 最易漏**，它不在属性脏同步里
- **S10（远端插值）** —— 决策五的镜像 body 位置届时可改用插值值，进一步缩小残差。调试面板加 Dash / 体力 / 击退三项
- **S11（求职展示）** —— 击退 ghost 分离是主素材。**定稿后回头复核 `MaxSmoothingDistance = 6.0`**（决策四）
- **玩家间碰撞的客户端预测** —— 决策五只做只读镜像。真要预测别人需另开号，且与 S6 方向相反，需重新论证
- **服务端脏字段过滤** —— S6 Context 末节列为「另开号」。本阶段新增 7 个快照字段后带宽收益更明显，值得重估
- **B 编号待查**：`AttributeBroadcastBaseline.Remove` 两个字典删除不一致（帧号字典真删、快照字典置零留空条目）。**与本阶段无关**，但 S9 的同玩家重新加入路径可能踩到，建议 S9 前单独核实
