# UIProbe MCP 授权模型

> 分支：`plan/workbench-refactor`  
> 范围：记录 UIProbe MCP 的授权模式设计与正交判定流程。本文档补充 `MCPReplacementPlan.md`，定义类似 Codex 的运行时授权方式。安全等级枚举见 `ToolContract.md` §5。  
> 状态：方案已收敛，授权模式 ⊥ Capability Profile 的正交关系已锁定（见 §1）。

---

## 1. 核心模型：授权模式 ⊥ Capability Profile（正交）

UIProbe MCP 有两个**正交**的权限维度，二者解决不同问题，不合并：

- **Capability Profile = 能力天花板**：决定某工具"是否被允许存在/可被启用"，由**团队在项目级锁定**。档位见 `MCPReplacementPlan.md` §6.1（SafeDefault / TeamAutomation / TrustedProject / AdminDebug）。
- **授权模式 = 批准策略**：决定已在天花板内的工具"调用时是否自动放行还是弹窗"，由**用户本地选择**。UI 上只是一个下拉框。

用户日常只感知授权模式；Profile 由团队配置并锁定。授权模式不对应单一 Profile，而是任意 Profile 之上的批准策略。

> **关键裁定**：`完全访问` **不**隐式把 Profile 顶到 AdminDebug。完全访问只改"放行方式"，能力仍受团队锁定的天花板约束——否则一个下拉框就能绕过团队锁定。`完全访问 + SafeDefault` 合法但无害（自动放行仅限只读）；`请求批准 + AdminDebug` 也合法（能力全开但每次弹窗）。

### 1.1 统一判定流程

一个工具调用 T 是否最终执行，按顺序判定：

```text
1. T.minProfile <= 当前 activeProfile?      否 → 拒绝(NOT_IN_PROFILE，不出现在 list_tools)
2. 项目级 deny 命中?                         是 → 拒绝(PERMISSION_DENIED)
3. 项目级 allow 未命中且 T 非默认启用?       是 → 拒绝(PERMISSION_DENIED)
4. T.enabled == true?                        否 → 拒绝
5. 按授权模式 + T.safety 判放行方式:
   请求批准: ReadOnly/PreviewOnly 自动放行，其余 → 弹窗
   替我批准: ReadOnly/PreviewOnly/受控报告导出 自动放行，其余 → 弹窗
   完全访问: 全部自动放行（仍审计）
   自定义:   按 config.toml mode/allow/deny 判
   → 若 T.RequiresConfirmation==true，即使自动模式也强制弹窗一次
6. 会话级"总是允许此工具"缓存命中 → 跳过弹窗
7. 执行 + 写审计日志
```

口诀：**Profile 先卡能力上限，授权模式再决定放行方式。**

---

## 2. 授权模式总览

工作台只暴露一个授权下拉框（这是用户感知的唯一权限入口）：

```text
请求批准
替我批准
完全访问
自定义（config.toml）
```

这四个模式定义的是"批准策略"，能力边界仍由 Capability Profile 决定（见 §1）。

### 2.1 请求批准

最保守模式。

适合：

```text
- 新项目首次启用 MCP
- 团队默认审慎策略
- AI client 不完全可信
- 管理员正在测试高风险工具
```

行为：

```text
- 只读查询默认允许
- Preview 工具默认允许
- 写文件、改 prefab、菜单执行、代码执行、反射、外部进程、网络访问都请求批准
- 用户可选择“仅本次批准”或“本会话允许此工具”
```

---

### 2.2 替我批准

推荐日常模式。

适合：

```text
- 团队日常开发
- AI 需要频繁读取 Unity 状态、Prefab 索引、资源引用、检查报告
- 用户不希望每个低风险操作都弹窗
```

行为：

```text
自动允许：
- health / editor state / selection
- console 查询
- prefab index 构建与查询
- asset reference 查询
- UI check 执行与结果读取
- report 导出到受控目录
- preview 类工具

请求批准：
- 修改 Assets 下资源
- 修改 prefab
- 批量重命名
- 图片覆盖生成
- 大红大金导入执行
- 菜单执行
- 代码执行
- 反射调用
- 外部进程
- 外部网络
```

这是默认推荐模式。

---

### 2.3 完全访问

高信任模式。

适合：

```text
- 本地个人项目
- 管理员调试
- 迁移旧 MCP 能力
- 明确希望 AI 自动完成多步骤工作流
```

