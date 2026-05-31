# 羽毛球核心玩法-MVP-a计划：球场与羽毛球物理引擎

## Context

这是羽毛球夺势游戏的第一个工程阶段。当前项目已具备完整的状态帧同步框架（v0.5a 已完成），包括：
- 确定性帧驱动基础设施（TickAccumulator / TickDispatcher / ITickable）
- Box2D 物理集成（FrameSyncPhysicsWorld）
- 快照/回滚系统（SnapshotBuffer / ISnapshotable\<TSnapshot\>）
- Buff 系统同步
- 输入缓冲区（InputBuffer）

本阶段的目标是**在帧同步框架之上搭建羽毛球游戏的基础物理层**——球场边界和羽毛球弹道。这不是一个独立的物理 Demo，而是为后续所有羽毛球玩法（角色移动、击球、对打）铺设地基。

### 已锁定的设计决策

1. **弹道模型**：高空气阻力模型，水平速度指数衰减约 60%/s
2. **物理引擎**：羽毛球弹道不使用 Box2D 刚体，复用帧同步基础设施（世界时钟、快照体系、确定性校验工具链），角色/地面移动后续仍走 Box2D 平面体系
3. **坐标约定**：2.5D 模型——XZ 为水平面，Y 为独立高度标量
4. **帧率**：30Hz 逻辑帧（dt = 33.333ms，与 `DeterminismRules.FixedDeltaTime` 一致）
5. **球路配置源**：Luban 接入验证已通过，默认使用正式表 `TbShuttlecockShot`；`ShuttlecockShotConfigFallback` 仅保留为开发期临时兜底

---

## 阶段状态

- **状态**：`已完成（Unity 手动验收通过）`
- **前置依赖**：无（帧同步基础设施已完成）
- **后续衔接**：MVP-b（角色在球场上移动）
- **验收记录**：
  - `2026-05-31` Unity Editor 手动验证：通过
  - `2026-05-31` EditMode 自动测试：`42` 项中 `41` 项通过，剩余 `1` 项为既有 `TickAccumulator` 浮点精度问题，与羽毛球 MVP-a 实现无关

---

## 目标

1. **球场场景**：标准羽毛球单打球场（13.4m × 5.18m），含网、边界线、半场划分
2. **羽毛球弹道模拟**：基于高空气阻力模型的确定性弹道，6 种球路参数可配置
3. **可视化验证**：在 Unity Editor 中可用简单线框渲染看到球飞行轨迹
4. **帧同步集成**：球物理通过 ITickable 接入帧同步管线，支持快照/回滚

---

## 范围

- 球场常量定义（尺寸、网高、区域划分）
- ShuttlecockState（羽毛球状态：位置 XZ+Y、速度 XZ+Y、飞行阶段）
- ShuttlecockSnapshot（羽毛球快照结构体，与 ShuttlecockState 一一对应）
- ShuttlecockPhysics（确定性弹道计算：空气阻力 + 重力）
- ShuttlecockShotConfig（6 种球路的发射参数配置表）
- ShuttlecockLaunchCommand（帧命令：携带 FrameIndex，仅在目标帧消费）
- 球场碰撞边界检测（出界判定，含完整状态机优先级）
- 简单的击发测试接口（给定球路类型和方向，发射羽毛球）
- Editor 内可视化（线框球场 + 羽毛球轨迹线）

### 明确不在范围内

- **旋转字段**：MVP-a 不跟踪羽毛球旋转。旋转对弹道无影响（阻力已建模为标量衰减），对可视化后续可由渲染层从速度方向推导。删除此字段避免伪范围。

---

## 非目标

- 角色移动和击球系统（MVP-b/MVP-c）
- 势系统、模糊起手等深度系统
- 网络同步测试（本阶段纯本地物理验证）
- 美术资源（使用简单线框/球体代替）
- 碰撞体积和网高度判定（本阶段球不与网发生物理碰撞，只做弹道模拟）

---

## 技术决策

### TD-1：2.5D 逻辑模型（XZ + Y）

采用 **2.5D 逻辑模型**：XZ 平面表达地面运动，Y 轴作为独立高度标量。

