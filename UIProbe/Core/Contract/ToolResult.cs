using System.Collections.Generic;

namespace UIProbe.Core.Contract
{
    /// <summary>
    /// 统一工具结果。一套契约两处消费（UI + MCP）。
    /// 字段以 Docs/ToolContract.md §4 为单一来源；额外保留 JobId（LongRunning 轮询，Bridge M3）
    /// 与 Data（只读工具返回的 payload，v0.1 查询能力必需），均以原始 JSON 字符串承载。
    /// </summary>
    public sealed class ToolResult
    {
        public ToolStatus Status;         // 替代裸 bool
        public string Message;
        public ToolError Error;           // 含错误码，见 ToolError
        public string Data;               // 只读查询 payload（原始 JSON）
        public string OperationId;        // Preview 产出，Execute 回传
        public string UndoId;             // 可撤销操作的回退句柄
        public string JobId;              // LongRunning 时的轮询句柄
        public string ReportPath;
        public bool RequiresConfirmation; // Preview 时提示是否需确认
        public bool CanUndo;
        public List<Issue> Issues = new List<Issue>();
        public List<Change> PlannedChanges = new List<Change>();   // Preview 阶段填充
        public List<Change> AppliedChanges = new List<Change>();   // Execute 阶段填充
        public List<Risk> Risks = new List<Risk>();
        public List<LogEntry> ProgressLog = new List<LogEntry>();
    }

    public enum ToolStatus
    {
        Success,
        Cancelled,      // 协作式取消，返回已完成的 AppliedChanges + UndoId
        Interrupted,    // Domain Reload 等外部中断，可否重试由 ReloadSafe 决定
        Failed
    }

    /// <summary>风险提示项，Preview 阶段呈现给用户/AI。</summary>
    public sealed class Risk
    {
        public Severity Severity;
        public string Message;
    }

    /// <summary>结构化进度/日志行，{ts, level, stage, message, correlationId}。见 ToolContract.md §9。</summary>
    public sealed class LogEntry
    {
        public string Ts;
        public string Level;
        public string Stage;
        public string Message;
        public string CorrelationId;
    }
}
