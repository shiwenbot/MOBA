# S5：无头测试改走真实 proto 序列化通路

> **状态**：**已完成**（2026-08-06）。执行编号 **S5**，见 [00-总索引.md](00-总索引.md)。它不是新功能，是补一条既有测试通路的盲区。
> **基线**：`a2b8006a`，行号按此核对。
> **落地记录**（2026-08-06）：抽出 `BattleSnapshotProtocolMapper`（服务端 + 客户端双份共用），`OnBroadcast → ApplySnapshot` 默认过 proto 序列化往返；既有 37 条用例现全部验证 proto 往返，新增 `proto-roundtrip-preserves-fixed64-extremes` 极值用例。`fixed-physics-bit-exact` 等既有基线未变（Step 4 核心断言成立）。

## 问题：37 条用例全绿，但 proto 往返路径从未被执行

S3 修掉了「快照用 float 传 `Fixed64` 丢约 13 位精度」的地基问题——proto 的 `PlayerSnapshot` 现在全传 `m_rawValue`（`XRaw` / `YRaw` / `LinearVelocityXRaw` / `LinearVelocityYRaw`，`BattleComponent.cs:210-214`）。**bug 本身已修，这点已核实。**

但**这个修复没有被任何测试覆盖**。无头测试完全绕过序列化：

```
生产路径：
  BattleLogic.Tick → BuildTestSnapshot → OnBroadcast
    → BattleComponent.BroadcastSnapshot（:153）
       → 逐字段填 S2C_FrameSnapshot（:207-227，含 m_rawValue 转换）
       → session.Send（:259）→ KCP 序列化
    → 客户端 ConvertSnapshot → FromRaw → RestoreSnapshot

无头路径（TestRunner.cs:1057-1063）：
  BattleLogic.Tick → BuildTestSnapshot → OnBroadcast
    → clients[i].ApplySnapshot(snapshot)
       → SimulatedClient.ApplySnapshot（SimulatedClient.cs:27-30）
          → WorldState.RestoreSnapshot(snapshot.ToBattleWorldSnapshot())
```

无头路径推的是**内存里的 `TestSnapshot` 结构体**，`ToBattleWorldSnapshot()` 是纯内存转换。**全程零序列化、零 proto、零 `m_rawValue` 转换。**

### 含义

「37 条用例全绿」证明的是**进程内确定性自洽**，不能证明**proto 往返位精确**。

`BattleComponent` 里那段 `m_rawValue` 填充与客户端 `ConvertSnapshot` / `FromRaw` 的还原，正确性目前**只靠人眼读代码**，没有任何机械断言。

而 demo 的核心叙事是「确定性 + 位精确和解」。面试时被问「端到端是位精确的吗、你怎么验证的」，当前的诚实回答是「测试覆盖不到那一段」。

## 为什么优先级高于 S6 / S7 / S8

1. **它守的是叙事根基。** S6/S7/S8 都是在「位精确」这个前提上加东西，前提本身没有验证手段是倒置的。
2. **成本极低。** 改动集中在一处（见下）。
3. **S6 会立刻用到。** S6 要拆 `BattleWorldState` 并改 `ReconcileAuthoritativeSnapshot` 的回滚路径。它的验收标准第 3 条是「哈希一致率不变」，而当前无头用例根本不走 proto —— **不先改通路，S6 的「一致率不变」证明不了什么。**
4. **S8 也会用到。** 击退大概率要给 `PlayerSnapshot` 加字段（加速度 / 状态位），新字段的精度问题在当前测试结构下**依然静默**。先改通路，S8 的新字段自动被覆盖。

## 决策：改测试通路，不是加一条用例

**加一条「走真 proto 的用例」只守住今天这几个字段。** 下次加字段盲区立刻回来，而且照样全绿——这正是本文要消除的失效模式，不该用同一个模式去修它。完整推导见 [设计教训-两类静默失效.md](设计教训-两类静默失效.md) 第二节。

**正确做法：让 `OnBroadcast` → `ApplySnapshot` 这条路默认过序列化。**

```
BattleLogic.Tick → BuildTestSnapshot → OnBroadcast
  → [新] 序列化为 S2C_FrameSnapshot → ToByteArray
  → [新] Parse 回 S2C_FrameSnapshot
  → [新] 走与客户端同一份 Convert / FromRaw 逻辑
  → SimulatedClient.ApplySnapshot → RestoreSnapshot
```

改完之后 **37 条既有用例全部在验证 proto 往返**，新增字段自动被覆盖。这是把「测试有效性」做进结构里，而不是再加一条会过期的断言。

