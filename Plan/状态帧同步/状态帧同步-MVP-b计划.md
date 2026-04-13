# 状态帧同步-MVP-b计划：最小同步链路

## Context

MVP-a 已建立稳定的 30Hz Tick 循环（TickDispatcher + TickAccumulator）、帧计时器（FrameTimerService）和命令对象池（CommandPool）。

MVP-b 在此基础上打通第一条完整同步链路：客户端采集摇杆输入 → 上行服务端 → 服务端权威执行仿真 → 广播快照 → 客户端应用位置。

这是帧同步系统的最小闭环，验收形式为两个 Unity Editor 实例（ParrelSync）可视化同步移动。

前置依赖：
- MVP-a（已完成）：TickDispatcher、FrameTimerService、CommandPool、ClientTickDriver、ServerTickDriver 均已就绪。

---

## 阶段状态

- 状态：`未开始`
- 更新日期：`2026-04-13`

---

## 目标

- 定义帧同步网络协议（3 条消息）。
- 服务端新增 Battle Scene，BattleComponent 实现权威仿真并广播快照。
- 客户端实现虚拟摇杆 UI（JoystickWidget），每 Tick 采集输入并上行。
- 客户端收到快照后直接应用位置到 Capsule。
- 两个 Unity Editor 实例通过本地服务端完成可视化同步验收。

---

## 范围

- 网络协议：`C2B_JoinBattle`、`C2B_PlayerInput`、`S2C_FrameSnapshot`。
- 服务端：Fantasy.config 新增 Battle Scene，`BattleComponent`（ITickable），`BattleMessageHandler`。
- 仿真层（GameShared）：`PlayerState`、`MoveSystem`。
- 客户端：`BattleMainUI`（UIWindow）、`JoystickWidget`（UIWidget）、`BattleClientController`。
- 场景：Unity 场景中放置两个 Capsule，代表两名玩家。

---

## 非目标

- 不实现客户端预测（v0.3b 处理）。
- 不实现插值平滑（v0.2b 处理）。
- 不实现房间匹配/大厅（直接硬连 Battle Scene）。
- 不实现定点数（v0.4a Box2D 集成时引入）。
- 不实现断线重连（v0.6b 处理）。
- 不实现帧号缓冲与超前帧控制（v0.2b 处理）。

---

## 技术决策

### 1. 服务端权威执行

服务端收到输入 → 在 Tick 中执行仿真 → 广播"第 N 帧所有玩家输入执行后的位置"。

客户端不做本地预测，等服务端广播才移动。延迟感知明显，但架构干净，为 v0.3 预测回滚打基础。

若现在选择广播坐标（状态同步），到 v0.3 需要大改架构，因为客户端没有本地仿真可供回滚。

### 2. 输入消息为单向 Message

`C2B_PlayerInput` 不使用 Request/Response 模式，减少一次 RTT 等待。服务端收到即处理，不回包。

### 3. 服务端每 Tick 必广播

即使当帧无任何玩家输入，服务端也广播一次 `S2C_FrameSnapshot`（所有玩家位置不变）。保持帧号连续，客户端可检测丢帧。

### 4. 同帧多条输入取最后一条

同一玩家同一帧发来多条 `C2B_PlayerInput`，取最后一条覆盖。避免网络抖动导致重复移动。

### 5. 仿真层放 GameShared

`PlayerState` 和 `MoveSystem` 放 `GameShared/FrameSync/`，纯 C#，无 Unity/Fantasy 依赖。双端共享同一份移动逻辑，为后续确定性验证打基础。

### 6. 摇杆 UI 使用 TEngine UIWidget

`JoystickWidget` 继承 `UIWidget`，挂在 `BattleMainUI` 下，复用 TEngine 资源加载和 UI 生命周期。Editor 中用鼠标拖拽模拟触摸。

### 7. playerId 由服务端分配

客户端发送 `C2B_JoinBattle` 时不携带 playerId，服务端根据 Session 分配并在响应中返回。避免客户端伪造身份。

