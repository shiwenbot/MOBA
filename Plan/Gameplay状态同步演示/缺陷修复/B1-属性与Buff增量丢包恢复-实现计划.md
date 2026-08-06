# B1：属性与 Buff 增量丢包恢复

**性质**：🔴 真 bug
**优先级**：先修（B2 之前）
**日期**：2026-08-05
**状态**：已完成；自动化验收见 `Tools/AutomationAcceptance/属性与Buff增量恢复-验收测试指南-20260805.md`

## 1. 缺陷描述

### 1.1 属性侧

服务端 `BattleComponent.cs:284-288` 在发送后**无条件**更新 `_lastBroadcastAttributesByPlayerId`，不检查客户端是否收到。客户端 `BattleClientController.cs:451` 先判丢包，丢了直接 `return`，`ConvertSnapshot` 根本不执行，`_authoritativeAttributesByPlayerId` 这一帧完全没更新。

分叉过程：

```
帧 N    服务端 Health 100→50，dirtyMask=Health，发出 → 客户端丢包
        服务端 baseline 更新为 50
帧 N+1  ComputeDirtyMask(50, 50) = None，Health 字段不发
        客户端 Merge(baseline=100, mask=None) → 仍是 100
帧 N+2… 永久停在 100，直到该字段再次变化
```

客户端血量永久错误，哈希从此帧帧失配。

### 1.2 Buff 侧（同样有，且更隐蔽）

`BuildBuffSyncPayload`（`BattleComponent.cs:481-515`）全文只有一处置 `IsFullSync = true`：`:484-493`，条件是 baseline 字典查不到该 `(observer, target)` 对，即**该观察者第一次看到这个玩家**。之后永远走增量，**没有任何周期性全量**。

Buff 有的是**帧号握手自检**：服务端发 `BuffSnapshotFrameIndex`（本条增量基于哪一帧），客户端 `:823` 比对 `baseline.FrameIndex != player.BuffSnapshotFrameIndex`，不等就打 `[Battle] Buff delta dropped` 并塞进 `_playersAwaitingBuffFullSync`。

**只能检测，不能恢复**：该标志没有任何上行消息通知服务端，服务端不知情继续发增量。真实行为是**后续非空增量被持续拒绝，Buff 状态冻结在陈旧值**（`:809-836` 早退路径）。标志只在 `IsBuffFullSync` 到达时清除（`:805`），而那要等服务端侧 baseline 被删除，即玩家重进。

> 注：`BuffDirtyMask == 0` 时在 `:810` 就早退了，不会每帧打日志；`_playersAwaitingBuffFullSync.Add` 对已存在元素是 no-op，标志不会「自我重置」。

### 1.3 为什么共享代码解决不了

| 部分 | 是否共享 | 位置 |
|---|---|---|
| 快照结构体 `PlayerAttributeSnapshot` | ✅ | GameShared |
| diff/merge 函数 | ✅ | `PlayerAttributeSync.cs` |
| **baseline（上一帧的值）** | ❌ **各持一份** | 服务端 `_lastBroadcastAttributesByPlayerId:17`、客户端 `_authoritativeAttributesByPlayerId:31` |

baseline 是可变本地状态，在两个进程的内存里。delta 编码要求双方对「你手上那份是什么」有一致认知，服务端发完就更新自己的副本等于断言送达。假设一破，函数再共享也算不对，因为**输入前提已经错了**。

### 1.4 KCP 不丢包，为什么还会踩

「晚到等同丢包」。弱网延迟、断线重连、切后台都会让某帧的快照错过应用窗口。S4 弱网框架直接注入丢包（`NetworkConditionSimulator.ShouldDropDownlink`），必然触发。

## 2. 修法：周期性全量兜底

选它而非 ACK 的理由：不新增消息类型、不需要服务端存 per-session 历史、改动量小。代价是恢复窗口有界而非即时（见第 4 节）。

**目标不是消除失配，是把「永久分叉」降级为「有界窗口」。**

### 2.1 属性加周期性全量

在 `BroadcastSnapshot`（`BattleComponent.cs:153`）的 dirtyMask 计算处（`:190-192`）追加强制全量条件：每 F 帧（**建议 F = 60**）对该玩家强制 `PlayerAttributeDirtyFlags.All`，打包完整属性值。

