# 状态帧同步-MVP-c计划：日志闭环验收

## Context

MVP-b 已打通最小同步链路：客户端采集键盘输入 → 上行服务端 → 服务端权威执行仿真 → 广播快照 → 客户端应用位置到 Capsule。

MVP-c 在此基础上完成**日志闭环验收**：完善 BattleClientController 的快照应用逻辑，并通过 ParrelSync 双 Editor 实例完成联调验收。

前置依赖：
- MVP-b（已完成）：BattleComponent、BattleClientController、协议三条消息均已就绪。

---

## 阶段状态

- 状态：`未开始`
- 更新日期：`2026-04-14`

---

## 目标

- 补全 BattleClientController 的快照应用逻辑（当前已有骨架，需确认完整性）。
- 新增调试日志输出，记录 Join 结果、快照应用帧号与关键状态。
- ParrelSync 双 Editor 实例联调，目视验证双端 Capsule 移动方向一致。
- 连续运行 10 分钟无逻辑中断、无帧循环异常。

---

## 范围

- 客户端：`BattleClientController` 补全（确认快照应用与日志观测字段输出）。
- 无服务端改动（MVP-b 服务端已满足需求）。
- 无协议改动（现有三条消息已足够）。

---

## 非目标

- 不实现客户端预测（v0.3b 处理）。
- 不实现插值平滑（v0.2b 处理）。
- 不实现精确 RTT 测量（不新增 Ping 消息，日志只做链路观测）。
- 不实现 JoystickWidget（保留键盘 WASD 输入）。
- 不实现帧号缓冲与超前帧控制（v0.2b 处理）。

---

## 技术决策

### 1. 网络延迟日志指标（非精确 RTT）

不新增 Ping/Pong 消息，拆成两个独立指标：

- `JoinRttMs`：加入战斗时 `C2B_JoinBattle` 的一次性往返时间，是唯一真实的 RTT 采样。
- `SnapshotIntervalMs`：相邻两次 `S2C_FrameSnapshot` 到达的时间间隔，用 EMA（alpha=0.1）平滑，反映网络抖动。

两者均仅用于日志观测，不参与仿真逻辑。不命名为"RTT"，避免误导后续版本直接用于超前帧控制。

### 2. 帧差计算

```
frameDelta = (int)serverFrameIndex - (int)lastAppliedFrame
```

- 两个 `uint` 先转 `int` 再相减，避免无符号下溢导致超大正数。
- 显示时裁剪到 `[-300, 300]` 区间，超出范围显示异常标记。
- 帧差 > 0 表示客户端落后服务端，MVP-c 阶段属于正常现象（无预测）。

### 3. 快照接收规则

`OnSnapshotMessage` 中必须先过滤旧帧：

- `frame <= _lastAppliedFrame`：直接丢弃，不应用，不更新任何状态。
- `frame > _lastAppliedFrame`：正常应用，推进 `_lastAppliedFrame`。

此规则防止网络乱序/重复包导致 `LastAppliedFrame` 回退或 Capsule 位置抖动。

### 4. 日志输出规范

BattleClientController 输出结构化日志，用于离线分析：
- Join 成功日志：包含 playerId 与 serverFrame。
- 快照应用日志：包含 playerId、frame 以及必要的状态字段。
- 连续运行验收通过日志脚本统计（不依赖 UI）。

---

## 实现步骤

### Step 1：确认 BattleClientController 快照应用完整性

检查现有 `BattleClientController` 的 `OnSnapshotMessage` 是否：
- 正确更新 `_lastAppliedFrame`
- 正确更新 `_serverFrameIndex`
- 正确调用 `GetOrCreateCapsule` 并应用位置

如有缺失，补全。

### Step 2：BattleClientController 补充日志观测指标

- `JoinBattleAsync` 中记录发出时间，收到响应后计算 `JoinRttMs`。
- `OnSnapshotMessage` 中：
  1. 先过滤旧帧（`frame <= _lastAppliedFrame` 直接 return）。
  2. 用 EMA 更新 `SnapshotIntervalMs`（相邻快照到达间隔）。
  3. 超过 500ms 未收到快照时，`SyncState` 切换为 `Stalled`。
- 不新增 UI 依赖字段，仅保留日志分析所需数据。

### Step 3：ParrelSync 联调验收

- 启动本地服务端（Battle Scene 端口 20101）
- 主 Editor 运行，Clone Editor 运行
- 双端各自连接服务端，目视验证 Capsule 移动方向一致
- 观察客户端日志帧号连续递增、旧帧被过滤、无异常断链日志

---

## 文件结构

```
UnityProject/Assets/GameScripts/HotFix/GameLogic/
└── Battle/
    └── BattleClientController.cs          ← 补全日志观测指标 + 快照应用逻辑（已有文件）
```

服务端无改动。

---

## 确定性验证

- 双客户端在相同输入序列下，Capsule 最终位置误差 < 0.001f。
- 停止输入后，双端 Capsule 位置完全一致（服务端单点计算，无累积误差）。

---

## 完成标准

- [ ] BattleClientController 快照应用逻辑完整，`LastAppliedFrame` 正确递增，旧帧被丢弃。
- [ ] 客户端日志完整输出 Join 成功与快照应用信息，可用于离线脚本统计。
- [ ] ParrelSync 双 Editor 联调，双端 Capsule 移动方向一致。
- [ ] 连续运行 10 分钟，无帧循环异常、无 NullReference、无 Join 失败日志。
- [ ] 服务端帧号连续递增，客户端 `ApplySnapshot` 帧号单调递增（按 player 统计）。

---

## 关键风险与应对

| 风险 | 影响 | 应对 |
|------|------|------|
| BattleClientController 已有骨架但快照应用不完整 | Capsule 不移动 | Step 1 先审查，确认后再补日志 |
| 日志量过大影响定位效率 | 排查成本上升 | 使用结构化日志 + 离线脚本聚合 |
| SnapshotIntervalMs 抖动过大 | 日志波动影响读数 | EMA alpha 调小（0.05） |
| uint 帧号相减下溢 | FrameDelta 出现超大正数 | 转 int 再减，裁剪到 [-300, 300] |
| 快照乱序/重复导致 LastAppliedFrame 回退 | Capsule 位置抖动 | OnSnapshotMessage 先过滤旧帧 |
| ParrelSync Clone 实例 playerId 与主实例冲突 | 双端控制同一角色 | playerId 由服务端 Session 分配，MVP-b 已处理 |

---

## 后续衔接

- MVP-c 的 `JoinRttMs` + `SnapshotIntervalMs` 是 v0.2b 超前帧计算的参考输入（届时需更精确的 Ping 消息替换）。
- MVP-c 的 `FrameDelta` 是 v0.2b 追帧触发条件的基础指标。
- MVP-c 的日志观测指标在后续版本中持续扩展（v0.3 加快照哈希、v0.4 加物理帧耗时）。
