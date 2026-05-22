# CLI游戏逻辑测试应用程序 - 实施计划（修订版）

- 修订日期：`2026-05-19`
- 当前判断：`推荐先做测试 MVP，再用它护航状态帧同步主线重构，完整独立 CLI 产品化后置`
- 当前执行状态：`测试入口 MVP 已落地；独立 CLI 产品化与后续高阶场景仍待继续`
- 关联文档：
  - [状态帧同步-架构重构计划.md](/D:/unity/Tencent/TEngine/Plan/状态帧同步/状态帧同步-架构重构计划.md:1)
  - [状态帧同步-v0.3-test计划.md](/D:/unity/Tencent/TEngine/Plan/状态帧同步/状态帧同步-v0.3-test计划.md:1)
  - [状态帧同步-v0.3b计划.md](/D:/unity/Tencent/TEngine/Plan/状态帧同步/状态帧同步-v0.3b计划.md:1)

## 当前决策（2026-05-16）

- 本计划当前仍保留为推荐工程化支线。
- `Milestone 1` 已于 `2026-05-19` 落地，`Milestone 0 / 2 / 3` 仍待继续推进。
- 需求方已明确接受先不做本阶段测试增强，直接开启 `v0.3b` 主线改造。
- `2026-05-16` 已验证现有命令可用：
  - `--mode=test` → `PASS`
  - `--mode=validate --duration-seconds 30 --update-hz 60` → `PASS`
- 本决定只代表“允许先推进”，不代表测试工具产品层任务已完成；后续仍需补回。

## 背景

当前项目已经具备两块可直接复用的基础：

- 现有无头测试入口：`dotnet run --project 'GameServer/Server/Main/Main.csproj' -- --mode=test`
- 现有帧同步验收工具：`GameServer/Tools/FrameSyncMvpAValidator`

也就是说，项目并不是“完全没有 CLI 测试能力”，而是：

1. 已经有一条可用但偏粗糙的无头测试链路。
2. 还没有一套足够适合 agent 持续调用、参数化、输出结构化报告的测试产品层。
3. 若此时直接投入完整 `GameLogicTester` 大工程，后续 `v0.3b` / `v0.3c` 主线改动会反复推翻测试工具实现。

因此，本计划的重点不再是“先从零搭一个完整 CLI 工具”，而是先把已有无头能力收敛成一个高频可用的测试 MVP。

## 核心结论

### 1. 先做“测试 MVP”，而不是先做“完整 CLI 产品”

测试 MVP 的目标是：

- agent 改完逻辑后能立即跑无头回归
- 不依赖 Unity Editor
- 支持稳定退出码与结构化报告
- 可以覆盖 `BattleLogic` / `FrameSync` / `v0.3b` / `v0.3c` 的关键路径

这一层一旦完成，后续大多数纯逻辑改动都能直接被 agent 验证。

### 2. 测试 MVP 应建立在现有 `v0.3-test` 基础上，而不是另起炉灶

当前仓库已经有：

- `BattleLogic`
- `SimulatedClient`
- `TestRunner`
- `--mode=test`
- `FrameSyncMvpAValidator`

所以第一阶段最合理的动作是“收敛与增强”，不是“重写与迁移”。

### 3. 完整独立 CLI 工具仍然有价值，但应放在主线稳定后

独立 `GameServer/Tools/GameLogicTester/` 的价值在于：

- 命令行接口更清晰
- 可以沉淀成团队工具
- 更适合 CI/CD、长期维护和扩展

但它不是 `v0.3b` 当前落地的前置条件。真正的前置条件是：先有稳定可跑的测试护栏。

### 4. 第一阶段不建议立即投入技能测试、网络模拟、场景配置系统

以下内容都重要，但都不适合现在抢跑：

- `SkillScenario`
- `NetworkScenario`
- JSON 场景配置系统
- 长时间性能测试
- 完整报告历史对比平台

它们都应建立在“主线帧号语义稳定、回滚路径稳定、测试入口稳定”之后再做。

## 目标分层

### 近期目标：测试 MVP

- agent 可通过一条命令运行关键逻辑回归
- 支持选择场景、帧数、客户端数、输出格式
- 支持 Battle/TestHarness 与 FrameSync validator 两类入口
- 输出稳定的 PASS / FAIL、退出码、Markdown/JSON 报告

### 中期目标：作为主线重构护栏

