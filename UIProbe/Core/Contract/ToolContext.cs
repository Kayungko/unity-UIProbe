using System;
using System.Threading;

namespace UIProbe.Core.Contract
{
    /// <summary>
    /// 长任务执行上下文：取消 / 进度 / 日志。字段以 Docs/ToolContract.md §9 为单一来源。
    /// 注：Adapter（IAssetGateway/IFileSystem/IEditorPrefs）注入接缝在 T1-3 定义后再接入，
    /// 本骨架不引用尚未存在的 Adapter 类型，保持契约层可独立编译。
    /// </summary>
    public sealed class ToolContext
    {
        public CancellationToken Cancellation;
        public IProgress<ToolProgress> Progress;   // 回调节流到 ~200ms/次
        public IToolLogger Log;                    // 结构化日志，带 CorrelationId
        public string CorrelationId;
    }

    public struct ToolProgress
    {
        public float Fraction;   // 0..1，-1 表示不确定
        public string Stage;     // "scanning" / "resolving"
        public int Done;
        public int Total;
        public string Message;
    }

    /// <summary>
    /// 结构化日志接口，输出 {ts, level, stage, message, correlationId} JSON 行，与 ReportService 同通道。
    /// </summary>
    public interface IToolLogger
    {
        void Log(string level, string stage, string message);
    }
}
