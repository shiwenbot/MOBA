# 羽毛球代码归档

归档日期：`2026-07-26`
归档原因：项目转向"单一可移动圆球"的状态帧同步演示，羽毛球玩法暂不推进。

这些代码**功能完整且通过过测试**，不是废弃代码。移出 `Assets/` 只是让 Unity 停止编译它们。

## 归档内容

| 归档目录 | 原路径 |
|---------|--------|
| `GameShared_Badminton/` | `UnityProject/Assets/GameScripts/HotFix/GameShared/Badminton/` |
| `GameShared_Tests_Badminton/` | `UnityProject/Assets/GameScripts/HotFix/GameShared/Tests/Badminton/` |
| `GameLogic_Badminton/` | `UnityProject/Assets/GameScripts/HotFix/GameLogic/Badminton/` |
| `Editor_Badminton/` | `UnityProject/Assets/Editor/Badminton/` |
| `BadmintonPhysicsTestScene.unity` | `UnityProject/Assets/Scenes/Test/` |
| `GameServer_Entity_Badminton/` | `GameServer/Server/Entity/Badminton/` |

`.meta` 文件一并保留，恢复后 Unity 的资源 GUID 不会丢。

## 归档时的完成度

`GameShared_Badminton/ShuttlecockEntity.cs` 已经具备接入帧同步的全部条件：

- 完全定点化（`Fixed64`，零 float）
- 实现 `ITickable` + `ISnapshotable<ShuttlecockSnapshot>`
- 具备 `RollBack()`、`CheckConsistency()`、`ShuttlecockStateHasher`
- 自带 `DeterminismSelfTest` / `RollbackSelfTest`

**当时唯一缺的**是接入网络：`BattleWorldSnapshot` 没有球体字段，
服务端 `BattleLogic.cs` 未 Tick 球体，协议未扩展。

物理模型含重力、空气阻力、马格努斯效应。

## 故意没有归档的部分

Luban 生成的配置代码**留在原地**，因为 `Tables.cs`（Luban 自动生成）硬引用了它们，
移走会直接编译失败：

- `UnityProject/Assets/GameScripts/HotFix/GameProto/GameConfig/badminton/`
- `GameServer/Server/Entity/Generate/GameConfig/badminton/`
- `UnityProject/Assets/AssetRaw/Configs/bytes/badminton_tbshuttlecockshot*.bytes`
- `GameServer/GameConfig/Binary/badminton_tbshuttlecockshot*.bytes`
- `Configs/GameConfig/Datas/shuttlecock_shot*.xlsx`（数据源，重新生成时需要）

这些是惰性加载的，不引用就没有运行时开销。

要彻底清除配置表，需要手动编辑 `Configs/GameConfig/Datas/__tables__.xlsx`
删掉 `shuttlecock_shot` 两行，再重跑 `Configs/GameConfig/gen_code_bin_to_project.bat`
和 `gen_code_bin_to_server.bat`。xlsx 是二进制格式，需要人工操作。

## 归档时顺带删掉的测试

`GameShared/Tests/FixedPoint/FixedMathSharpTests.cs` 中的
`CourtBounds_EvaluatesFixedInputs` 已删除——它依赖 `CourtConstants`，
属于羽毛球场地边界校验，不是定点数数学测试。同文件的
`using GameShared.Badminton;` 一并移除。该文件其余定点数测试保持不变。

若恢复羽毛球，可从 git 历史取回这个测试。

## 如何恢复

把上表的目录 `git mv` 回原路径（连 `.meta` 一起），Unity 会自动重新编译。
`GameShared/Badminton/` 放回原位后会自动重新进入服务端编译——
`GameServer/Server/Entity/Entity.csproj` 用的是
`GameShared/**/*.cs` 通配符，不需要改 csproj。

## 相关文档

归档前的历史计划文档（保留在 `Plan/` 下，未改动）：

- `Plan/状态帧同步/定点数全局迁移计划.html`
- `Plan/状态帧同步/定点数库实现计划.html`
- `Plan/项目概览/项目进度概览-2026-06-17.html`
