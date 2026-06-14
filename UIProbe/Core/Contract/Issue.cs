namespace UIProbe.Core.Contract
{
    /// <summary>
    /// 结构化问题项。UI 检测的差异化能力以此承载，可被 UI 与 MCP 同样消费。
    /// 字段以 Docs/ToolContract.md §10 为单一来源。
    /// </summary>
    public sealed class Issue
    {
        public Severity Severity;     // Error | Warning | Info
        public string RuleId;
        public string PrefabPath;
        public string NodePath;
        public string ComponentType;
        public string Message;
        public string SuggestedFixId;
        public bool CanAutoFix;
    }

    public enum Severity
    {
        Error,
        Warning,
        Info
    }
}