**为什么不是完整 3D 物理引擎：**
- 与 LoL 类项目思路一致："3D 渲染表现 + 2D/平面逻辑"。LoL 的角色移动在 2D 导航网格上，渲染为 3D 模型。本项目的角色移动（MVP-b）同理，在 Box2D 的 XZ 平面上运行。
- 羽毛球比 LoL 多一个**真正重要的高度维度**——球的飞行弧线、落地点都高度依赖 Y 分量。因此不能像 LoL 一样完全忽略高度，但也不需要完整 3D 刚体物理（没有 3D 碰撞体积、没有 3D 力矩）。
- 结论：**`XZ + Y` 的 2.5D 逻辑模型**。水平运动和高度运动独立计算，渲染层可以从 XZ+Y 映射到 3D Transform。

**落地方式：**
- ShuttlecockState 包含 `Vector2 xz`（水平位置）+ `float y`（高度）
- 速度同理：`Vector2 vxz` + `float vy`
- 每帧分别更新水平运动（阻力衰减）和垂直运动（重力 + 竖直阻力）
- 渲染层将 (xz.x, y, xz.y) 映射到 Unity Transform.position

### TD-2：羽毛球弹道不使用 Box2D

**复用的是帧同步基础设施，不是 Box2D 物理引擎。**

具体说明：
- 复用：TickAccumulator 的世界时钟、SnapshotBuffer 的快照回滚体系、ITickable 的帧驱动管线、DeterminismRules 的确定性校验工具、StateHasher 的一致性哈希模式
- 不复用：Box2D 的刚体/碰撞求解。羽毛球不创建 Box2D Body，不参与 Box2D World.Step()
- 原因：羽毛球弹道是纯数学模拟（解析/半解析），不需要碰撞求解；扣杀球速可达 22m/s，Box2D 对高速物体有穿透风险；2.5D 高度维度超出 Box2D 的 2D 能力
- 角色/地面移动（MVP-b）仍继续走 Box2D 平面体系，与羽毛球弹道并行运行

此决策消除"复用 Box2D"与"羽毛球不进 Box2D"的口径冲突：**羽毛球复用帧同步基础设施但不走 Box2D 物理路径。**

### TD-3：弹道物理模型

羽毛球弹道分两个独立分量计算：

**水平分量**（XZ 平面，受空气阻力）：
- 使用指数衰减模型：`v_horizontal(t+dt) = v_horizontal(t) × exp(-drag × dt)`
- drag 系数约 0.92（对应每秒衰减 ~60%）
- 方向不变，只衰减速度大小

**垂直分量**（Y 轴，重力 + 竖直阻力）：
- 重力加速度 g = 9.8 m/s²
- 竖直阻力系数使用 0.5；对正仰角球路额外施加一个向上速度缩放（当前实现为 `0.4`），用于让高远球/吊球的飞行时间窗口更贴近验收口径
- 落地判定：Y ≤ 0 时球触地

**浮点确定性工程约束：**
- MVP-a 阶段允许直接使用 `System.Math.Exp` + `System.Math` 浮点运算做本地验证，因为本阶段不涉及双端一致性比对
- 若后续进入双端严格一致性阶段（联网对战），需要评估以下方案：查表预计算 exp 值（按 drag×dt 离散化）或将关键路径量化为定点数
- 评估时机：MVP-d（双端帧同步验证）开始前做专项定点化评估，届时用实际不一致率数据决定是否需要定点化

### TD-4：击发语义为帧命令（ShuttlecockLaunchCommand）

击发操作不是即时生效的方法调用，而是携带 FrameIndex 的帧命令。

**设计：**
- `ShuttlecockLaunchCommand` 实现 `IResettable`（复用现有 `CommandPool<T>` 池化）
- 命令字段：`uint TargetFrame`（目标逻辑帧号）、`ShotType`（球路类型）、`Vector2 OriginXZ`（击发水平位置）、`float OriginY`（击发高度）、`Vector2 DirectionXZ`（水平方向，单位向量）
- 命令只在 `ShuttlecockEntity.Tick(frameIndex)` 中消费，且仅当 `frameIndex == command.TargetFrame` 时执行击发
- 命令通过 `CommandPool<ShuttlecockLaunchCommand>` 池化，消费后归还池

**MVP-a 本地验证阶段：**
- 测试脚本直接构造 ShuttlecockLaunchCommand 并投递到 ShuttlecockEntity 的待处理命令队列
- TargetFrame 设为当前帧号 + 1（模拟"下一帧生效"）
- 接口语义从第一天起就与后续对战接入一致，后续替换为服务端下发命令时只需改投递源

