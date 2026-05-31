# 羽毛球 Luban 接入验证计划

## Context

这是《羽毛球核心玩法-MVP-a计划》的前置验证计划。羽毛球 MVP-a 需要 `ShuttlecockShotConfig` 作为双端共享配置，计划走 Luban 配置表。

本计划不实现任何羽毛球玩法，只回答一个问题：**当前项目能不能实际用 Luban，如果能，最小接入路径是什么？**

---

## 阶段状态

- **状态**：`验证通过`
- **前置依赖**：无
- **后续衔接**：羽毛球 MVP-a 已可直接接入正式 `TbShuttlecockShot`；Unity Editor 内 `ConfigSystem.Instance` 验证已通过

---

## 目标

1. 确认 Luban 工具链是否能在本地跑通
2. 用一个最小测试表跑通“定义 -> 生成 -> 双端读取 -> 一致性校验”链路
3. 给出 MVP-a 是否可直接依赖 Luban 的明确结论

---

## 现状盘点

### 已有基础设施

| 组件 | 路径 | 状态 |
|------|------|------|
| 客户端 Luban 运行时 | `UnityProject/Assets/GameScripts/HotFix/GameProto/LubanLib/` | 已存在 |
| 服务端 Luban 运行时 | `GameServer/Server/Entity/Luban/` | 已存在 |
| 配置定义 | `Configs/GameConfig/luban.conf` | 已存在 |
| 表定义与数据目录 | `Configs/GameConfig/Datas/` | 已存在 |
| 自定义模板 | `Configs/GameConfig/CustomTemplate/` | 已存在 |
| 客户端转表脚本 | `Configs/GameConfig/gen_code_bin_to_project_lazyload.bat` | 已修正首次生成时的交互卡顿 |
| 服务端转表脚本 | `Configs/GameConfig/gen_code_bin_to_server.bat` | 已修正首次生成时的交互卡顿 |
| Unity 菜单集成 | `UnityProject/Assets/TEngine/Editor/LubanTools/LubanTools.cs` | 已存在 |

### 本次验证新增/补齐

| 组件 | 路径 | 状态 |
|------|------|------|
| Luban CLI | `Tools/Luban/Luban.dll` | 已通过源码构建补齐 |
| Luban 源码 | `Tools/luban-src/` | 已拉取，可用于后续重编 |
| 最小测试表 | `Configs/GameConfig/Datas/shuttlecock_shot_test.xlsx` | 已新增 |
| 表定义注册 | `Configs/GameConfig/Datas/__tables__.xlsx` | 已新增 `badminton.TbShuttlecockShotTest` |
| 客户端生成代码 | `UnityProject/Assets/GameScripts/HotFix/GameProto/GameConfig/badminton/` | 已生成 |
| 服务端生成代码 | `GameServer/Server/Entity/Generate/GameConfig/badminton/` | 已生成 |
| 客户端 bytes | `UnityProject/Assets/AssetRaw/Configs/bytes/` | 已生成 |
| 服务端 bytes | `GameServer/GameConfig/Binary/` | 已生成 |
| 离线验证工具 | `Tools/LubanValidation/` | 已新增 `ClientRead` / `ServerRead` |
| 正式球路表 | `Configs/GameConfig/Datas/shuttlecock_shot.xlsx` | 已新增 6 种正式球路 |
| Unity Editor 验证入口 | `UnityProject/Assets/Editor/Badminton/ShuttlecockConfigValidationEditor.cs` | 已新增，可 batchmode 执行 |

### 当前业务配置现状

| 配置类型 | 当前方案 | 说明 |
|---------|---------|------|
| Buff 配置 | `DefaultBuffConfigProvider` | 纯 C# 硬编码，不走 Luban |
| 技能配置 | `RuntimeSkillGraph` | 走 JSON，不走 Luban |
| 网络协议 | Fantasy 生成 | 不走 Luban |

### 结论

项目原先处于“Luban 基础设施已搭好，但没有跑通过完整生成链路”的状态。本次验证已经完成 CLI、双端生成、双端读取和二进制一致性校验，说明 **Luban 本身可用**。

`AssetRaw/Configs/bytes/` 已确认落在 YooAsset `Assets/AssetRaw/Configs` 的 `CollectAll` 收集范围内，且 `ConfigSystem.Instance` 已在 Unity batchmode 下通过正式表和测试表读取验证。

---

## 验证样例设计

### 测试表：`badminton.TbShuttlecockShotTest`

字段如下：

| 字段名 | 类型 | 说明 |
|--------|------|------|
| Id | int | 主键 |
| Name | string | 球路名称 |
| HorizontalSpeed | float | 水平初速度 |
| LaunchAngle | float | 发射仰角 |
| Drag | float | 阻力系数 |
| Description | string | 描述 |

测试数据：

| Id | Name | HorizontalSpeed | LaunchAngle | Drag | Description |
|----|------|----------------|-------------|------|-------------|
| 1 | TestClear | 18.0 | 55.0 | 0.92 | 高远球测试参数 |
| 2 | TestSmash | 22.0 | 15.0 | 0.92 | 扣杀测试参数 |
| 3 | TestDrop | 8.0 | 35.0 | 0.92 | 吊球测试参数 |