- 为 `v0.3b` 补齐全局帧号相关用例
- 为 `v0.3c` 补齐回滚/重播相关用例
- 让每次主线改动都能在无头环境下立即回归

### 远期目标：独立 CLI 产品化

- 形成 `GameServer/Tools/GameLogicTester/`
- 统一 `test` / `validate` / `run-scenario` 命令接口
- 支持更多场景与 CI/CD 集成

## 推荐架构

### Layer 1：现有基础层

直接复用当前仓库已有能力：

- `GameServer/Server/Main/Program.cs`
- `GameServer/Server/Entity/TestHarness/TestRunner.cs`
- `GameServer/Server/Entity/TestHarness/SimulatedClient.cs`
- `GameServer/Tools/FrameSyncMvpAValidator`

这一层已经证明“无头测试链路是可行的”。

### Layer 2：测试 MVP 层（当前优先级最高）

在不大规模搬迁目录的前提下，为现有入口补上：

- 参数化场景选择
- 报告输出
- validator 路由
- 稳定退出码
- 适合 agent 解析的输出格式

推荐形式：

```text
GameServer/Server/Main
  --mode=test
  --mode=validate
  --scenario determinism|consistency|convergence|player-leave|join-align|global-frame
  --frames 300
  --clients 2
  --output console|markdown|json
  --report <path>
```

### Layer 3：独立 CLI 产品层（可选后置）

当主线稳定后，再将 Layer 2 产品化为独立工具：

```text
GameServer/Tools/GameLogicTester/
├── GameLogicTester.csproj
├── Program.cs
├── Commands/
├── Reports/
└── Scenarios/
```

这一层更多解决“工程组织”和“工具体验”问题，不应阻塞主线逻辑推进。

## 推荐实施顺序

### Milestone 0：验收口径收敛

**目标**：先统一“什么算通过”，避免后面边做边打架。

**任务清单**：

- [ ] 统一 `FrameSyncMvpAValidator` 与当前 `FrameSync` 实现的约束口径
- [ ] 明确 `Dictionary<K,V>` / `HashSet<T>` 是否仍属于禁止项
- [ ] 将当前已存在的 TestHarness 与 validator 的职责边界写清楚

**原因**：

当前 validator 的静态扫描规则与现有 `BattleWorldState` / `SnapshotBuffer` 实现并不完全一致。如果不先收敛口径，后续即使代码走在正确方向上，也会持续被验收工具误伤。

**验证**：

- `FrameSyncMvpAValidator` 的失败项能真实反映当前要约束的风险，而不是历史规则残留

### Milestone 1：测试入口 MVP

**目标**：让 agent 真的“随改随测”。

**任务清单**：

- [x] 在现有 `--mode=test` 基础上增加 `--scenario`
- [x] 增加 `--frames`、`--clients`、`--output`、`--report`
- [x] 增加 `--mode=validate` 或等价路由，统一触发 validator
- [x] 输出 Console / Markdown / JSON 三种结果
- [x] 保证退出码稳定：0 = PASS，1 = FAIL
- [x] 输出格式固定，便于 agent 直接解析

**验证**：

- 能运行 `dotnet run --project 'GameServer/Server/Main/Main.csproj' -- --mode=test --scenario determinism --frames 300`
- 能运行 `dotnet run --project 'GameServer/Server/Main/Main.csproj' -- --mode=validate`
- 能生成 Markdown / JSON 报告

### Milestone 2：为 v0.3b / v0.3c 补测试护栏

**目标**：测试入口不只是“有”，而是能真正保护主线。

**任务清单**：

- [ ] 增加 `join-aligns-global-frame`
- [ ] 增加 `frame-index-is-global`
- [ ] 增加 `catchup-target-is-auth-plus-lead`
- [ ] 增加 `expired-input-is-dropped`
- [ ] 增加 `future-input-is-buffered`
- [ ] 增加 `missing-input-reuses-last`
- [ ] 为后续回滚补 `rollback-restores-snapshot-and-replay`

**验证**：

- `v0.3b` 每完成一步都能跑对应无头回归
- `v0.3c` 加入后，能无头验证回滚与重播结果

### Milestone 3：独立 CLI 产品化（可选）

**目标**：把当前测试 MVP 抽成可维护的团队工具。

**任务清单**：

- [ ] 新建 `GameServer/Tools/GameLogicTester/`
- [ ] 将现有 `TestHarness` / validator 以组合方式接入，而非复制实现
- [ ] 整理统一命令接口：`test` / `validate` / `run-scenario`
- [ ] 保持与 `--mode=test` 的兼容或明确迁移策略