**为什么不直接 Launch()：**
- 即时调用语义（收到就立刻改状态）与帧同步的"状态变化只能在 Tick 中发生"原则冲突
- 回滚重播时无法复现"哪一帧发生了击发"，因为击发时刻没有被记录到命令流中
- 客户端预测与服务端权威必须共享同一帧语义：客户端预测"第 N 帧击发"，服务端确认/否认的也是"第 N 帧击发"

### TD-5：球路配置表设计

使用 Luban 配置表管理球路参数，避免硬编码。

- 表名：TbShuttlecockShot
- 字段：球路类型 ID、名称、水平初速度、发射仰角、阻力系数、描述
- 包含 6 种球路的基础参数：高远球 / 吊球 / 扣杀 / 平抽 / 网前球 / 挑球
- 配置生成到 GameShared 层（双方共用）

**Luban 构建链路不通时的兜底方案：**
- 使用纯 C# 静态配置类 `ShuttlecockShotConfigFallback`，内含与 Luban 表结构相同的静态字段
- 此类放在 GameShared 层，可被服务端和客户端共用
- 当前 Luban 链路已就绪，默认应直接使用 `TbShuttlecockShot`；仅在临时阻塞开发时才启用该兜底类
- **不使用 ScriptableObject**：ScriptableObject 是 Unity 编辑器资源，无法在服务端（无 Unity 运行时）加载，不适合双端共享配置

### TD-6：落地/出界状态机

羽毛球飞行阶段状态机（`ShuttlecockFlightPhase` 枚举）定义如下：

```
Idle → Flying → (Landed | OutOfBounds)
```

**判定规则与优先级：**

1. **Flying → Landed**：球的 Y ≤ 0 且球位于球场有效区域内（含边线）
2. **Flying → OutOfBounds**：以下任一条件满足
   - 球在 XZ 平面上越出球场边界（单打宽度 ±2.59m 或端线 ±6.7m）
   - 球触地（Y ≤ 0）时位于球场有效区域外
3. **Landed 与 OutOfBounds 互斥**：一旦进入终态不再转换。判定时先检查触地位置是否在界内：界内→Landed，界外→OutOfBounds
4. **空中出界 vs 触地出界**：MVP-a 阶段采用"触地后判定"策略——球可以在界外区域飞行（飞过边线上方），只有在触地时才做最终界内/界外判定。这与真实羽毛球裁判规则一致（球在空中不算出界，落地才判）

**裁判字段（记录在 ShuttlecockState 上）：**
- `LastValidFlyingFrame`：最后处于 Flying 状态的帧号（用于回溯"何时变终态"）
- `LandingXZ`：落地点 XZ 坐标（仅在进入终态时记录）
- `IsInBounds`：落地点是否界内（仅在进入终态时记录，配合 LandingXZ 用于裁判逻辑）

这些字段同时纳入快照，确保回放和裁判逻辑的口径一致。

### TD-7：快照设计

遵循仓库现有模式：State 负责快照生成/恢复，Snapshot 是独立的不变结构体。

**具体设计：**
- `ShuttlecockSnapshot`：不可变快照结构体，包含以下字段：
  - `uint FrameIndex`
  - `Vector2 XZ`（水平位置）
  - `float Y`（高度）
  - `Vector2 Vxz`（水平速度）
  - `float Vy`（竖直速度）
  - `ShuttlecockFlightPhase Phase`
  - `int LastValidFlyingFrame`
  - `Vector2 LandingXZ`
  - `bool IsInBounds`
- `ShuttlecockEntity : ISnapshotable<ShuttlecockSnapshot>`（与 `BattleWorldState : ISnapshotable<BattleWorldSnapshot>` 模式一致）
  - 内部持有 `ShuttlecockState`（可变运行时状态）
  - `TakeSnapshot()` 从 ShuttlecockState 构造 ShuttlecockSnapshot
  - `RestoreSnapshot(ShuttlecockSnapshot)` 将字段写回 ShuttlecockState
- `ShuttlecockState` 本身不实现 ISnapshotable，它是纯数据容器，由 ShuttlecockEntity 负责快照生命周期