行为：

```text
- UIProbe MCP 内置工具默认允许执行
- 项目扩展工具默认允许执行
- 菜单执行、代码执行、反射、外部进程、网络访问可以启用
- 不逐次请求批准
- 仍然记录审计日志
- 仍建议只监听 127.0.0.1
```

UI 必须明显提示：

```text
完全访问会允许 AI 在当前项目中执行高风险操作，包括文件写入、菜单执行、代码执行、反射调用、外部进程和网络访问。仅在信任当前 MCP client 时启用。
```

完全访问模式可以作为“替代其他全能 MCP”的兼容模式，但不作为默认模式。

---

### 2.4 自定义（config.toml）

高级模式。

适合：

```text
- 团队希望统一权限策略
- 不同项目需要不同 allowlist / denylist
- CI 或自动化环境不希望弹 UI
- 管理员希望细粒度控制路径、菜单、网络、外部进程
```

示例：

```toml
[authorization]
mode = "ask-on-risk"
loopback_only = true
remote_clients = false
session_ttl_minutes = 120

[tools]
allow = [
  "ui_probe.health",
  "ui_probe.get_editor_state",
  "ui_probe.build_prefab_index",
  "ui_probe.search_prefabs",
  "ui_probe.find_asset_references",
  "ui_probe.run_ui_checks",
  "ui_probe.export_report"
]

deny = [
  "ui_probe.run_editor_script_execute",
  "ui_probe.reflect_method_call",
  "ui_probe.run_external_process"
]

[paths]
read_allow = [
  "Assets/",
  "Packages/",
  "ProjectSettings/"
]

write_allow = [
  "Assets/UIProbeReports/",
  "Assets/UI/"
]

write_deny = [
  "ProjectSettings/",
  "Packages/manifest.json"
]

[menu]
allow = [
  "Assets/Refresh",
  "UI Probe/打开面板"
]

deny = [
  "File/Save Project",
  "File/Build Settings..."
]

[network]
allow = []
deny_all = true

[external_process]
allow = []
deny_all = true
```

---

## 3. 工具风险等级

虽然 UI 上只显示四种授权模式，内部仍需要给工具标注风险等级，用于“替我批准”和“自定义”模式判断。

```text
ReadOnly           只读查询
PreviewOnly        只生成预览，不修改项目
WriteSafe          可控写入，支持回滚或影响范围较小
WriteDestructive   批量覆盖、删除、重命名、修改 prefab 等
MenuExecution      执行 Unity 菜单
CodeExecution      执行 C# / 动态脚本
Reflection         反射访问类型或方法
ExternalProcess    启动外部进程
ExternalNetwork    访问外部网络
Experimental       实验能力
```

默认策略：

```text
请求批准：ReadOnly / PreviewOnly 自动允许，其余请求批准
替我批准：ReadOnly / PreviewOnly / 受控报告导出自动允许，其余请求批准
完全访问：默认允许，但仍记录审计日志
自定义：按 config.toml 判断
```

---

## 4. 授权请求内容

当工具调用需要用户批准时，UIProbe 工作台应展示清楚信息。

建议字段：

```json
{
  "toolId": "ui_probe.write_project_file_execute",
  "toolName": "写入项目文件",
  "source": "builtin",
  "risk": "WriteDestructive",
  "authorizationMode": "替我批准",
  "reason": "该工具会写入项目文件",
  "inputSummary": {
    "path": "Assets/UI/Shop/ShopPanel.prefab",
    "operation": "modify"
  },
  "riskSummary": [
    "将修改 Assets 目录中的文件",
    "可能影响 prefab 引用",
    "建议先查看 preview"
  ],
  "supportsPreview": true,
  "supportsUndo": true
}
```

用户操作尽量少：

```text
批准本次
拒绝
本会话总是允许此工具
打开权限设置
```

不建议默认暴露太多高级按钮，避免 UI 复杂化。

---

## 5. 工作台 UI 建议

UI Toolkit 工作台中只显示一个授权选择器：

```text
MCP 授权：替我批准 ▼
```

下拉项：

```text
请求批准
每次风险操作都请求确认。

替我批准
只对检测到的风险操作请求确认。推荐。

完全访问
不受限制地访问互联网和项目文件。仅在信任当前 client 时使用。

自定义（config.toml）
使用 config.toml 中定义的权限。
```

高级配置放进二级页面：

