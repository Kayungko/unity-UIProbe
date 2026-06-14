# UIProbe MCP 授权模型

> 分支：`plan/workbench-refactor`  
> 范围：记录 UIProbe MCP 的授权模式设计。本文档补充 `Docs/MCPReplacementPlan.md`，用于定义类似 Codex 的运行时授权方式。  
> 状态：草案。

---

## 1. 设计目标

UIProbe MCP 既要能替代项目里其他 MCP 的高能力工具，又不能在默认状态下把团队项目暴露给高风险自动化。

因此安全模型分成两层：

```text
Capability Profile 能力档位
    决定当前会话最多允许哪些类型的能力

Approval Mode 授权模式
    决定遇到需要权限的工具调用时如何处理
```

Capability Profile 解决“最大能力边界”，Approval Mode 解决“每次调用是否需要批准”。

---

## 2. 授权模式总览

UIProbe MCP 建议提供类似 Codex 的四种授权模式：

```text
RequestApproval       请求批准
RiskBasedApproval     替我批准低风险操作，仅风险操作请求批准
FullAccess            完全访问权限
CustomConfig          使用 config.toml 自定义权限
```

工作台 UI 可以显示为：

```text
请求批准
每次工具调用、文件写入、菜单执行、代码执行、外部进程等都需要确认。

替我批准
只对检测到的风险操作请求批准。只读、查询、报告、预览类工具自动允许。

完全访问权限
不受限制地访问允许档位内的工具、网络和项目文件。仍受 Capability Profile 最大边界约束。

自定义（config.toml）
使用 config.toml 中定义的权限、allowlist、denylist、路径范围和工具策略。
```

---

## 3. 与 Capability Profile 的关系

Approval Mode 不能突破 Capability Profile。

例如：

```text
当前 Profile = SafeDefault
Approval Mode = FullAccess
```

这不代表可以执行任意 C#、反射、外部进程。它只代表在 SafeDefault 档位允许的工具范围内不再逐个请求批准。

再例如：

```text
当前 Profile = AdminDebug
Approval Mode = RequestApproval
```

这代表高风险能力可见，但每次调用仍需要用户确认。

最终权限判断顺序：

```text
1. Tool 是否存在
2. Tool 是否被当前 Capability Profile 允许
3. Tool 是否被项目策略 allow / deny
4. Tool 是否被用户本地策略 allow / deny
5. 当前 Approval Mode 是否允许自动执行
6. 若不允许自动执行，向用户请求批准
```

---

## 4. 各授权模式细节

### 4.1 RequestApproval 请求批准

最保守模式。

适用场景：

- 新项目首次启用 MCP。
- 不确定 AI client 是否可信。
- 开启了 TrustedProject / AdminDebug 档位但仍想逐次确认。
- 团队项目默认审慎策略。

行为：

- 只读查询可以配置为是否也请求批准。
- 写操作必须请求批准。
- 菜单执行必须请求批准。
- 代码执行必须请求批准。
- 反射调用必须请求批准。
- 外部进程必须请求批准。
- 外部网络必须请求批准。

建议默认：

```text
ReadOnly             自动允许，或由配置决定
PreviewOnly          自动允许
WriteSafe            请求批准
WriteDestructive     请求批准
CodeExecution        请求批准
Reflection           请求批准
MenuExecution        请求批准
ExternalProcess      请求批准
ExternalNetwork      请求批准
Experimental         请求批准
```

---

### 4.2 RiskBasedApproval 替我批准

推荐日常模式。

适用场景：

- 团队日常使用。
- AI 需要频繁查询 Unity 状态、Prefab 索引、资源引用、检查报告。
- 不希望每个只读工具都弹窗。

行为：

- 低风险工具自动允许。
- Preview 工具自动允许。
- 高风险工具请求批准。
- 命中 denylist 直接拒绝。
- 命中 allowlist 且风险等级足够低，可以自动允许。

建议默认自动允许：

```text
ReadOnly
PreviewOnly
ReportExport 到受控目录
Console 查询
Editor State 查询
Prefab Index 查询
Asset Reference 查询
UI Check 查询
```

建议请求批准：