---

## 协议定义

### C2B_JoinBattle（Request）

客户端进入战斗时发送一次，服务端返回分配的 playerId。

```
C2B_JoinBattle
  → 无额外字段

C2B_JoinBattleResponse
  playerId: long    // 服务端分配的玩家 ID
  x: float          // 初始位置 X
  y: float          // 初始位置 Y
```

### C2B_PlayerInput（Message，单向）

每 Tick 发送一次，不等回包。

```
C2B_PlayerInput
  frameIndex: uint  // 客户端当前帧号
  dx: float         // 摇杆 X 方向 [-1, 1]
  dy: float         // 摇杆 Y 方向 [-1, 1]
```

### S2C_FrameSnapshot（Message，服务端广播）

服务端每 Tick 广播一次。

```
S2C_FrameSnapshot
  frameIndex: uint          // 服务端当前帧号
  players: PlayerSnapshot[] // 所有玩家状态
    playerId: long
    x: float
    y: float
```

---

## 架构与数据流

```
[Unity Editor A]                      [Fantasy 服务端 - Battle Scene]          [Unity Editor B]
  JoystickWidget                                                                  JoystickWidget
      ↓ Vector2 Direction                                                             ↓ Vector2 Direction
  BattleClientController                                                         BattleClientController
  每 Tick 打包 C2B_PlayerInput                                                   每 Tick 打包 C2B_PlayerInput
      ──────────────────────→  BattleMessageHandler                ←──────────────────────
                                存入当帧输入队列
                                        ↓
                               BattleComponent.Tick()
                                 消费输入队列
                                 MoveSystem.Apply()
                                 广播 S2C_FrameSnapshot
      ←──────────────────────  {frameIndex, players[]}  ──────────────────────→
  BattleClientController                                                         BattleClientController
  按 playerId 找 Capsule                                                          按 playerId 找 Capsule
  transform.position = (x, y)                                                    transform.position = (x, y)
```

---

## 文件结构

```
GameShared/FrameSync/
  Battle/
    PlayerState.cs          // 玩家状态数据（playerId, x, y）
    MoveSystem.cs           // 移动仿真逻辑（纯 C#，无依赖）

GameServer/Server/
  Entity/
    Battle/
      BattleComponent.cs    // ITickable，管理玩家状态 + 广播快照
      PlayerSession.cs      // playerId ↔ Session 映射
  Hotfix/
    Battle/
      Handler/
        C2B_JoinBattleHandler.cs
        C2B_PlayerInputHandler.cs
      System/
        BattleComponentSystem.cs
  Entity/Generate/NetworkProtocol/
    OuterMessage.cs         // 新增 3 条消息（生成代码）
    OuterOpcode.cs          // 新增对应 Opcode

UnityProject/Assets/GameScripts/HotFix/GameLogic/
  Battle/
    BattleClientController.cs   // 连接服务端、发送输入、应用快照
  UI/Battle/
    BattleMainUI.cs             // UIWindow，战斗主界面
    JoystickWidget.cs           // UIWidget，虚拟摇杆
```

---

## 实现思路

### 服务端 BattleComponent

- 维护 `Dictionary<long, PlayerState>` 和 `Dictionary<long, Queue<C2B_PlayerInput>>` 当帧输入队列。
- 实现 `ITickable.Tick(uint frameIndex, float dt)`：
  1. 遍历所有玩家，取当帧最后一条输入（无输入则 dx=dy=0）。
  2. 调用 `MoveSystem.Apply(state, dx, dy, dt)` 更新位置。
  3. 清空当帧输入队列。
  4. 构造 `S2C_FrameSnapshot` 广播给所有在线 Session。
- 注册到 `ServerTickDriver.Dispatcher`。

### GameShared MoveSystem

```
MoveSystem.Apply(PlayerState state, float dx, float dy, float dt)
  state.x += dx * MoveSpeed * dt
  state.y += dy * MoveSpeed * dt
```

