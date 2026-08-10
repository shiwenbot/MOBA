# 真实客户端自动化验收

主入口：

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1
```

默认会执行四个场景：

- `two-client-join` - 两客户端加入房间
- `two-client-basic-move` - 两客户端基础移动
- `two-client-disconnect` - 断线测试
- `two-client-reconnect` - client B 主动断开并以同账号认领同一玩家

S9 起所有真实客户端场景都走 Authentication 真实注册/登录。运行前必须有 MongoDB `27017`，服务端会启动 Auth `20001` 与 Battle `20101`；client A/B 默认使用 `battle-auto-client-a` / `battle-auto-client-b` 两个不同账号。脚本会检查端口并拒绝相同账号。

S4 新增三个按需执行的弱网场景（不加入默认场景，避免普通回归主动注入丢包）：

- `two-client-weaknet-delay` - 双向 100ms 延迟 + 30ms 抖动
- `two-client-weaknet-uplink-loss` - 20% 上行永久丢包
- `two-client-weaknet-downlink-loss` - 20% 下行永久丢包，用于观察属性/Buff 基线错位及周期性全量恢复

## 当前状态

- 默认 bridge：`puerts`
- 已验证场景：
  - `two-client-join`
  - `two-client-basic-move`
  - `two-client-disconnect`
- `two-client-reconnect` 已完成代码与机械断言接入，Unity 双客户端结果待项目负责人执行
- 当前验证结论：
  - 双客户端均运行在 `puerts-runtime`
  - JS controller 已实际驱动输入与完成判定
- S4 弱网状态：代码、无头用例和场景入口已接入；Unity 实机场景按本次委托未执行，结果以 `弱网自动化测试-验收测试指南-20260805.md` 的负责人记录为准

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
├── two-client-disconnect.js.txt # 断线测试场景
├── two-client-reconnect.js.txt  # 断开、认领和全量恢复场景
└── weaknet-controller.js.txt    # 三个 S4 弱网场景共用
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
    // 返回: { completed, passed, reason, requestDisconnect? }
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

### 运行弱网场景

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -Scenario 'two-client-weaknet-delay' -Bridge 'puerts' -InteractiveEditor
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -Scenario 'two-client-weaknet-uplink-loss' -Bridge 'puerts' -InteractiveEditor
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -Scenario 'two-client-weaknet-downlink-loss' -Bridge 'puerts' -InteractiveEditor
```

场景默认参数可用 `-NetSimUplinkDelayMs`、`-NetSimDownlinkDelayMs`、`-NetSimUplinkJitterMs`、`-NetSimDownlinkJitterMs`、`-NetSimUplinkLossPercent`、`-NetSimDownlinkLossPercent` 和 `-NetSimSeed` 覆盖。最终 `report.json` 会回显生效配置、种子、上下行发送/丢弃计数、最大队列深度和溢出丢弃计数。

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

脚本会优先使用 `-DotnetExePath`，其次查找用户目录和 PATH 中带 .NET 9 SDK 的 `dotnet`。需要显式指定时：

```powershell
powershell -ExecutionPolicy Bypass -File D:\unity\Tencent\TEngine\Tools\AutomationAcceptance\Run-BattleAcceptance.ps1 -DotnetExePath 'C:\Users\shiwe\.dotnet\dotnet.exe'
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
