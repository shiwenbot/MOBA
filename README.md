# WeDoBest Pinball Tech Demo

当前分支：`feature/pinball-tech-demo`

本项目当前定位是一个以弹珠玩法为承载的技术展示工程。主线目标不是先做完整商业玩法，而是复用现有 TEngine / Fantasy / 状态帧同步基础，展示固定 Tick、服务端权威、客户端预测、快照回滚、确定性物理、调试可视化和自动化验收能力。

## 当前技术底座

- Unity + TEngine：HybridCLR 热更、YooAsset 资源、UI 和模块系统。
- Fantasy 服务端：认证、Gate、Battle Scene 和网络消息链路。
- 状态帧同步：30Hz 固定逻辑帧、服务端权威推进、客户端预测、快照校正、回滚重播、哈希一致性检查。
- Box2D / Box2DSharp：作为确定性物理展示核心。
- 无头测试：服务端 `--mode=test` 场景可作为开发基线。

## 分支方向

弹珠技术演示会优先围绕以下能力展开：

1. 弹珠实体、碰撞体、墙体、目标物和触发器。
2. 弹珠发射、反弹、得分、回合重置的状态同步。
3. 预测与权威快照在高速碰撞场景下的回滚表现。
4. 面向展示的调试 UI：帧号、RTT、预测领先量、回滚次数、哈希差异。
5. 最小可演示场景，而不是复杂角色、技能或完整关卡系统。

## 已移除内容

旧玩法原型已经从本分支移除，包括热更逻辑、共享仿真、Editor 菜单、测试场景、Luban 配置、生成代码、二进制配置和相关历史文档入口。

## 基线测试

```powershell
dotnet run --project GameServer\Server\Main\Main.csproj --framework net8.0 -- --mode=test --scenario=all
```

## 致谢

本项目基于 [TEngine](https://github.com/Alex-Rachel/TEngine) 开发，感谢原作者的贡献。
