# Fantasy 源码阅读索引

以**学习**为目的的阅读路线，不是排查手册。挑选标准是「概念密度高、代码量小」。

源码根目录：`UnityProject/Assets/GameScripts/HotFix/Fantasy.Unity/Runtime/Core/`

下文所有路径均相对该根目录，行数为实测值。

---

## 阅读顺序总览

| 阶段 | 模块 | 行数 | 核心收获 |
|------|------|------|----------|
| 一 | FTask | 1248 | `async/await` 编译器契约、池化 awaitable |
| 二 | Entitas | 5636（选读约 1500） | CoroutineLock、时间轮、await 事件 |
| 三 | KCP | 4440 | UDP 可靠传输、传输层接口抽象 |

阶段一、二是投入产出比最高的部分，约 2700 行。

### 配套讲解文档

- [阶段一：FTask](FTask阶段一讲解.html)
- [网络层（一）TCP —— 字节流与分包粘包](网络层一-TCP讲解.html)
- [网络层（二）UDP —— 拿掉保证之后](网络层二-UDP讲解.html)
- [网络层（三）KCP —— 把保证造回来](网络层三-KCP讲解.html)
- [网络层 · Channel 与 Session 的区别](../问答/网络层-Channel与Session.html) —— 卡点问答，归在「问答」区

网络层三篇按理解成本排序，不按项目实际使用排序。项目走 KCP，但先读 TCP 才能把「框架逻辑」和「协议逻辑」分开。

「Channel vs Session」是读三篇时最常见的卡点（接口和 Session 互相持有，分不清谁是谁），单开一页辨析，归在 [问答区](../问答/)。

---

## 阶段一：FTask —— 自定义 awaitable

全项目学习价值密度最高。绝大多数 C# 开发者只会用 `async/await`，读完这里你会写。

### 阅读顺序

- `FTask/Task/FVoid.cs`（28 行）：最简单的 awaitable，从这里入门
- `FTask/Task/FTask.cs`（262 行）：核心实现，四件套契约
- `FTask/Builder/AsyncFVoidMethodBuilder.cs`（61 行）：最简 builder
- `FTask/Builder/AsyncFTaskMethodBuilder.cs`（139 行）：带返回值的 builder
- `FTask/Task/FTaskCompleted.cs`（30 行）：已完成任务的零分配优化
- `FTask/FTask.Extension/FTask.Factory.cs`（175 行）：对象池创建入口
- `FTask/FTask.Extension/FTask.Tools.cs`（370 行）：WaitAll 等组合操作
- `FTask/FCancellationToken/FCancellationToken.cs`（131 行）：取消机制

### 关注点

- **编译器契约四件套**：`GetAwaiter()` / `IsCompleted` / `UnsafeOnCompleted()` / `GetResult()`。搞清楚状态机在什么时机调用哪一个
- **`[AsyncMethodBuilder]` 特性**：如何让 `async FTask` 方法通过编译，builder 怎么接管返回类型
- **`ICriticalNotifyCompletion`**：与 `INotifyCompletion` 的区别，为什么要跳过 ExecutionContext 捕获
- **池化的代价**：`GetResult()` 成功路径直接调 `Return()` 归还对象。理解这一点就明白为什么本项目 `GameClient.cs` 里要写 `FTask<bool>.Create(isPool: false)` —— 跨帧持有的 task 不能池化
- **`Coroutine()` 方法**：fire-and-forget 的实现手法，对比 UniTask 的 `Forget()`

### 项目内对照

- `GameScripts/HotFix/GameLogic/DataCenter/GameClient.cs`：`m_connectTask` 的创建与 `SetResult` 时机
- `GameScripts/HotFix/GameLogic/DataCenter/ClientConnectWatcher.cs:242`：FTask 与 UniTask 混用的现场

---

## 阶段二：Entitas —— 工程模式集

重点不是 ECS 本身，而是它顺带实现的几个通用模式，每个都可以独立读。

### 必读（小而精）

- `Entitas/Component/TimerComponent/TimeWheel/TimeWheel.cs`（133 行）：**时间轮**。133 行看懂一个经典数据结构，性价比最高
- `Entitas/Component/CoroutineLock/CoroutineLockComponent.cs`（99 行）：入口，先看这个
- `Entitas/Component/CoroutineLock/WaitCoroutineLock.cs`（145 行）：**异步互斥锁**的等待句柄
- `Entitas/Component/CoroutineLock/CoroutineLock.cs`（412 行）：完整实现，排队与释放
- `Entitas/Component/EventAwaiterComponent/EventAwaiterComponent.cs`（193 行）：**await 一个事件**，把回调地狱转成线性代码
- `Entitas/Component/EventComponent/EventComponent.cs`（231 行）：事件分发，对照 TEngine 的 `GameEvent` 看设计差异

