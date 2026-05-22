# AGENTS.md

请使用中文写提案和回答
这个文件为 Codex (Codex.ai/code) 提供指导，用于处理此代码库中的代码。

TEngine 基于 HybridCLR + YooAsset + UniTask + Luban 构建。

## 核心原则

1. **异步优先**：IO 操作用 `UniTask`，禁止同步加载/Coroutine
2. **模块访问**：通过 `GameModule.XXX` 访问，而非 `ModuleSystem.GetModule<T>()`
3. **资源必须释放**：`LoadAssetAsync` 对应 `UnloadAsset`，GameObject 用 `LoadGameObjectAsync`
4. **热更边界**：`GameScripts/Main` 不热更，`GameScripts/HotFix/` 全部热更
5. **事件解耦**：模块间用 `GameEvent`，UI 内部用 `AddUIEvent`

## 状态帧同步项目级规则

1. **任何客户端可预测的状态变化，必须是按帧命令，而不是带外即时消息**：包括移动、耗蓝、客户端主动触发的属性变化、后续技能前摇消耗等。消息必须携带 `FrameIndex`，服务端只能在对应 `Tick(frameIndex)` 中消费，禁止“收到就立刻生效”。
2. **客户端预测与服务端权威必须共享同一帧语义**：客户端若在本地预测了某个状态变化，服务端必须在同一逻辑帧上执行同一条命令；否则即使最终数值一致，也会制造伪 `MISMATCH/ROLLBACK`。
3. **不适合预测的结果不得伪装成可预测命令**：如受击掉血、服务器裁决命中、Buff tick、依赖碰撞/目标状态/随机数的结果，默认走权威快照，不得先在客户端随意改值。
4. **回滚重播必须覆盖完整状态变化**：凡进入预测链路的属性/数值变化，都必须进入快照、一致性检查、历史缓存与重播路径，不能只改当前显示值。
5. **一致性日志必须打印真实不一致域**：若 `MISMATCH` 由属性、Buff、黑板等非位置状态引起，日志必须显式打印对应 `pred/auth` 差异，禁止只打印位置后让人误判。

## 程序集分层

```
GameScripts/Main/       → 主包（不热更）
GameScripts/HotFix/
  ├── GameProto/        → Luban 配置代码
  └── GameLogic/        → 业务逻辑（GameApp.cs 入口）
```

**依赖**：GameLogic → TEngine.Runtime（单向）


## 文档参考

详细文档见 `skills/tengine-dev/references/`：

**核心开发**：
- [architecture.md](skills/tengine-dev/references/architecture.md) - 项目结构/启动流程
- [modules.md](skills/tengine-dev/references/modules.md) - 模块 API（Timer/Scene/Audio/Fsm）
- [ui-development.md](skills/tengine-dev/references/ui-development.md) - UI 开发
- [event-system.md](skills/tengine-dev/references/event-system.md) - 事件系统
- [resource-management.md](skills/tengine-dev/references/resource-management.md) - 资源加载
- [hotfix-development.md](skills/tengine-dev/references/hotfix-development.md) - 热更代码
- [luban-config.md](skills/tengine-dev/references/luban-config.md) - 配置表
- [conventions.md](skills/tengine-dev/references/conventions.md) - 代码规范
- [troubleshooting.md](skills/tengine-dev/references/troubleshooting.md) - 问题排查

**Unity Editor 自动化（unity-skills）**：
- [unity-mcp-guide.md](skills/tengine-dev/references/unity-mcp-guide.md) - MCP 工具索引
- [ui-prefab-builder.md](skills/tengine-dev/references/ui-prefab-builder.md) - UI Prefab 拼接
- [scene-gameobject.md](skills/tengine-dev/references/scene-gameobject.md) - 场景/GameObject 操作
- [script-asset-workflow.md](skills/tengine-dev/references/script-asset-workflow.md) - 脚本/资源管理