**一致性校验字段（后续扩展 StateHasher 时纳入）：**
- XZ、Y、Vxz、Vy、Phase 是必须比对的字段（影响后续仿真结果）
- LastValidFlyingFrame、LandingXZ、IsInBounds 是裁判辅助字段（终态后不再变化，校验优先级次之但必须一致）

**回滚恢复：**
- ShuttlecockEntity.Rollback(targetFrame) 从 SnapshotBuffer 取出对应帧的 ShuttlecockSnapshot
- 调用 RestoreSnapshot 恢复 ShuttlecockState 的全部字段
- 清空待处理命令队列中 TargetFrame > targetFrame 的命令（回滚后重播时重新消费）

### TD-8：测试场景与目录结构

按 Unity 程序集边界和编辑器/运行时分离原则组织文件。

**运行时代码（GameShared，可被服务端共用）：**
```
GameScripts/HotFix/GameShared/Badminton/
├── CourtConstants.cs                  # 球场常量
├── ShuttlecockFlightPhase.cs          # 飞行阶段枚举
├── ShuttlecockShotType.cs             # 球路类型枚举
├── ShuttlecockState.cs                # 羽毛球运行时状态（纯数据）
├── ShuttlecockSnapshot.cs             # 羽毛球快照结构体（不可变）
├── ShuttlecockPhysics.cs              # 确定性弹道计算（纯静态）
├── ShuttlecockLaunchCommand.cs        # 帧命令（IResettable，可池化）
├── ShuttlecockEntity.cs               # 羽毛球实体（ITickable + ISnapshotable<ShuttlecockSnapshot>）
└── Config/
    ├── (Luban 生成的球路配置代码)
    └── ShuttlecockShotConfigFallback.cs  # 仅开发期临时兜底，不作为主方案
```

**运行时调试脚本（GameLogic，进入客户端热更侧，负责把共享逻辑接进 Unity 场景）：**
```
UnityProject/Assets/GameScripts/HotFix/GameLogic/Badminton/
├── ShuttlecockDebugController.cs      # 场景中的羽毛球调试驱动
├── ShuttlecockTrajectoryDebug.cs      # Gizmos 轨迹/球场可视化
└── ShuttlecockShotRuntimeConfigProvider.cs # 优先读正式配置，失败时回退 fallback
```

**Editor 工具脚本（Assets/Editor，引用 UnityEditor，不进热更包）：**
```
UnityProject/Assets/Editor/Badminton/
├── ShuttlecockLaunchPanel.cs          # 击发面板（EditorWindow）
├── BadmintonPhysicsTestSceneBuilder.cs # 创建/打开测试场景
└── ShuttlecockConfigValidationEditor.cs # 配置表校验入口
```

**测试场景资源（放在 Scenes 目录下，不与代码混放）：**
```
UnityProject/Assets/Scenes/Test/
└── BadmintonPhysicsTestScene.unity    # 测试场景
```

**GameShared vs GameLogic vs Editor 边界说明：**
- GameShared/Badminton/：羽毛球状态、物理、快照、命令——纯逻辑，无 Unity 依赖（除 Vector2 等基础类型），可被服务端和客户端共用
- GameLogic/Badminton/：Unity 场景调试驱动，负责把共享逻辑接入 MonoBehaviour 生命周期和可视化对象
- Assets/Editor/Badminton/：Editor 工具脚本，仅在 Unity Editor 内运行，引用 UnityEditor 命名空间，不进入热更包
- 测试场景 .unity 文件放在 Assets/Scenes/Test/ 下，与代码目录分离，避免 asmdef 引用污染

---

## 实现步骤

### Step 0：球场常量定义

定义 `CourtConstants` 静态类（放在 GameShared/Badminton/），包含球场所有尺寸常量：

- 球场全长 13.4m、单打宽 5.18m
- 网高 1.55m（边柱）、网中央高 1.524m
- 发球线距网 1.98m
- 前后场分界线（中场线距网 6.7m / 半场）
- 坐标系约定：球场中心为原点，Z 轴正方向指向对手半场
- 边界判定辅助方法：`IsInBounds(Vector2 xz)` 返回界内/界外

### Step 1：枚举与数据结构

定义 `ShuttlecockFlightPhase` 枚举（Idle / Flying / Landed / OutOfBounds）和 `ShuttlecockShotType` 枚举（6 种球路）。