```text
MCP / 权限
├─ 当前授权模式
├─ 工具 allowlist / denylist
├─ 路径策略
├─ 菜单策略
├─ 网络策略
├─ 外部进程策略
└─ 审计日志
```

主界面只保留简单状态，不让用户一开始就面对复杂策略。

---

## 6. 审计日志

所有工具调用都应记录基础审计信息。

基础字段：

```text
时间
会话 ID
MCP client
授权模式
工具 ID
工具来源
风险等级
批准方式
输入摘要
输出摘要
是否成功
错误信息
耗时
```

高风险工具额外记录：

```text
受影响路径
菜单路径
代码摘要 hash
反射目标类型 / 方法
外部进程命令摘要
网络目标
用户批准记录
elevatedFrom（是否临时升档触发）
confirmationToken
```

### 6.1 审计存储与隐私

- **存储位置**：`<Project>/UserSettings/UIProbe/audit/` 下按天滚动 **JSONL**（一行一事件），默认保留 **30 天** + 上限大小滚动删除。
- **隐私**：审计含路径与代码摘要 hash，默认**不纳入版本控制**（`UserSettings/` 不应进 git）；高风险事件可选镜像到项目级 `Logs/` 供团队审查，但需团队显式开启。
- **不可变性**：仅追加；MCP 工具自身禁止写/删审计目录（写策略硬编码 deny），防止 AI 抹除痕迹。
- **链路串联**：每条审计带 `correlationId`，串联一次 AI 调用在 Node Server / Bridge / Service 三处的日志。

---

## 7. 推荐默认值与配置治理

### 7.1 首次安装默认值

```text
授权模式：替我批准
Capability Profile：SafeDefault（项目 maxProfile 默认锁定 TrustedProject）
Remote Clients：Disabled
Loopback Only：Enabled（AdminDebug 强制 loopback，不可被本地 override 解开）
Config Source：Project default + User local override
```

如果用户选择"完全访问"，工作台应显示明显警告，并建议仅在本地可信环境使用。

### 7.2 配置文件位置与优先级

| 配置 | 路径 | 版本控制 | 职责 |
|---|---|---|---|
| 项目级 | `ProjectSettings/UIProbe/mcp.config.toml` | **纳入** | `max_profile`、`default_profile`、allow/deny、路径/菜单/网络/外部进程策略 |
| 用户本地 | `UserSettings/UIProbe/mcp.local.toml` | **.gitignore** | 授权模式选择、临时升档、会话偏好 |

合并规则（用户本地只能**收紧**，不能突破天花板）：

```text
deny    = 项目 deny ∪ 本地 deny        （deny 永远胜出）
profile = min(本地选档, 项目 max_profile)
```

- **CI 环境**：强制 `--config` 指向项目 toml，`mode=自定义`、`loopback_only=true`、`remote_clients=false`，禁用一切弹窗（无 UI 即拒绝而非阻塞）。

### 7.3 Profile 继承、锁定与临时升档

- 项目 toml 设 `max_profile`（锁定上限）+ `default_profile`（默认档）。用户档 = `min(用户选, max_profile)`，超上限项在 UI 显示为灰锁。
- **临时升档**：本地 `mcp.local.toml` 写 `elevated_profile` + `expires_at`（TTL，如 120 分钟），到期自动回落；每次升档强制审计一条 `profile_elevated`（谁/何时/到哪档/有效期）。
- 团队把 `max_profile=TrustedProject` 提交版控即完成锁定，个人无法在 UI 调过此线。

### 7.4 远程访问

- 默认 `loopback_only=true` / `remote_clients=false`，Bridge 绑定 `127.0.0.1`。
- **AdminDebug 强制 loopback-only（硬约束，不可被本地 override 解开）**；AdminDebug + 远程组合直接禁止。
- 远程访问只能在项目 toml 显式开 `remote_clients=true` 且档位 ≤ TeamAutomation 时允许。
- loopback 之外仍需 token 鉴别同机进程，见 `MCPReplacementPlan.md` §6.3。

---

## 8. 与 MCP 替代计划的关系

`Docs/MCPReplacementPlan.md` 负责定义 UIProbe MCP 如何替代其他 Unity MCP。

本文档负责定义替代过程中最重要的用户体验：

```text
默认简单
日常少打扰
风险可确认
全能可选
高级可配置
```

最终目标不是让 UIProbe MCP 看起来更复杂，而是让用户能像 Codex 一样快速选择信任级别。