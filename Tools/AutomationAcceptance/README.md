# 真实客户端自动化验收

主入口：

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1
```

默认会执行三个场景：

- `two-client-join` - 两客户端加入房间
- `two-client-basic-move` - 两客户端基础移动
- `two-client-disconnect` - 断线测试

## 当前状态

- 默认 bridge：`puerts`
- 已验证场景：
  - `two-client-join`
  - `two-client-basic-move`
  - `two-client-disconnect`
- 当前验证结论：
  - 双客户端均运行在 `puerts-runtime`
  - JS controller 已实际驱动输入与完成判定

## 架构说明

### 自动化框架

- **服务端**：真实 `dotnet run` 进程
- **客户端**：原工程 + `ParrelSync` clone 两个 Unity batchmode Editor 实例
- **运行时自动化**：`BattleAutomationController`
- **Editor 启动入口**：`RealClientAutomationEditor.RunAutomationClient`
- **结构化结果**：每个客户端输出一份 JSON 报告和事件日志

### Controller Bridge

框架支持两种 controller 实现：

| Bridge | 说明 | 脚本位置 |
|--------|------|----------|
| `puerts` | 使用 JavaScript 编写 controller（默认，已验证） | `Assets/StreamingAssets/BattleAutomation/Puerts/*.js.txt` |
| `builtin` | 使用 C# 内置实现 | `BattleAutomationRuntime.cs` |

### Puerts Controller 脚本

每个场景对应一个 JavaScript controller 脚本：

```
Assets/StreamingAssets/BattleAutomation/Puerts/
├── two-client-join.js.txt       # 加入房间场景
├── two-client-basic-move.js.txt # 基础移动场景
└── two-client-disconnect.js.txt # 断线测试场景
```

脚本需要实现以下接口：

```javascript
module.exports = {
  initialize(configJson) {
    // 初始化，接收配置
    // config: { clientId, scenario, battleServerAddress, battleServerPort, timeoutSeconds, ... }
  },

  tryGetInput(payloadJson) {
    // 返回输入指令
    // payload: { frameIndex }
    // 返回: { hasInput, dx, dy }
  },

  evaluate(snapshotJson) {
    // 评估完成状态
    // snapshot: { joined, localFrame, activePlayerCount, players, ... }
    // 返回: { completed, passed, reason }
  },

  dispose() {
    // 清理资源
  }
};
```

## 使用方法

### 运行所有场景

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1
```

### 运行特定场景

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -Scenario 'two-client-join'
```

### 使用 builtin bridge

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -Bridge 'builtin'
```

### 强制使用 puerts bridge

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -Bridge 'puerts'
```

### 使用自定义 controller 脚本

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -ControllerScriptPath 'C:\path\to\custom-controller.js.txt'
```

## 高级选项

若本机 Unity 路径不是默认的 `C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe`，请显式传：

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -UnityExePath 'C:\Path\To\Unity.exe'
```

如果本机 Unity 在 batchmode 下出现 `LicensingClient` 或退出码 `199`，可以切到交互式 Editor 自动运行：

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -InteractiveEditor
```

跳过编译步骤（适合频繁测试时）：

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -NoBuild
```

## 注意事项

- 当 `UnityProject/LocalPackages` 发生变更后，建议重建一次 `ParrelSync` clone，避免 clone 工程仍引用旧包状态。
- 服务端启动口径固定为 `Release + pid 2001`，对应 Battle 端口 `20101`。
- `InteractiveEditor` 模式更适合当前机器环境，可规避部分 Unity Licensing / batchmode 问题。
