# HTML 模板规范

HTML 版本是计划文档的精美排版版，使用 Anthropic 官网风格。

## 样式要点

- **整体布局**：左侧固定导航栏（240px）+ 右侧内容区域
- **背景**：深色渐变（#1a1a2e → #16213e → #0f3460）
- **卡片**：深色半透明背景（rgba(255,255,255,0.05)），圆角 12px，微妙边框
- **字体**：系统字体栈，标题用 600/700 weight
- **强调色**：主色 #D97706（琥珀色），辅助色 #8B5CF6（紫色）
- **代码/参数**：等宽字体 + 背景高亮（rgba(217,119,6,0.15)）
- **校验标签**：绿色 ✅ / 黄色 ⚠️ / 红色 ❌，对应颜色背景
- **响应式**：移动端导航折叠为汉堡菜单

## HTML 结构

```html
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>[模块名]-[版本号]计划</title>
  <style>
    /* 内联所有 CSS，确保自包含 */
  </style>
</head>
<body>
  <!-- 顶部标题栏 -->
  <header>
    <h1>[模块名]-[版本号]计划：[阶段标题]</h1>
    <div class="validation-summary">
      <!-- 校验结果摘要徽章 -->
    </div>
  </header>

  <!-- 左侧导航 -->
  <nav id="sidebar">
    <div class="nav-section">目录</div>
    <a href="#context">Context</a>
    <a href="#status">阶段状态</a>
    <a href="#goals">目标</a>
    <!-- ... -->
  </nav>

  <!-- 主内容区 -->
  <main id="content">
    <section id="context" class="card">
      <!-- 对应 MD 的每个章节 -->
      <!-- 数值参数用 <span class="param"> 包裹 -->
      <!-- 校验结果用 <span class="badge pass/warn/fail"> 标记 -->
    </section>
    <!-- ... -->
  </main>

  <!-- 校验详情面板（可折叠） -->
  <section id="validation-detail" class="card collapsible">
    <h2>校验详情</h2>
    <!-- 4 步校验的完整结果 -->
  </section>
</body>
</html>
```

## MD → HTML 转换规则

| MD 元素 | HTML 转换 |
|---------|----------|
| `# 标题` | `<header><h1>` 或 `<section><h2>` |
| `## 章节` | `<section id="slug" class="card"><h2>` |
| `### 子节` | `<h3>` |
| 表格 | `<table class="styled-table">` |
| ` [ ] 复选框` | 完成标准中的待验证项 |
| ` [x] 复选框` | 已通过的验证项 |
| 代码标识符 | `<code>` 标签 |
| 数值参数（带单位） | `<span class="param">` 高亮 |
| ✅ ⚠️ ❌ | `<span class="badge pass/warn/fail">` |

## 特殊组件

### 校验摘要栏
```html
<div class="validation-summary">
  <span class="badge pass">代码可行性 ✅</span>
  <span class="badge warn">GDD 一致性 ⚠️ 2</span>
  <span class="badge pass">总目录边界 ✅</span>
  <span class="badge pass">结构完整性 ✅</span>
</div>
```

### 参数高亮
```html
<span class="param" title="来源：GDD-击球系统 参数表">22 m/s</span>
```

### 风险表格
```html
<table class="risk-table">
  <tr class="risk-high"><td>...</td></tr>
  <tr class="risk-medium"><td>...</td></tr>
</table>
```
