# API 契约

> 由 wiki_gen 生成。

## API 契约

### asset-reference

- **FindReferences** `[inbound]`
  - 查询某资源被哪些 prefab/节点/组件引用。
- **ExportCsv** `[inbound]`
  - 导出引用结果为 CSV。

### authorization

- **LoadConfig** `[internal]`
  - 加载 mcp.config.toml(共享)叠加 mcp.local.toml(本地)得到生效 Profile + Mode。
- **Authorize** `[internal]`
  - 对一次工具调用做统一授权判定。
- **Audit** `[internal]`
  - 把调用与判定结果追加写审计 JSONL。

### mcp-server

- **ListTools (MCP)** `[inbound]`
  - MCP 客户端发现工具,代理 Bridge /tools/list 并走缓存。
- **CallTool (MCP)** `[inbound]`
  - MCP 客户端调用工具,翻译为 Bridge POST /rpc。
- **Handshake** `[outbound]`
  - 连接 Bridge 时校验 contractVersion/uiProbeVersion。
- **WaitForReload** `[internal]`
  - Domain Reload 期间轮询 /health 等待新 serverId 稳定后恢复。

### prefab-index

- **BuildIndex** `[inbound]`
  - 构建 prefab 索引(可增量)。
- **LoadCache** `[inbound]`
  - 加载缓存索引。
- **SaveCache** `[inbound]`
  - 保存索引缓存。
- **Search** `[inbound]`
  - 按 query 搜索 prefab。
- **GetPrefabDetail** `[inbound]`
  - 查看单个 prefab 详情(组件/引用)。

### tool-contract

- **UIProbeTool<TParams>** `[internal]`
  - 工具基类。ToolRegistry 按 Phase 调 Preview 或 Execute。
- **DescribeParams** `[internal]`
  - 返回工具参数 JSON-Schema。
- **Validate** `[internal]`
  - 语义校验,失败转 ToolResult.Issues / INVALID_PARAMS,不抛异常穿透。
- **Preview** `[internal]`
  - SupportsPreview=true 时必须重写,产出 OperationId + PlannedChanges + Risks。
- **Execute** `[internal]`
  - 凭 OperationId + ConfirmationToken 落地变更,产出 AppliedChanges + UndoId。

### tool-registry

- **Register** `[inbound]`
  - 注册一个工具(内置或项目扩展)。
- **ListTools** `[inbound]`
  - 列出当前 Profile 可见的工具(NOT_IN_PROFILE 的不出现)。
- **DescribeTool** `[inbound]`
  - 返回单个工具的完整 descriptor + 参数 schema。
- **Invoke** `[inbound]`
  - 统一执行入口:校验 schema -> 按 Phase 调 Preview/Execute -> 返回 ToolResult。

### ui-check

- **RunChecks** `[inbound]`
  - 运行检测,产出结构化报告。
- **GetCheckResults** `[inbound]`
  - 读取上次检测结果。

### unity-adapters

- **UnityAssetGateway** `[internal]`
  - IAssetGateway 的生产实现,真实调用 AssetDatabase。
- **InMemoryAssetGateway** `[internal]`
  - IAssetGateway 的测试假体,内存模拟资源表。
- **UnityFileSystem / InMemoryFileSystem** `[internal]`
  - IFileSystem 的生产 / 测试实现对。

### unity-bridge

- **GET /health** `[inbound]`
  - 返回 HealthStatus,供握手与 Domain Reload 检测。
- **POST /rpc** `[inbound]`
  - JSON-RPC 调用工具,经 Dispatcher 主线程执行。请求头携带 session token。
- **GET /tools/list, /tools/describe** `[inbound]`
  - 代理 ToolRegistry 的发现接口。
- **MainThreadDispatcher** `[internal]`
  - EditorApplication.update 回调逐帧 drain 并发队列、主线程执行、结果写回 TCS。