**验证**：

- 能运行 `dotnet run --project 'GameServer/Tools/GameLogicTester/GameLogicTester.csproj' -- test battle --frames 300`
- 与旧入口输出一致或有明确迁移说明

### Milestone 4：主线稳定后再扩展

**目标**：把“有用”升级成“完整”。

**扩展方向**：

- `SkillScenario`
- `NetworkScenario`
- JSON 场景配置系统
- 性能分析与长时间运行
- CI/CD 集成

**说明**：

这些都应该建立在 `v0.3b` / `v0.3c` 逻辑稳定之后，否则返工成本很高。

## 当前不建议立即做的内容

- 不建议现在立刻从零创建完整 `GameLogicTester` 并把所有逻辑搬进去
- 不建议现在就做技能系统测试全覆盖
- 不建议现在就做高保真网络模拟
- 不建议现在就做长时间性能工具链
- 不建议现在就做通用 JSON 场景库

这些方向不是不要做，而是现在做性价比偏低。

## 与现有实现映射

- `GameServer/Server/Main/Program.cs`
  当前已经支持 `--mode=test`，适合作为测试 MVP 的宿主入口。

- `GameServer/Server/Entity/TestHarness/TestRunner.cs`
  当前已经有 4 个核心场景，是测试 MVP 的直接基础。

- `GameServer/Server/Entity/TestHarness/SimulatedClient.cs`
  当前是轻量客户端仿真层，适合作为 battle/determinism 的基座。

- `GameServer/Server/Entity/Battle/BattleLogic.cs`
  当前是纯 C# 主逻辑，适合作为无头回归的核心被测对象。

- `GameServer/Tools/FrameSyncMvpAValidator`
  当前是独立 validator，适合作为 `validate` 子命令或并列路由接入。

## 建议命令形态

### 当前阶段建议

```bash
# 跑无头逻辑场景
dotnet run --project 'D:\unity\Tencent\TEngine\GameServer\Server\Main\Main.csproj' -- --mode=test --scenario determinism --frames 300

# 跑帧同步基础验收
dotnet run --project 'D:\unity\Tencent\TEngine\GameServer\Tools\FrameSyncMvpAValidator\FrameSyncMvpAValidator.csproj' -- --duration-seconds 30 --update-hz 60
```

### 产品化后建议

```bash
gamelogic-tester test battle --frames 300 --clients 2 --output markdown --report test.md
gamelogic-tester validate framesync --output json --report validator.json
```

## 完成标准

### 测试 MVP

- [ ] `--mode=test` 支持按场景运行
- [ ] 支持 `--frames` / `--clients` / `--output` / `--report`
- [ ] 有稳定退出码
- [ ] Battle/TestHarness 与 validator 都能被命令行直接触发
- [ ] `v0.3b` 关键场景有无头回归用例
- [ ] agent 改完纯逻辑代码后可直接跑回归，无需手动拉起 Unity

### 完整 CLI

- [ ] 形成独立 `GameLogicTester` 工具目录
- [ ] 支持 `test` / `validate` / `run-scenario`
- [ ] 支持 Battle / 确定性 / 网络 / 技能 / 回滚等多类场景
- [ ] 支持结构化报告与 CI/CD 集成

## 技术风险与缓解

| 风险 | 缓解措施 |
|------|----------|
| 过早做完整独立 CLI，后续被主线重构反复推翻 | 先做测试 MVP，产品化后置 |
| validator 规则与现有实现不一致，导致“假失败” | 先做 Milestone 0，收敛验收口径 |
| 测试入口分散，agent 不知道该跑哪条命令 | 在 `Main` 入口先统一常用命令形态 |
| 技能/网络测试过早介入，返工成本高 | 等 `v0.3b` / `v0.3c` 稳定后再扩展 |
| 报告格式不稳定，agent 难以解析 | 早期就固定 Console / Markdown / JSON 模式 |

## 关键优势

1. **最快见效**：不用等完整工具做完，agent 很快就能帮你跑测试。
2. **返工更少**：先做轻量测试护栏，避免被主线重构反复推翻。
3. **最大化复用**：直接复用 `v0.3-test` 和 validator 现有成果。
4. **主线更稳**：`v0.3b` / `v0.3c` 每一步都能无头回归。
5. **后续可产品化**：等主线稳定后，再自然演进成独立 CLI 工具。