---

## 实施结果

### Step 1：获取 Luban CLI

- 结果：通过
- 实际动作：拉取 `https://github.com/focus-creative-games/luban` 到 `Tools/luban-src/`，编译 `src/Luban/Luban.csproj`
- 产物：`Tools/Luban/Luban.dll`

### Step 2：验证现有 `item` 表生成

- 结果：通过
- 说明：现有 `item` 表双端都能正常生成
- 注意：`item` 表客户端/服务端字段分组不同，不适合作为“双端字节级一致性”样例

### Step 3：检查生成目录与编译

- 结果：通过
- 客户端/服务端生成目录均正常落地
- `GameServer/Server/Entity/Entity.csproj` 构建通过
- `UnityProject/GameLogic.csproj` 构建通过
- 为命令行构建闭环，补充了 `UnityProject/GameProto.csproj` 中对新生成 `badminton/*.cs` 的显式 `Compile Include`
- 注意：该 `csproj` 可能被 Unity/Rider 重新生成覆盖，但不影响 Unity 内 asmdef 实际编译逻辑

### Step 4：新增最小测试表定义

- 结果：通过
- 已新增 `shuttlecock_shot_test.xlsx`
- 已在 `__tables__.xlsx` 注册 `badminton.TbShuttlecockShotTest`
- 双端均生成：
  - `ShuttlecockShotTest.cs`
  - `TbShuttlecockShotTest.cs`
  - `badminton_tbshuttlecockshottest.bytes`

### Step 5：客户端读取验证

- 结果：通过
- 已通过 `Tools/LubanValidation/ClientRead` 离线读取客户端生成 bytes
- 读取结果正确：`Id=1, Name=TestClear, HorizontalSpeed=18, LaunchAngle=55, Drag=0.92`
- 已新增 `ShuttlecockConfigValidationEditor.ValidateFromCommandLine`
- 已通过 Unity `-batchmode -executeMethod` 实际执行 `ConfigSystem.Instance.Tables.TbShuttlecockShotTest` 和正式 `TbShuttlecockShot`

### Step 6：服务端读取验证

- 结果：通过
- 已通过 `Tools/LubanValidation/ServerRead` 读取
- 读取结果正确：`Id=1, Name=TestClear, HorizontalSpeed=18, LaunchAngle=55, Drag=0.92`
- 同时修复了 `ServerConfigSystem.GenerateConfigPath`，使其可向上搜索仓库根并命中 `GameServer/GameConfig/Binary/`

### Step 7：双端一致性验证

- 结果：通过
- `HorizontalSpeed` / `LaunchAngle` / `Drag` 三个 float 字段的 bit 表示完全一致
- 客户端与服务端 `badminton_tbshuttlecockshottest.bytes` 的 SHA256 完全一致

### Step 8：结论记录

- 结果：通过
- 本文档已回填

---

## 验证结论

### 结果摘要

| 项 | 结果 | 备注 |
|----|------|------|
| Luban CLI 可用 | 通过 | `Tools/Luban/Luban.dll` 已生成 |
| 双端代码生成 | 通过 | 客户端/服务端均生成 `badminton` 测试表代码 |
| 双端二进制生成 | 通过 | bytes 产物齐全 |
| 服务端读表 | 通过 | 读取正确 |
| 客户端读表 | 通过 | 离线读取与 Unity Editor `ConfigSystem.Instance` 实读均通过 |
| 双端一致性 | 通过 | 测试表与正式表的 float bit / SHA256 均一致 |

### 最终结论

- **MVP-a 配置选型**：可以直接采用 Luban 作为羽毛球配置源
- **主阻塞点**：无
- **剩余待补验项**：无主阻塞；后续只需在玩法接入阶段按需补具体球路调参
- **Fallback 策略**：`ShuttlecockShotConfigFallback` 仅保留为开发期临时兜底，不建议继续作为主方案扩张

### 建议

1. 继续沿用 `c,s` 双端共享分组
2. 正式玩法层直接使用 `TbShuttlecockShot` + `ShuttlecockShotConfigProvider`
3. 保留 `TbShuttlecockShotTest` 作为 Luban 自测表
4. 如果团队不准备长期维护 `Tools/luban-src/`，后续可考虑仅保留 `Tools/Luban/Luban.dll` 与最小构建说明

---

## 与羽毛球 MVP-a 的衔接

### 可直接进入的下一步

1. 在羽毛球玩法代码里直接消费 `TbShuttlecockShot`
2. 基于现有 6 种正式球路参数开始弹道调参
3. 视需要扩展更多字段，但保持双端共享表结构

### 暂不建议现在做的事

- 不必先把 Buff/Skill 全面迁入 Luban
- 不必为了羽毛球 MVP-a 先做大规模配置体系重构
- 不必因为 Unity Runtime 补验未做，就回退整个 Luban 方案
