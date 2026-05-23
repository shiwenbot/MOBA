# CLAUDE.md

请使用中文写提案和回答
这个文件为 Claude Code (claude.ai/code) 提供指导，用于处理此代码库中的代码。

TEngine 基于 HybridCLR + YooAsset + UniTask + Luban 构建。

## 核心原则

1. **异步优先**：IO 操作用 `UniTask`，禁止同步加载/Coroutine
2. **模块访问**：通过 `GameModule.XXX` 访问，而非 `ModuleSystem.GetModule<T>()`
3. **资源必须释放**：`LoadAssetAsync` 对应 `UnloadAsset`，GameObject 用 `LoadGameObjectAsync`
4. **热更边界**：`GameScripts/Main` 不热更，`GameScripts/HotFix/` 全部热更
5. **事件解耦**：模块间用 `GameEvent`，UI 内部用 `AddUIEvent`

## 程序集分层

```
GameScripts/Main/       → 主包（不热更）
GameScripts/HotFix/
  ├── GameProto/        → Luban 配置代码
  └── GameLogic/        → 业务逻辑（GameApp.cs 入口）
```

**依赖**：GameLogic → TEngine.Runtime（单向）

## 计划编写规范

编写 `Plan/` 下的实现计划时，必须遵循**实现与验收分离**原则：

### 1. 计划必须包含的验收交付物

每个实现计划除代码实现步骤外，必须同时产出：

- **测试场景代码**：作为实现步骤的一部分写好（无头自测场景 + 服务端自动化场景配置 + Puerts 脚本）
- **验收指南文件**：放在 `Tools/AutomationAcceptance/` 下，命名格式 `模块名-验收测试指南-日期.md`

### 2. 验收指南必须包含

1. **前置条件**：Unity 版本、端口、进程依赖
2. **执行命令**：可直接复制的完整 CLI 命令（无头 + 真实客户端）
3. **预期通过标准**：退出码、report.json 关键字段、日志关键字
4. **失败排查顺序**：按优先级列出排查文件和关注点
5. **相关实现文件**：方便验收 agent 快速定位代码

### 3. Agent 职责边界

| 角色 | 职责 | 不做 |
|------|------|------|
| 实现 agent | 写代码 + 写测试场景代码 + 写验收指南 | 不跑 Unity Editor 测试 |
| 验收 agent | 读验收指南 → 执行 CLI 命令 → 报告 PASS/FAIL | 不改代码 |

### 4. 参考范例

- 验收指南：`Tools/AutomationAcceptance/战斗Buff系统-Buff验收测试指南-20260523.md`
- 自动化方案：`Plan/自动化验收方案-测试效率提升计划.md`


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