`MoveSpeed` 为常量（如 5.0f），定义在 `DeterminismRules` 或独立常量类中。

### 客户端 BattleClientController

- 启动时发送 `C2B_JoinBattle`，收到响应后记录 `selfPlayerId` 和初始位置。
- 注册为 `ITickable`，在 `Tick` 中读取 `JoystickWidget.Direction`，发送 `C2B_PlayerInput`。
- 注册 `S2C_FrameSnapshot` 消息回调，遍历 `players`，按 `playerId` 更新对应 Capsule 的 `transform.position`。

### Fantasy.config 新增 Battle Scene

```xml
<!-- BattleScene ID 范围 1101 - 1150 -->
<scene id="1101" processConfigId="2001" worldConfigId="1"
       sceneRuntimeMode="MultiThread" sceneTypeString="Battle"
       networkProtocol="KCP" outerPort="20101" innerPort="11101" />
```

### OnSceneCreate_Init 新增分支

```
case SceneType.Battle:
  scene.AddComponent<BattleComponent>()
  scene.AddComponent<ServerTickDriver>()
```

---

## 验证方式

### 功能验证

1. 启动服务端，两个 Unity Editor 实例（ParrelSync）各自连接。
2. 两个实例均显示两个 Capsule（自己 + 对方）。
3. Editor A 拨动摇杆，Editor A 和 Editor B 中对应 Capsule 均移动，方向一致。
4. Editor B 拨动摇杆，同上。
5. 同时拨动，双端均正确显示两个 Capsule 的移动。

### 链路验证

- 服务端日志：每 Tick 打印 `[Battle] Frame=N, Players=2, Broadcast`。
- 客户端日志：收到 `S2C_FrameSnapshot` 时打印 `[Battle] ApplySnapshot Frame=N`。
- 帧号连续：客户端收到的 `frameIndex` 严格递增，无跳帧。

### 确定性验证

- `GameShared/FrameSync/Battle/` 下代码不含 `using UnityEngine` 或 `using Fantasy`。
- 两个客户端在相同输入序列下，Capsule 最终位置误差 < 0.001f（float 精度范围内）。

---

## 完成标准

- [ ] 协议消息定义完成，服务端和客户端均可编译。
- [ ] 服务端 BattleComponent 每 Tick 正确执行仿真并广播。
- [ ] 客户端 JoystickWidget 可正常采集输入。
- [ ] 两个 Editor 实例联调，双端 Capsule 移动方向一致。
- [ ] 服务端帧号连续递增，无跳帧。
- [ ] GameShared 仿真层无 Unity/Fantasy 依赖。

---

## 关键风险与应对

| 风险 | 影响 | 应对 |
|------|------|------|
| 无预测导致延迟感知明显 | 操作手感差 | MVP-b 阶段可接受，v0.2b 加超前帧控制 |
| Fantasy.config Battle Scene 路由配置错误 | 客户端无法连接 Battle | 参考现有 Gate Scene 配置，逐字段核对 |
| C2B_PlayerInput 30Hz 高频发送导致消息积压 | 服务端处理延迟 | v0.0a 已验证 30Hz × N 可行，MVP-b 仅 2 人，压力极小 |
| ParrelSync Clone 实例 playerId 冲突 | 双端控制同一角色 | playerId 由服务端 Session 分配，不依赖客户端本地标识 |
| float 坐标在双端累积误差 | 位置不一致 | MVP-b 坐标由服务端单点计算后广播，客户端直接应用，无累积误差；定点数在 v0.4a 引入 |

---

## 后续衔接

- MVP-b 的 `BattleComponent` + `ITickable` 是 v0.2a 帧号协议的基础。
- MVP-b 的 `MoveSystem` 是 v0.4a Box2D 物理集成的替换目标（届时用 Box2D Step 替代简单位移）。
- MVP-b 的 `C2B_PlayerInput` 协议在 v0.2b 中扩展帧号缓冲字段。
- MVP-b 的 `PlayerState` 是 v0.3a 状态快照/恢复的基础数据结构。