### 框架世界观

- `Entitas/Entity.cs`（967 行）：ET 系框架的实体基类。父子关系、组件挂载、Id 分配
- `Entitas/Interface/System/`（各 30~60 行）：`IAwakeSystem` / `IUpdateSystem` / `IDestroySystem` 等生命周期接口。快速扫一遍即可建立全貌
- `Entitas/Component/EntityComponent/EntityComponent.cs`（548 行）：系统调度中枢，谁在驱动那些 `ISystem`

### 可跳过

`SeparateTableComponent`（755 行，分表存储）、`PoolGeneratorComponent`、`Collection/` 下两个 39 行的集合封装。

### 关注点

- CoroutineLock 解决什么问题：异步方法之间没有线程锁可用，如何保证同一实体的操作串行
- 时间轮相比最小堆定时器的取舍
- `TimerSchedulerNet.cs` 与 `TimerSchedulerNetUnity.cs`（399 / 372 行）：同一抽象的双平台实现，Unity 版为什么要单独写

---

## 阶段三：KCP —— 可靠 UDP

唯一有算法分量的模块。目标是理解「手游为什么能用 UDP 做到可靠有序」。

### 接口抽象（先读，252 行）

- `Network/Protocol/Interface/INetworkChannel.cs`（13 行）
- `Network/Protocol/Interface/ANetwork.cs`（148 行）
- `Network/Protocol/Interface/AClientNetwork.cs`（37 行）
- `Network/Protocol/Interface/ANetworkServerChannel.cs`（54 行）

### KCP 实现

- `Network/Protocol/KCP/KcpHeader.cs`（13 行）+ `KCPSettings.cs`（90 行）：协议头与参数，先建立概念
- `Network/Protocol/KCP/Base/Kcp.cs`（450 行）：C# 封装层
- `Network/Protocol/KCP/Base/c/kcp.cs`（1366 行）：**算法核心**，从 C 版 kcp 移植。ARQ 重传、拥塞窗口、RTO 计算都在这里
- `Network/Protocol/KCP/Client/KCPClientNetwork.cs`（745 行）：客户端实际走的路径，与本项目最相关

### 横向对照

`Network/Protocol/TCP/`（894 行）与 `Network/Protocol/WebSocket/`（1037 行）实现同一套接口。三种传输层对照读，是学习接口抽象设计的现成案例。

### 关注点

- 快速重传与超时重传的触发条件差异
- `nodelay` / `interval` / `resend` / `nc` 四个参数如何影响延迟与带宽
- 为什么服务端有 `ByArrayPool` 和 `ByPipe` 两个版本（574 / 622 行）

---

## 明确跳过的部分

| 模块 | 行数 | 原因 |
|------|------|------|
| `Serialize/` | 11116 | 大半是 MemoryPack 生成的 formatter。只值得花十分钟扫一眼生成产物长什么样，理解源码生成器的工作方式 |
| `DataStructure/` | 7215 | SkipTable、FrozenDictionary 等标准实现，有更好的教材 |
| `Network/Protocol/` 非 KCP 部分 | — | 模板化收发代码 |
| `Network/Addressable/` `Sphere/` | 1511 | 服务端多进程寻址，客户端不涉及 |
| `DataBase/` | 1630 | MongoDB 封装 |
| `Benchmark/` `IdFactory/World/` | — | 与学习目标无关 |

**例外**：`Network/Roaming/`（2508 行）是实体跨进程迁移，MMO 架构的核心难题。若后续往服务端或帧同步方向走，值得单独立项读。

---

## 补充：随时可读

- `Scene/Scene.cs`（792 行）：调度器与线程模型如何绑定，配合 `Scene/Scheduler/` 下几个极短的调度器实现（ThreadScheduler 12 行、MainScheduler 16 行）
- `Core/Platform/Unity/Entry`：本项目 HybridCLR 热更下的初始化时序，`GameClient.cs` 里 `assembly.EnsureLoaded()` 那段补丁的由来