客户端 `PlayerAttributeSync.Merge` 收到 `All` 时会采用所有字段（`PlayerAttributeSync.cs:53-58`），**逻辑现成，客户端不需要改**。

`_lastBroadcastAttributesByPlayerId` 保持全 battle 共享，**不用改成 per-session**——全量按绝对帧号触发，同一玩家对所有 session 在同一帧全量，天然对齐。

### 2.2 Buff 加周期性全量 —— 必须同时更新服务端 baseline

**这是本计划最容易写错的一步，会导致修法完全失效。**

`BuildBuffSyncPayload` 现在只在两处动 baseline：首次创建（`:486-487`），和有变化时 `baseline.Update(..., frameIndex)`（`:508`）。

如果强制全量只是让 payload 返回 `IsFullSync = true` 而不动 baseline：

```
帧 60   服务端发全量，但 baseline.FrameIndex 仍是旧值（比如 12）
        客户端收全量 → 自己的 baseline.FrameIndex = 60   (:801-804)
帧 75   有变化，服务端发增量，携带 baselineFrameIndex = 12
        客户端比对 60 != 12 → :823 判定分叉 → 拒绝 → 重新置位等待标志
```

**全量刚修好立刻又坏，且稳定复现。** 所以强制全量分支必须同时把该 `(observer, target)` baseline 的**内容和 FrameIndex 一起推进到全量帧**。

好消息：`BuffBroadcastBaseline` 是可变类，有 `Update(activeBuffs, nextRuntimeBuffId, frameIndex)` 方法（`BuffBroadcastBaseline.cs:18-23`），直接复用即可，不需要新类型。

### 2.3 全量帧编排

- **同一玩家的属性与 Buff 全量对齐到同一帧** —— 一个包修两样，省带宽
- **不同玩家按 `playerId % F` 错开** —— 把尖峰从「F 帧一次、全员同时」摊成「每帧约 N/F 个玩家」。每个玩家的全量间隔仍是 F，不影响恢复窗口上界

### 2.4 属性加 baseline 帧号握手（需改 proto）

反向抄 Buff 的检测机制，给 `PlayerSnapshot` 新增 `AttributeBaselineFrameIndex`。属性现在连「我错了」都察觉不到，出问题只能靠哈希失配倒推，加这个能直接打出分叉日志。

**需要改 proto 并跑 `Run.bat`。** 本计划可以说「不新增 ACK 消息类型、不需要服务端存 per-session 历史」，但**不能说「不改 proto 结构」**。

**帧号语义必须钉死**：是「属性 baseline **最后一次真正变化或发送全量**的帧」，**不是当前广播帧**。

理由：服务端 `:287` 每帧无条件覆盖属性 baseline，客户端 `:664` 每收一帧就写回缓存。若帧号跟着每帧递增，那么丢掉一个**完全没有属性变化**的包也会产生帧号不等，打出无意义的分叉告警。

正确做法照 Buff 现在的样子：
- 服务端：只在 `dirtyMask != None` 时推进该帧号（需要新增「属性 baseline 值 + 最后变更/全量帧号」的组合状态，现在的 `Dictionary<long, PlayerAttributeSnapshot>` 只存了值）
- 客户端：只在应用了非空增量或全量时推进

这样丢无变化的包不误报，丢带变化的包必被下一条增量检测到。

## 3. 实现步骤

1. **改 proto**：`PlayerSnapshot` 新增 `AttributeBaselineFrameIndex`，跑 `GameServer/Tools/ProtocolExportTool/Run.bat` 生成两端代码
2. **服务端属性状态升级**：`_lastBroadcastAttributesByPlayerId` 的值类型从 `PlayerAttributeSnapshot` 扩成「值 + 最后变更/全量帧号」的组合，注意 `:147` 的清理和 `:317` 的带宽对照测量两处引用要跟上
3. **服务端属性周期性全量**：`:190-192` 的 dirtyMask 计算追加 `frameIndex % F == playerId % F` 强制 `All`，并推进帧号
4. **服务端 Buff 周期性全量**：`BuildBuffSyncPayload` 加强制全量分支，**同时调用 `baseline.Update(player.ActiveBuffs, player.NextRuntimeBuffId, frameIndex)`**，全量帧与属性对齐
5. **客户端属性帧号自检**：`ConvertSnapshot` 内比对 baseline 帧号，不等则打 `[Battle] Attribute baseline diverged` 告警，并按当前 baseline 继续（等下一个全量恢复）
6. **测试场景代码**（与实现同批产出，见第 5 节）
7. **写验收指南**到 `Tools/AutomationAcceptance/属性与Buff增量恢复-验收测试指南-20260805.md`

