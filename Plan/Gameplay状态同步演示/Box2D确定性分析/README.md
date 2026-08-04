# Box2D 确定性分析：pair 顺序与 WarmStarting 隐患评估

## Context

这份报告回答一个问题：**学习 Box2D 源码时发现的「确定性隐患」，对本项目的帧同步/回滚架构到底成不成立？该不该处理？**

背景：Box2D 教材（`Books/学习笔记/教材/Box2D讲解.html` 第四章）提到两条确定性问题：
1. **pair 顺序非规范化** → ContactList 头插 → 求解顺序 → 浮点累加顺序 → 数值发散
2. **WarmStarting 跨帧状态** → 第 N 帧依赖第 N-1 帧累积冲量，快照/恢复丢失该状态

本报告基于对当前代码的扫描，给出**两条隐患在现状下的真实严重程度**，以及**处理建议**。

---

## 结论先行（TL;DR）

| 隐患 | 现状严重程度 | 是否需处理 |
|------|------------|-----------|
| pair 顺序非规范化 | **潜伏**（目前被 `GroupIndex=-1` 屏蔽，不触发） | 不立即处理，但需留记录，一旦打开 native 碰撞必触发 |
| WarmStarting 跨帧状态 | **真实存在**（代码未显式处理，依赖 DLL 默认=开启） | **建议立即处理**：加一行 `_world.WarmStarting = false` |

---

## 现状扫描结果

### 1. 物理层只有一个封装：`FrameSyncPhysicsWorld.cs`

- 路径：`Assets/GameScripts/HotFix/GameShared/FrameSync/Battle/FrameSyncPhysicsWorld.cs`（本报告成文时 636 行，截至 2026-08-03 为 687 行；下文行号均为成文时的旧行号）
- World 配置：**零重力**（`Vector2.Zero`，俯视角）
- 唯一的 body：**玩家圆形**（`CircleShape`，Radius=0.45，DynamicBody + Bullet + FixedRotation + 不睡眠）
- **没有墙体 / 障碍物 / 多边形 / 关节**（仓库内未找到任何相关创建代码）
- `Step(dt, VelocityIterations=12, PositionIterations=8)`

### 2. 关键：玩家间 native 碰撞被完全关闭

`FrameSyncPhysicsWorld.cs:19` + `:100-103`：

```csharp
private const short PlayerNoPushGroupIndex = -1;
...
Filter = new Filter { GroupIndex = PlayerNoPushGroupIndex }  // = -1
```

回忆 Box2D 语义：`GroupIndex` 相同且为负 → **无条件不碰撞**。所有玩家 body 的 `GroupIndex` 都是 -1，所以**玩家之间 Box2D 根本不产生 Contact**。

玩家之间的挤位是**手写**的 `ResolvePlayerOccupancy`（`:281-351`）：4 次迭代、O(n²) 两两检查圆心距、直接 `SetTransform` 硬掰位置。**这一步绕开了 Box2D 的求解器。**

### 3. WarmStarting 未被显式处理（真实漏洞）

全树 grep `WarmStarting|warmStart|SetWarmStart` **无任何命中**。代码既没开也没关，依赖 DLL 默认行为。Box2D 默认 **WarmStarting = true**。

恢复流程 `RestoreSnapshot`（`:200-229`）：

```csharp
ClearBodies();                          // 销毁所有 body
for (...) {
    EnsureBody(...);                    // 重建 body
    body.SetTransform(...);             // 写位置
    SetBodyLinearVelocity(...);         // 写速度
    body.IsAwake = ...;
}
// ← 没有任何地方恢复 Contact 的累积冲量（warm starting 的跨帧状态）
```

快照存了 body 的位置/速度/IsAwake，但**没存、也存不了** Contact 的累积冲量（internal 字段，0.6 无公开 API）。

### 4. 确定性/回放需求确实存在

- 服务端权威 + 客户端预测 + 权威快照对账（`CheckConsistency`）+ 不一致回滚重放（`ReconcileAuthoritativeSnapshot`→`AdvancePredictionTo`）
- `DeterminismRules` 逐位固定步校验（30Hz）
- `StateHasher` 哈希比对
- 静态扫描工具 `FrameSyncValidationRunner` 禁用非确定性 API

**所以这是一套真的对 bit-exact 有要求的系统。** WarmStarting 的隐藏状态是个真实威胁。

---

## 隐患一：pair 顺序非规范化 —— 目前潜伏，打开 native 碰撞必触发

### 为什么目前不触发

教材那段警告的前提是：**Box2D 的接触求解器真的在跑、真的在累加冲量**。

但本项目 `GroupIndex=-1` 关闭了玩家间碰撞 → 玩家之间**不产生 Contact** → 求解器对玩家间空转 → pair 顺序→ContactList→求解累加 这条链路**断在第一步**。

项目当前的确定性由 `ResolvePlayerOccupancy` 自己保证：它遍历的是 `_sortedBodyIdsBuffer`（排过序），顺序可控，不受 Box2D 树形状影响。

### 什么时候会触发

**一旦给玩家或场景加上 native 碰撞**，比如：
- 玩家去掉 `GroupIndex=-1`，改用 mask 让 Box2D 处理玩家间推挤
- 加入墙体/障碍物（Dynamic ↔ Static），让 Box2D 处理玩家撞墙

此时 Contact 会真正产生，求解器开始累加冲量，**pair 顺序隐患立刻复活**：

```
物体创建/移动顺序不同
  → 树插入顺序不同 → 树形状不同（AVL 平衡后 Child1/Child2 分配可能变）
     → 查树遍历顺序不同（LIFO 先访问 Child2）
        → pair 产出顺序不同
           → ContactList 头插后排列顺序不同（AddFirst，:191）
              → 求解器累加顺序不同
                 → 浮点误差累积不同（浮点加法不满足交换律）
                    → 数值发散
```