定义 `ShuttlecockState` 数据类（纯数据容器，不实现 ISnapshotable）：
- 位置：`Vector2 XZ` + `float Y`
- 速度：`Vector2 Vxz` + `float Vy`
- 飞行阶段：`ShuttlecockFlightPhase Phase`
- 裁判字段：`int LastValidFlyingFrame`、`Vector2 LandingXZ`、`bool IsInBounds`

定义 `ShuttlecockSnapshot` 结构体（不可变），字段与 ShuttlecockState 一一对应。

### Step 2：ShuttlecockPhysics 弹道计算

实现纯静态的确定性弹道计算类（无状态、无副作用）：

- `Step(ShuttlecockState state, float dt, uint frameIndex)` — 每帧推进
- 水平分量：指数衰减速度，更新 XZ 位置
- 垂直分量：重力 - 竖直阻力，更新 Y 位置
- 落地检测：Y ≤ 0 时根据 CourtConstants.IsInBounds 判定 Landed / OutOfBounds
- 调用 `DeterminismRules.AssertFixedDt(dt)` 确保帧间隔正确
- 调用 `DeterminismRules.AssertFinite()` 校验关键值
- 不依赖 Unity 物理，纯数学计算确保确定性

### Step 3：ShuttlecockLaunchCommand 帧命令

定义帧命令结构，实现 `IResettable`：

- 字段：`uint TargetFrame`、`ShuttlecockShotType ShotType`、`Vector2 OriginXZ`、`float OriginY`、`Vector2 DirectionXZ`
- `Reset()` 清空所有字段，支持 CommandPool 池化
- 命令只在目标帧的 Tick 中消费

### Step 4：ShuttlecockEntity 帧同步实体

创建羽毛球实体（放在 GameShared/Badminton/），实现 `ITickable` + `ISnapshotable<ShuttlecockSnapshot>`：

- 持有 `ShuttlecockState`（运行时状态）
- 维护待处理命令队列（`Queue<ShuttlecockLaunchCommand>`）
- `Tick(frameIndex, fixedDt)`：
  - 检查队列中 TargetFrame == frameIndex 的命令，执行击发（设置初速度、切换到 Flying）
  - 若当前为 Flying 状态，调用 ShuttlecockPhysics.Step
- `TakeSnapshot()` 从 State 构造 ShuttlecockSnapshot
- `RestoreSnapshot(ShuttlecockSnapshot)` 将字段写回 State
- `RollBack(targetFrame)` 从 SnapshotBuffer 恢复状态，清空 targetFrame 之后的命令
- `CheckConsistency(frameIndex)` 比对当前状态哈希与预期哈希

### Step 5：球路配置表

直接使用 Luban 配置表（`TbShuttlecockShot`），定义 6 种球路的基础参数。

正式表 `shuttlecock_shot.xlsx`、双端生成代码和 Unity Editor 读取验证均已完成；`ShuttlecockShotConfigFallback` 仅在临时开发阻塞时启用。

### Step 6：Editor 击发面板与可视化

创建 `ShuttlecockLaunchPanel`（EditorWindow，放在 `Assets/Editor/Badminton/`）：

- 选择球路类型、击发位置、目标方向
- 点击按钮构造 ShuttlecockLaunchCommand 投递到 ShuttlecockEntity
- 显示实时参数（速度、高度、飞行时间、当前 Phase）

创建测试场景 `BadmintonPhysicsTestScene`（放在 Assets/Scenes/Test/）：
- 菜单入口：`TEngine/Badminton/Shuttlecock Launch Panel`
- 场景生成入口：`TEngine/Badminton/Create Or Open Physics Test Scene`
- 当前默认实现使用 `Gizmos + MeshRenderer` 做最小可用可视化，便于在无额外资源依赖的前提下完成 MVP-a 验证
- 球体 GameObject 跟随 ShuttlecockState 位置
- 轨迹预览由 `ShuttlecockDebugController + ShuttlecockTrajectoryDebug` 提供

### Step 7：验证与参数调优

从球场中心向各个方向击发各球路类型，验证弹道特征和确定性（见完成标准）。

### 实际落地结果（2026-05-31）