```text
WriteSafe
WriteDestructive
MenuExecution
CodeExecution
Reflection
ExternalProcess
ExternalNetwork
Experimental
```

---

### 4.3 FullAccess 完全访问权限

高信任模式。

适用场景：

- 本地个人项目。
- 管理员调试。
- 已经明确允许 AI 自动完成多步骤工作流。
- 临时迁移旧 MCP 工具。

行为：

- 当前 Capability Profile 允许的工具默认执行。
- 不逐次弹出批准提示。
- 仍然受项目级 denylist 限制。
- 仍然记录审计日志。
- 仍然建议限制 loopback 访问。

限制：

- FullAccess 不是无边界。
- FullAccess 不能突破 Profile。
- FullAccess 不应默认允许远程网络客户端。
- FullAccess 应支持临时有效期。

建议 UI 显示明显警告：

```text
完全访问权限会允许当前档位内的工具不再逐次请求批准。
在 AdminDebug 档位下，这可能包括代码执行、反射调用、外部进程和网络访问。
```

---

### 4.4 CustomConfig 自定义 config.toml

给团队和高级用户使用。

适用场景：

- 团队希望锁定统一策略。
- 不同项目有不同允许目录、菜单、工具。
- CI / 自动化环境需要无 UI 批准。
- 管理员希望细粒度配置 allowlist / denylist。

示例：

```toml
[authorization]
profile = "TeamAutomation"
approval_mode = "RiskBasedApproval"
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

## 5. 授权请求内容

当工具调用需要用户批准时，UIProbe 工作台应展示清楚信息。

建议字段：

```json
{
  "toolId": "ui_probe.write_project_file_execute",
  "toolName": "写入项目文件",
  "source": "builtin",
  "safety": "WriteDestructive",
  "profile": "TrustedProject",
  "approvalMode": "RiskBasedApproval",
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

用户操作：

```text
批准本次
本次拒绝
本会话总是允许此工具
本项目总是允许此工具
将此工具加入 denylist
打开 config.toml
```

---

## 6. 工作台 UI 建议

UI Toolkit 工作台中建议新增 MCP 权限选择器。

位置：

```text
MCP / 工具中心
├─ Runtime 状态
├─ Tools
├─ Permissions
│  ├─ Capability Profile
│  ├─ Approval Mode
│  ├─ Path Policy
│  ├─ Tool Allowlist / Denylist
│  ├─ Menu Allowlist
│  └─ Network / Process Policy
└─ History / Audit
```

顶部状态可以类似：

```text
SafeDefault · 替我批准
TeamAutomation · 请求批准
AdminDebug · 完全访问
CustomConfig · config.toml
```

---

## 7. 审计日志

所有工具调用都应记录基础审计信息。

高风险工具必须记录完整审计信息。

基础字段：

```text
时间
会话 ID
MCP client
工具 ID
工具来源
Capability Profile
Approval Mode
批准方式
输入摘要
输出摘要
是否成功
错误信息
耗时
```

高风险字段：

```text
受影响路径
菜单路径
代码摘要 hash
反射目标类型 / 方法
外部进程命令摘要
网络目标
用户批准记录
```

---

## 8. 推荐默认值

首次安装 UIProbe MCP：

```text
Capability Profile: SafeDefault
Approval Mode: RiskBasedApproval
Remote Clients: Disabled
Loopback Only: Enabled
Config Source: Project default + User local override
```

如果用户选择“完全访问”：

```text
- 需要明显警告
- 可以选择有效期：15 分钟 / 1 小时 / 本会话 / 永久
- 永久启用需要写入用户本地配置，不建议写入项目默认配置
- AdminDebug + FullAccess 需要二次确认
```

---

## 9. 结论

UIProbe MCP 的安全设计不应该是“禁止高风险能力”，而应该是：

```text
默认安全
按档位开放能力
按授权模式决定是否请求批准
按 config.toml 支持团队级细粒度治理
所有高风险调用可审计、可追踪、可关闭
```

这样 UIProbe MCP 才能同时满足两个目标：

```text
替代其他 MCP 的强能力
保留团队项目需要的安全、可控、可审计
```