### 关键约束：必须复用生产的转换代码，不能在测试里重写一份

`BattleComponent.BroadcastSnapshot`（`:153-260`）目前把「填 proto 字段」和「per-session 发送 / 带宽统计 / 脏属性掩码」混在一个方法里，测试无法直接调。

**要把纯转换那段抽出来**（`TestSnapshot` / `BattleWorldSnapshot` → `S2C_FrameSnapshot` 的字段填充，`:207-227` 那块），让 `BattleComponent` 与测试 harness 调同一份。

**如果在 harness 里重写一份转换逻辑，这件事就白做了** —— 那验证的是 harness 自己写的那份，生产那份依然没被测到。这与 S4 决策五、S7 决策三是同一条原则：**接线层不在覆盖里，就把可判断的逻辑挪到覆盖得到的地方。**

### 脏属性掩码怎么处理

`BroadcastSnapshot` 的字段填充依赖 `_lastBroadcastAttributesByPlayerId` 算脏掩码（`:187-192`），这是 per-session 的增量状态。测试通路要么：

- **A：全量同步**（`dirtyMask = All`），跳过增量逻辑。简单，但测不到增量路径
- **B：harness 里也维护一份 baseline**，走真实增量

**建议 A**，理由：本文的目标是守**精度**，不是守增量协议。增量路径的缺陷已由 S4 单独暴露（属性脏同步无丢包恢复，见总索引），是独立议题，不要混进来。**A 方案要在代码注释里写明这个取舍**，否则将来会被误读成疏漏。

## 实现步骤

### Step 1：抽出纯转换函数

从 `BattleComponent.BroadcastSnapshot`（`:207-227`）抽出：

```
TestSnapshot / BattleWorldSnapshot + physicsByBodyId + dirtyMask
  → S2C_FrameSnapshot
```

放在能被 `Entity.csproj` 与测试同时引用的位置。**优先放 `GameShared/**`**（已被 `Entity.csproj:24` 通配覆盖），与 S4/S7 的分层一致。

`BattleComponent` 改为调用它，**行为必须逐字节不变** —— 这一步是纯重构，跑一遍现有 37 条确认基线未动，再进 Step 2。

### Step 2：反向转换也要复用客户端那份

客户端 `ConvertSnapshot` / `FromRaw` 在 `BattleClientController` / `BattleSimulation` 侧。若它已在 `GameShared` 或可被服务端引用，直接用；**若不可引用，把纯转换部分抽到 `GameShared`**，客户端改为调用。

**这一步的判断依据是「生产客户端调的是哪份代码」** —— 测试必须调同一份。动手前先确认当前 `ConvertSnapshot` 的位置与依赖，别假设。

### Step 3：测试通路接线

`TestRunner.CreateHarness`（`:1052-1078`）的 `OnBroadcast` 回调改为：

```
battleLogic.OnBroadcast = snapshot =>
{
    // 默认过真实 proto 序列化，守住位精确叙事；开关见下
    if (roundTripThroughProto)
    {
        S2C_FrameSnapshot message = SnapshotConverter.ToProto(snapshot, dirtyMask: All);
        byte[] bytes = message.ToByteArray();
        S2C_FrameSnapshot parsed = S2C_FrameSnapshot.Parser.ParseFrom(bytes);
        BattleWorldSnapshot restored = SnapshotConverter.FromProto(parsed);
        for (int i = 0; i < clients.Count; i++) { clients[i].ApplySnapshot(restored); }
        return;
    }

    for (int i = 0; i < clients.Count; i++) { clients[i].ApplySnapshot(snapshot); }
};
```

`SimulatedClient.ApplySnapshot` 需要一个接 `BattleWorldSnapshot` 的重载（当前只接 `TestSnapshot`，`SimulatedClient.cs:27`）。

**开关默认开。** 性能类用例（若有对每帧耗时敏感的）可显式关掉，**关掉的用例要在注释里写明原因**。

### Step 4：确认基线是否合法变化

**这是本阶段唯一可能翻车的地方，必须先想清楚再改。**

两个哈希基线 `fixed-physics-bit-exact` = `0xD2120B5F0F5A5FE5`、Determinism = `0xD01E17BC0F96A3EB`。

- **若 proto 往返真的位精确**（S3 修复有效），基线**必须一字不变**。这正是本文要证明的事。
- **若基线变了**，说明往返**不是**位精确的 —— 那是抓到了一个真 bug，**不要改基线去迁就**，先定位是哪个字段丢了精度。

