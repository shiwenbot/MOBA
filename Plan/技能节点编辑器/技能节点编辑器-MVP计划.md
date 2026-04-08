# 技能节点编辑器 MVP 开发计划

## Context

策划需要一个可视化节点编辑器来编辑技能逻辑，最终导出为配置文件供运行时使用。本次 MVP 目标：实现一个基于 Unity GraphView API 的最简图编辑窗口，支持创建节点、连接端口。

项目使用 Unity 2022.3，内置 `UnityEditor.GraphView` 正式 API。项目中无现有 GraphView 代码，需从零搭建。

---

## 目录结构

代码放在 `UnityProject/Assets/Editor/SkillGraph/` 下，属于 `Assembly-CSharp-Editor`，无需新建 asmdef。

```
Assets/Editor/SkillGraph/
├── SkillGraphEditor.cs          -- EditorWindow 入口
├── SkillGraphView.cs            -- GraphView 画布
├── SkillGraphNode.cs            -- 节点基类
└── SkillGraphSearchWindow.cs    -- 节点创建搜索窗口
```

命名空间：`TEngine.Editor.SkillGraph`

---

## 实现步骤

### 第 1 步：SkillGraphEditor.cs — 窗口入口

- 继承 `EditorWindow`
- `[MenuItem("TEngine/Skill Graph Editor")]` 注册菜单
- `OnEnable`：创建 `SkillGraphView` 实例，添加到 `rootVisualElement`；创建 `SkillGraphSearchWindow` 实例
- `OnDisable`：清理资源

### 第 2 步：SkillGraphView.cs — 画布核心

- 继承 `UnityEditor.GraphView.GraphView`
- 构造函数添加：
  - `GridBackground` 网格背景
  - 操控器：`ContentDragger`、`ContentZoomer`、`SelectionDragger`、`RectangleSelector`
- 重写 `GetCompatiblePorts`：输出端口只能连输入端口，不连自身节点，类型兼容
- 注册 `Delete` 键回调：删除选中节点和连线
- 处理 `nodeCreationRequest` 事件：弹出 SearchWindow

### 第 3 步：SkillGraphNode.cs — 节点

- 继承 `UnityEditor.GraphView.Node`
- 构造参数：`string nodeTitle`
- 提供方法：
  - `AddInputPort(string name, Type type)` — 调用 `InstantiatePort` + `inputContainer.Add`
  - `AddOutputPort(string name, Type type)` — 调用 `InstantiatePort` + `outputContainer.Add`
- MVP 默认创建一个 Input 端口 + 一个 Output 端口
- 修改后调用 `RefreshPorts()` 和 `RefreshExpandedState()`

### 第 4 步：SkillGraphSearchWindow.cs — 搜索窗口

- 实现 `ISearchWindowProvider`
- `CreateSearchTree`：返回包含 "Skill Node" 选项的树形菜单
- `OnSelectEntry`：创建节点，坐标从屏幕坐标转为 GraphView 本地坐标（两步转换），添加到 GraphView

---

## GraphView API 关键注意点

1. 命名空间用 `UnityEditor.GraphView`（正式 API），不是 `Unity.Experimental.GraphView`
2. `GetCompatiblePorts` 必须重写，否则默认返回空列表，无法连线
3. 节点修改后必须调用 `RefreshPorts()` + `RefreshExpandedState()`
4. SearchWindow 坐标转换：`screenMousePosition` → `rootVisualElement.ChangeCoordinatesTo` → `contentViewContainer.WorldToLocal`

---

## 验证方式

1. Unity 菜单 → TEngine → Skill Graph Editor 打开窗口
2. 窗口显示网格背景，可拖拽和缩放画布
3. 右键或快捷键弹出搜索窗口，选择 "Skill Node" 创建节点
4. 节点有一个输入端口和一个输出端口
5. 从输出端口拖线到另一个节点的输入端口，成功连线
6. 选中节点/连线后按 Delete 键可删除

---

## 后续扩展（本次不实现）

- 节点类型体系（ActionNode、ConditionNode、TriggerNode）
- 序列化为 ScriptableObject
- 导出为 Luban/JSON 配置
- 自定义节点 UI（枚举、文本框等）
- 黑板（Blackboard）面板