## 4. 恢复窗口的数学前提（验收判据基础）

周期性全量只保证**最终恢复**，前提是后续至少有一个全量包成功送达。全量包本身也会丢，且可以连丢多个。

```
最大恢复窗口 ≤ (C + 1) × F 帧
单次分叉的 mismatch 上报次数 ≤ ⌈(C + 1) × F / H⌉

F = 60   全量间隔（帧）
H = 30   哈希上报间隔（BattleSimulation.cs:19 HashReportIntervalFrames）
C = 允许的连续全量丢包数
```

**C 必须作为显式测试参数**，没有对 C 的约束就不存在确定上界。F = 60、H = 30、C = 1 时是 4 次上报窗口。

**不要写「最多退化到 120 帧」** —— 那隐含假设全量只丢一次。

随机丢包测试必须**固定 seed**并记录实际分叉次数；或者改用统计阈值。单靠「跑 N 秒」推不出固定 mismatch 上限，因为还取决于产生分叉的状态变化次数与丢包时序。

## 5. 验收交付物

### 5.1 必须有的测试用例

1. **属性全量恢复**：注入单帧丢包（该帧含属性变化）→ 验证 mismatch 出现 → 全量帧后 → 验证客户端属性与服务端一致、mismatch 停止增长
2. **Buff 全量恢复**：同上，针对 Buff
3. **全量后紧跟增量不被拒绝**（针对 2.2 的陷阱）：强制全量帧之后立刻制造一次 Buff 变化 → 验证客户端**不**打 `Buff delta dropped`、增量被正常应用。**这个用例不写，2.2 的 bug 会溜过去**
4. **无变化丢包不误报**（针对 2.4 的语义）：丢掉一个属性无变化的包 → 验证**不**打 baseline 分叉告警
5. **全量帧错开**：多玩家场景验证全量不集中在同一帧

### 5.2 验收指南必须写清

- mismatch 判据是**有界、不随时间线性增长**，**不是 == 0**。给出 `⌈(C+1)×F/H⌉` 的算式和本次测试取的 C 值
- 固定的随机 seed
- 全量包连丢导致窗口退化到 `(C+1)×F` 是**预期行为**，不是新 bug

## 6. 关键决策记录

| 决策 | 理由 |
|---|---|
| 选周期性全量而非 ACK | 不新增消息类型、不存 per-session 历史，改动量最小。代价是恢复有界而非即时 |
| F = 60 | 30Hz 下 2 秒。可调，若弱网测试显示窗口过长再下调 |
| 属性 baseline 保持全 battle 共享 | 全量按绝对帧号触发，同一玩家对所有 session 同帧全量，天然对齐 |
| 全量帧按 playerId 错开 | 避免带宽尖峰；同一玩家的属性与 Buff 仍同帧，保住「一个包修两样」 |
| 属性帧号语义 = 最后变更/全量帧 | 若用当前广播帧，丢无变化的包会误报分叉 |

## 7. 相关实现文件

| 文件 | 关注点 |
|---|---|
| `GameServer/Server/Entity/Battle/BattleComponent.cs` | `:17-18` baseline 声明、`:153` BroadcastSnapshot、`:190-192` dirtyMask、`:287` 无条件更新、`:481-515` BuildBuffSyncPayload |
| `GameServer/Server/Entity/Battle/BuffBroadcastBaseline.cs` | `:18` Update 方法，强制全量复用它 |
| `UnityProject/.../GameLogic/Battle/BattleClientController.cs` | `:451` 丢包早退、`:641-703` ConvertSnapshot、`:664` 属性写回、`:791-846` ResolveAuthoritativeBuffSnapshot |
| `UnityProject/.../GameShared/FrameSync/Battle/PlayerAttributeSync.cs` | 共享 diff/merge，本次**不需要改** |
| `GameServer/Tools/NetworkProtocol/Outer/OuterMessage.proto` | `:101-127` PlayerSnapshot |
