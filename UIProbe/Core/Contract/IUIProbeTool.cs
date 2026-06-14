using System;
using System.Collections.Generic;

namespace UIProbe.Core.Contract
{
    /// <summary>
    /// 标注一个工具类，供 ToolRegistry 反射发现并构造 ToolDescriptor。见 Docs/ToolContract.md §8。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class UIProbeToolAttribute : Attribute
    {
        public string Id;                 // 必填
        public string Name;
        public string Category;
        public ToolSafety Safety = ToolSafety.ReadOnly;
        public CapabilityProfile MinProfile = CapabilityProfile.SafeDefault;
        public bool SupportsPreview;
        public bool SupportsUndo;
        public bool RequiresConfirmation;
        public bool AuditRequired;
        public bool LongRunning;
        public bool ReloadSafe;
    }

    /// <summary>
    /// 非泛型工具句柄，供 ToolRegistry（T1-4）持有异构工具集合。
    /// 参数反序列化与 schema 校验由 Registry 统一做，再分派到泛型基类的对应阶段。
    /// </summary>
    public interface IUIProbeTool
    {
        ToolDescriptor Descriptor { get; }
        ToolSchema DescribeParams();
        ToolResult Run(ToolRequest request, ToolContext ctx);
    }

    /// <summary>
    /// 工具作者派生的泛型基类，对应 Docs/ToolContract.md §8 的四阶段方法。
    /// 只读工具可不重写 Preview（默认抛 NotSupportedException）；写工具 SupportsPreview=true 时必须重写。
    /// 具体 Run 分派（按 Phase 调 Preview/Execute、反序列化 TParams）由 ToolRegistry（T1-4）接线，
    /// 本契约骨架不实现。
    /// </summary>
    public abstract class UIProbeTool<TParams> : IUIProbeTool where TParams : class
    {
        public abstract ToolDescriptor Descriptor { get; }
        public abstract ToolSchema DescribeParams();
        protected virtual ValidationResult Validate(TParams p) => ValidationResult.Ok;
        protected virtual ToolResult Preview(TParams p, ToolContext ctx)
            => throw new NotSupportedException();   // SupportsPreview=true 时必须重写
        protected abstract ToolResult Execute(TParams p, ToolContext ctx);

        public abstract ToolResult Run(ToolRequest request, ToolContext ctx);
    }

    /// <summary>
    /// 语义校验结果。Validate 失败转 ToolResult.Issues / INVALID_PARAMS，不抛异常穿透。
    /// </summary>
    public sealed class ValidationResult
    {
        public bool IsValid;
        public List<Issue> Issues = new List<Issue>();

        public static ValidationResult Ok => new ValidationResult { IsValid = true };

        public static ValidationResult Fail(params Issue[] issues)
        {
            var r = new ValidationResult { IsValid = false };
            if (issues != null)
            {
                r.Issues.AddRange(issues);
            }
            return r;
        }
    }
}
