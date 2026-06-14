# UIProbe 分发与版本治理

> 分支：`plan/workbench-refactor`
> 范围：UIProbe 作为"装进多个 Unity 项目的可分发工具 + 一个 Node MCP Server"的分发形态、版本绑定、升级迁移策略。
> 状态：草案。本文档补齐早期计划几乎空白的分发/版本维度。

---

## 1. 为什么需要这份文档

UIProbe 不是单一项目内的一次性脚本，而是会被安装进多个 Unity 项目的工具，且引入了一个独立的 Node 进程（MCP Server）。这带来早期计划没处理的问题：

- C# 包（Unity 内）与 Node Server 是两套发布物，如何同步版本、避免版本错配握手失败。
- 升级 UIProbe 后，旧的 `mcp.config.toml`、`mcp.local.toml`、Prefab IndexCache 如何迁移而不丢配置/不崩。
- `ToolContract` schema 改了，AI client 缓存的 tool 列表如何失效重拉。
- 多个 Unity 项目同时装了**不同版本**的 UIProbe，端口探测 / serverId 路由能否区分版本。

---

## 2. 分发形态

| 发布物 | 形态 | 说明 |
|---|---|---|
| Unity 侧（C#） | **UPM package**（`com.uiprobe.*`） | Editor-only asmdef；从当前"裸放 Assets/"迁为 package。这是新增工作，现状无 asmdef |
| MCP Server（Node） | **npm 包 + npx 启动** | AI client 配置里用 `npx` 拉起，零本地安装；锁 MCP SDK 版本 |
| 二者关系 | 同一仓库、同步版本号 | C# 包 version 与 Node 包 version 保持一致主次版本 |

### 2.1 asmdef 化（前置工作）

现状全仓库无 asmdef，在 Assets 下全局编译。package 化前必须先拆：

```text
UIProbe.Editor.asmdef        Editor-only，引用 UI Toolkit 程序集
UIProbe.Runtime.asmdef       可选，纯数据/运行时安全类型
UIProbe.Tests.Editor.asmdef  测试，引用 Editor + Adapter 接缝
```

UXML / USS / Icons 作为 package 内资源，须用 `AssetDatabase.LoadAssetAtPath("Packages/com.uiprobe.../...")` 加载，不能依赖 `Assets/` 相对路径。

---

## 3. 版本号策略（SemVer）

三个版本号需协调：

```text
UIProbe 包版本      C# 包 + Node 包共享同一 MAJOR.MINOR（PATCH 可各自递增）
ToolContract 版本   独立 SemVer，记录在 ToolDescriptor.ContractVersion
Unity 兼容基线      2022.3 LTS+（package.json 的 unity 字段声明）
```

规则：
- `ToolContract` 的 **major** 变更（删字段/改语义）= 不兼容 → AI client 必须重拉 tool schema。
- C# 包与 Node 包的 MINOR 必须一致才允许握手（见 §4）。
- PATCH 级别（纯修 bug、不改契约）允许 C# / Node 单独发。

---

## 4. 版本握手

Unity Bridge `/health` 返回的字段用于 Node Server 启动时握手校验：

```json
{
  "status": "ok",
  "serverId": "guid",
  "pid": 12345,
  "projectPath": "...",
  "projectName": "...",
  "unityVersion": "2022.3.x",
  "uiProbeVersion": "x.y.z",
  "contractVersion": "a.b.c",
  "port": 58300,
  "isCompiling": false,
  "isUpdating": false,
  "isPlaying": false
}
```

握手逻辑：
1. Node Server 读自身 `contractVersion` 支持范围。
2. 拉 `/health`，比对 `contractVersion` major / C# 包 minor。
3. 不兼容 → 不连接，向 AI client 返回明确 `VERSION_MISMATCH` 提示（建议升级哪一侧）。

---

## 5. 配置与缓存迁移

| 数据 | 位置 | 升级迁移策略 |
|---|---|---|
| 项目级 MCP 配置 | `ProjectSettings/UIProbe/mcp.config.toml`（版控） | 带 `schemaVersion`；升级时按版本链迁移，未知字段保留 |
| 用户本地配置 | `UserSettings/UIProbe/mcp.local.toml`（.gitignore） | 同上；迁移失败时回退默认 + 告警，不阻塞 |
| Prefab IndexCache | 现有缓存机制 | 带版本标记；版本不符时**自动重建**而非读坏数据 |
| 既有 UIProbeConfig | 已有 `version` 字段 + `MigrateFromEditorPrefs()` | **复用现有迁移雏形**，扩展为统一迁移链，不另起炉灶 |

迁移原则：
- 每类持久化数据都带 `schemaVersion`。
- 迁移是"向前升级"单向链；缺失字段补默认，未知字段保留（前向兼容）。
- 缓存类数据（IndexCache）版本不符直接重建，不尝试迁移。
- 配置类数据迁移失败 → 回退默认 + 显著告警 + 写审计，绝不静默崩溃。

---

## 6. 多版本 / 多实例共存

多个 Unity 项目可能装不同版本 UIProbe，且同时运行：

- 端口探测从固定基址（如 58300）向上探测空闲端口，写入 EditorPrefs。
- `/health` 返回 `serverId` + `pid` + `projectPath` + `uiProbeVersion` + `contractVersion`。
- Node Orchestrator 扫端口段，按 `projectPath` 路由；**多实例必须显式选择目标**，不自动猜。
- 版本区分：路由前校验 `contractVersion`，对不兼容实例直接标记不可用并提示升级。
- 僵尸监听：靠 `pid` 存活校验剔除。

---

## 7. 升级与回退

- 升级 UIProbe：C# 包与 Node 包应同时升到匹配 MINOR；只升一侧可能触发 `VERSION_MISMATCH`。
- AI client 侧：`npx` 默认拉最新，建议在 client 配置中锁定版本，避免自动升级打断进行中的工作流。
- 回退：配置/缓存迁移是单向的；回退旧版本时，新版本写入的高 schemaVersion 配置可能不被旧版本识别 → 旧版本应"忽略未知字段 + 用默认"，而非报错。

---

## 8. 待细化（非阻塞）

- npm 包名与发布渠道（公开 npm vs 私有 registry）。
- UPM 分发渠道（OpenUPM / git URL / 私有 registry）。
- MCP SDK 版本锁定与升级节奏。
- 是否提供 `uiprobe doctor` 类自检命令报告版本/连接/配置健康度。