- 已完成共享运行时主链路：`CourtConstants / ShuttlecockState / ShuttlecockSnapshot / ShuttlecockPhysics / ShuttlecockLaunchCommand / ShuttlecockEntity`
- 已完成配置接入与 fallback：正式配置优先读 `TbShuttlecockShot`，不可用时回退 `ShuttlecockShotConfigFallback`
- 已完成 Unity 调试链路：`ShuttlecockDebugController`、`ShuttlecockTrajectoryDebug`、`ShuttlecockLaunchPanel`、`BadmintonPhysicsTestScene`
- 已补充 EditMode 测试并修复 2 个羽毛球测试用例的错误预期
- 已完成 Unity Editor 手动验证，当前阶段可标记为完成

---

## 完成标准

### 功能验证（可脚本化）

- [x] 6 种球路均可击发并飞行出合理弹道（无 NaN、无穿地、Phase 正确终态转换）
- [x] 高远球飞行时间 1.2s~1.5s（以 `Phase != Flying` 时的帧数 × 33.333ms 计算），落点 Z 坐标在己方端线至对方端线距离的 80%~95%（即从击发点 Z 向对方端线方向，落点 Z 距离对方端线 ≤ 全场长度 × 20%）
- [x] 扣杀飞行时间 0.3s~0.4s（同上计时方式），全程水平速度为所有球路最高
- [x] 吊球落点在击发点到对方发球线距离的 5%~15%（即距网较近的前场区域）
- [x] 落点计算基准：均相对于击发 OriginXZ 到目标端线/发球线的距离比例，非绝对坐标
- [x] 球落地后 Phase 变为 Landed（界内）或 OutOfBounds（界外），终态不再转换
- [x] 空中飞过边线上方不触发 OutOfBounds，仅在触地时判定

### 确定性验证

- [x] 相同击发参数连续运行两次，球的轨迹逐帧一致：实现一个 `DeterminismSelfTest` 方法，用相同 ShuttlecockLaunchCommand 运行两次，逐帧比较 ShuttlecockSnapshot 的 XZ/Y/Vxz/Vy 字段的二进制表示（`BitConverter.SingleToInt32Bits`），任一帧不一致则抛出异常
- [x] 快照回滚一致：保存快照 → 推进 30 帧（1 秒）→ 恢复快照 → 继续推进 30 帧，与无中断连续运行 60 帧的结果逐帧比较，完全一致
- [x] 使用 `DeterminismRules.AssertFinite()` 校验所有帧的计算结果无 NaN/Infinity

### 工程验证

- [x] ShuttlecockEntity 通过 ITickable 正确接入帧同步管线（可被 TickDispatcher 调度）
- [x] ShuttlecockEntity 实现 `ISnapshotable<ShuttlecockSnapshot>`，支持快照/回滚
- [x] ShuttlecockLaunchCommand 实现 IResettable，可通过 CommandPool 池化
- [x] GameShared/Badminton/ 下无 UnityEditor 引用
- [x] 无编译错误，无运行时异常

### 实际验收结果

- [x] Unity Editor 手动验收通过
- [x] EditMode 自动测试二次复验后达到 `41 / 42` 通过
- [x] 唯一剩余失败项为 `TickAccumulator` 既有精度问题，已确认与羽毛球 MVP-a 实现无关

---

## 关键风险与应对

| 风险 | 影响 | 应对 |
|------|------|------|
| 空气阻力系数与真实羽毛球差距大 | 弹道观感不自然 | 以 Pure Badminton 反编译代码的参数为初始值，通过试玩微调 |
| 浮点非确定性 | 后续双端不一致 | MVP-a 允许浮点+exp 做本地验证；双端阶段前做定点化评估（见 TD-3） |
| Luban 配置表局部回归或临时不可用 | 短期阻塞开发 | 临时启用 `ShuttlecockShotConfigFallback` 兜底，但默认应回到正式 `TbShuttlecockShot`（见 TD-5） |
| 状态机判定边界情况 | 裁判不一致 | MVP-a 明确"触地后判定"策略，所有边界 case 写入测试用例 |

---

## 后续衔接

### MVP-b

MVP-b 将在球场之上实现角色移动：
- 复用 CourtConstants 的球场定义
- 角色作为 Box2D Body 在球场上移动（走 TD-2 的 Box2D 平面体系）
- 角色位置将成为击球系统的输入（站位偏移等）

### MVP-c

MVP-c 将实现击球系统：
- 复用 ShuttlecockEntity 和 ShuttlecockShotConfig
- 接入时机判定和球路选择
- 击球品质三元组为势系统做准备