**换句话说：这一步的「基线不变」本身就是本阶段的核心断言。** 与 S1/S2（哈希基线合法变化）相反，与 S3（基线必须不变）一致。

### Step 5：加一条显式的精度断言用例

通路改完后，37 条都在守往返，但再加一条**专门针对边界值**的：

| 用例 | 断言什么 |
|---|---|
| `proto-roundtrip-preserves-fixed64-extremes` | 构造若干极值坐标/速度（`Fixed64` 最小正值、最大值、负值、`m_rawValue` 低位全 1），走完整 proto 往返，断言 `m_rawValue` **逐位相等**。**不用容差比较** |

原因：正常 gameplay 的坐标值不会覆盖到 `Fixed64` 的低位边界，37 条用例即便走了往返也可能碰不到会暴露精度问题的位模式。

## 验证方式

- **无头**：`--mode=test --scenario=all`，38 条（37 + 1）全通过，退出码 `0`
- **哈希基线**：`0xD2120B5F0F5A5FE5` 与 `0xD01E17BC0F96A3EB` **必须不变**（见 Step 4）
- **确定性扫描**：`--mode=validate --duration-seconds 30 --update-hz 60`，Issues 零
- **Step 1 纯重构验证**：抽函数后先单独跑一遍，确认 37 条与两个基线未动，再进 Step 2

## 完成标准

1. 无头测试的快照通路**默认经过真实 proto 序列化与反序列化**。
2. 测试用的转换代码与生产用的**是同一份**，不是 harness 里重写的副本。
3. 两个哈希基线未变 —— 即证明 proto 往返位精确。
4. `proto-roundtrip-preserves-fixed64-extremes` 通过，边界位模式逐位相等。
5. 关掉往返开关的用例（若有）都在注释里写明原因。
6. 能说清：**bug 已修但测试没覆盖，全绿是假绿**；以及为什么修法是改通路而不是加用例。

## 风险与对策

| 风险 | 对策 |
|---|---|
| **在 harness 里重写一份转换逻辑** | 那就验证不到生产代码，本阶段白做。必须抽出共用（Step 1、2） |
| **基线变了就改基线** | 基线变化意味着往返不是位精确的，是抓到真 bug。**先定位字段，不要迁就**（Step 4） |
| **Step 1 重构顺手改行为** | 抽函数必须是纯重构，先单独验证 37 条与基线未动再继续 |
| **把增量脏属性一起拉进来** | 增量缺陷是 S4 暴露的独立议题。测试通路走全量同步，注释写明取舍 |
| **`ConvertSnapshot` 位置假设错** | 动手前确认生产客户端调的是哪份代码，测试必须调同一份（Step 2） |
| 只加用例不改通路 | 只守今天的字段，S8 加字段后盲区回归。这是本文的核心决策 |
| 性能用例耗时上升 | 加开关显式关掉，注释写明原因 |

## 相关实现文件

| 文件 | 改动 |
|---|---|
| `GameShared/FrameSync/Snapshot/SnapshotConverter.cs`（或同类位置） | 新建，`ToProto` / `FromProto` 纯转换，生产与测试共用 |
| `GameServer/Server/Entity/Battle/BattleComponent.cs` | `:207-227` 改调用抽出的转换函数，行为不变 |
| `GameServer/Server/Entity/TestHarness/TestRunner.cs` | `:1057-1063` 的 `OnBroadcast` 加 proto 往返 |
| `GameServer/Server/Entity/TestHarness/SimulatedClient.cs` | `:27` 加接 `BattleWorldSnapshot` 的 `ApplySnapshot` 重载 |
| `GameLogic/Battle/BattleClientController.cs` 或 `BattleSimulation.cs` | `ConvertSnapshot` 纯转换部分抽到共用位置（视 Step 2 确认结果） |
| `GameLogic/Battle/BattlePredictionSelfTestSuite.cs` | 1 条极值用例；注册两处（`:18` 数组 + `:109` switch） |

## 估时

**半天。**

| 部分 | 估时 |
|---|---|
| Step 1 抽转换函数 + 纯重构验证 | 1.5 小时 |
| Step 2 反向转换共用 | 1 小时 |
| Step 3 通路接线 | 1 小时 |
| Step 4-5 基线确认 + 极值用例 | 1 小时 |

## 讲解要点

见 [设计教训-两类静默失效.md](设计教训-两类静默失效.md) 末节。本阶段是「第二类：测试绕过了真实路径」的案例，与 S7 的「第一类：判据绕过了真实区间」成对讲效果更好。
