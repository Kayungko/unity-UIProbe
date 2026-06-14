namespace UIProbe.Core.Contract
{
    /// <summary>
    /// 工具调用请求。UI / MCP / 内部 Flow 统一构造它，差异仅在传输层。
    /// 字段以 Docs/ToolContract.md §3 为单一来源；Params 在契约层以原始 JSON 字符串承载。
    /// </summary>
    public sealed class ToolRequest
    {
        public string ToolId;
        public ToolPhase Phase;           // Describe | Preview | Execute
        public string Params;             // 原始 JSON，按 ParamsSchema 校验
        public string OperationId;        // Execute 阶段回传 Preview 产出的 id
        public string ConfirmationToken;  // Execute 阶段的批准令牌
        public string SessionId;          // 权限判定 + 审计串联
        public string CorrelationId;      // 全链路 trace id（Node → Bridge → Service）
    }

    public enum ToolPhase
    {
        Describe,
        Preview,
        Execute
    }
}