### 处理建议

**当前不处理**（被 GroupIndex 屏蔽，无影响）。但**留一条记录**：未来若打开 native 碰撞，必须同时处理 pair 顺序问题——可行方向是放弃依赖 Box2D 的求解顺序，改用排序后的 contact 列表驱动求解（或干脆像现在一样手写碰撞解算，完全绕开 Box2D 求解器）。

---

## 隐患二：WarmStarting 跨帧状态 —— 真实存在，建议立即处理

### 问题机制

WarmStarting 让第 N 帧的求解以「第 N-1 帧的累积冲量」为初值。这个累积冲量存在 `Contact.Manifold` 上，是 **internal 字段，0.6 没有公开 API 读写**。

恢复时 `RestoreSnapshot` 重建了 body 的位置/速度，但 **Contact 是全新的、累积冲量=0**。所以读档/回滚后第一帧：
- 物理状态对了（位置速度）
- 但 warm starting 拿不到上一帧冲量 → 第一帧的解和原始轨迹**不一致**

### 为什么现在影响小，但仍建议处理

当前玩家间不碰，所以 warm starting 暂时没有「对手」——没有累积冲量值得复用。但：
1. **只要将来加一面墙**（球撞墙反弹），warm starting 就会介入球-墙的接触累积冲量
2. 回滚/读档后第一帧，这个冲量丢失 → 球的反弹轨迹会抖一下
3. 在一套已经做了 30Hz 逐位校验 + 哈希对账的系统里，这种「恢复后第一帧不一致」是**确定性 bug**，很难定位

成本几乎为零，收益是彻底消除一整类「跨帧隐藏状态」问题。

### 处理方案对比

| 方案 | 成本 | 收益 | 推荐度 |
|------|------|------|--------|
| **A：显式关掉 WarmStarting** | 极低（加一行代码） | 彻底消除跨帧隐藏状态 | ⭐⭐⭐ 推荐 |
| B：恢复时反射写回累积冲量 | 高（反射读写 internal 字段 + 快照多存字段） | 保留稳态堆叠精度 | 当前用不上 |
| C：改 DLL 源码重新编译 | 高（要拿到源码、重新编译） | 同 B | 当前用不上 |

**方案 A 细节**：

```csharp
// FrameSyncPhysicsWorld 构造函数（:39-45 附近）
_world = new World(in gravity);
_world.SetContactListener(_contactListener);
_world.WarmStarting = false;   // ← 加这一行，确定性优先
```

**代价**：关掉 warm starting 后，稳态堆叠场景可能略微下沉（每帧从 0 爬冲量）。但本项目是**零重力 + 俯视角 + 圆形 body + 无堆叠**，这个代价**不存在**。`VelocityIterations=12` 足够在单帧内收敛。

---

## 附：一个值得长期思考的问题

扫描后发现一个观察，供后续评估（**不急于动**）：

当前物理世界里，Box2D 实际承担的只有：
- 圆形 body 的速度积分（`c += h*v`，一次乘加）
- 接触判定信号（给快照用）

真正的「物理推挤」是手写的 `ResolvePlayerOccupancy`。也就是说，**Box2D 的求解器在本项目里几乎是空转的**——`GroupIndex=-1` 让 Contact 不产生，速度/位置求解器全程没活干。

如果未来弹球场景也只是「球+墙」的简单碰撞，可能根本不需要 Box2D 的求解器——一个手写的圆-墙反射 + occupancy，确定性和性能都比引入 Box2D 更可控。

教材第零章末尾那句「也有可能结论是『这个场景用不着物理引擎』，那同样是有效结论」——指的可能就是现在的情况。这不是现在要做的决定，但值得记住。

---

## 落地清单

> **状态更新（2026-08-03）**：三项均已结案，详见下表「现状」列。本清单保留原文用于追溯。

| # | 动作 | 文件 | 原优先级 | 现状（2026-08-03） |
|---|------|------|---------|-------------------|
| 1 | 加 `_world.WarmStarting = false` | `FrameSyncPhysicsWorld.cs:39-45` 附近 | 高（立即） | **已完成**，commit `0bb9a468`（实际落在 `:47`）。但因全场无 Contact 而**暂未实际生效**；S2 拿掉 Box2D 后本项自然消失 |
| 2 | pair 顺序隐患留记录（未来打开 native 碰撞时处理） | 本报告 + 笔记 | 低（备忘） | **已失效**。S2 决定拿掉 Box2D、改手写定点，不会再打开 native 碰撞，该隐患不复存在 |
| 3 | 长期评估「是否仍需 Box2D 求解器」 | 不动，记在心里 | 仅备忘 | **已结案**：不需要。见 [物理层与玩法形态-讨论记录-20260803.md](../物理层与玩法形态-讨论记录-20260803.md) 决策 2 与 [S2 实现计划](../S2-物理层重写-手写定点物理-实现计划.md) |

---

## 参考资料

- 教材：`Books/学习笔记/教材/Box2D讲解.html` 第四章（碰撞检测）、第五章（warm starting）
- 卡点笔记：`Books/学习笔记/问答/Box2D卡点.html`（pair 产生去重 + 帧同步隐患）
- 源码：`D:\Unity\Box2DSharp-src`（核对 GroupIndex / AddFirst / Manifold internal 等结论）
- 项目代码：`FrameSyncPhysicsWorld.cs`、`BattleSimulation.cs`、`DeterminismRules.cs`
