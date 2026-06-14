using System;
using System.Collections.Generic;
using UnityEngine;
using UIProbe.Core.Contract;

namespace UIProbe.Core.Services
{
    /// <summary>
    /// Registry 内的注册条目:工具实例 + 其自描述 ToolDescriptor。
    /// 描述符在注册时从工具取一次并缓存,避免每次 list/describe 重复取。
    /// </summary>
    public sealed class ToolRegistration
    {
        public ToolDescriptor Descriptor;
        public IUIProbeTool Tool;
    }

    /// <summary>
    /// 写操作两阶段票据,为 v0.2 写工具预留。Preview 阶段产出 ticket(含预期 Changes),
    /// Execute 凭 ticket 落地。v0.1 只读路径不强制,本骨架仅留字段/类型占位,不实现校验逻辑。
    /// </summary>
    public sealed class OperationTicket
    {
        public string OperationId;
        public string ConfirmationToken;
        public string ExpiresAtUtc;
        public List<Change> PlannedChanges = new List<Change>();
    }

    /// <summary>
    /// 契约 §8 留白的 Run 接线落点(任务说明:Run 分派由 ToolRegistry 接线,契约骨架不实现)。
    /// Registry 只持非泛型 IUIProbeTool,编译期看不见 TParams,无法在 Registry 内做泛型
    /// 反序列化与 Phase 路由;故统一在此泛型基类承载,所有工具继承复用,消除每个工具重复写 Run。
    /// 职责仅限"把 ToolRequest 翻译成 TParams + Validate 短路 + 按 Phase 分派"。
    /// 权限 / Profile / Ticket 校验属 Registry.Invoke,不在此处。
    /// </summary>
    public abstract class ToolRunnerBase<TParams> : UIProbeTool<TParams> where TParams : class
    {
        public sealed override ToolResult Run(ToolRequest request, ToolContext ctx)
        {
            // Describe 不需要参数,直接回 schema。
            if (request.Phase == ToolPhase.Describe)
            {
                var schema = DescribeParams();
                return new ToolResult
                {
                    Status = ToolStatus.Success,
                    Data = schema != null ? schema.Json : null
                };
            }

            TParams p;
            try
            {
                p = Deserialize(request.Params);
            }
            catch (Exception ex)
            {
                return Fail(ToolErrorCodes.InvalidParams, "参数反序列化失败", ex.Message);
            }

            // Validate 失败转 Issues / INVALID_PARAMS,绝不进入 Execute,也不让异常穿透。
            ValidationResult validation = Validate(p);
            if (!validation.IsValid)
            {
                ToolResult invalid = Fail(ToolErrorCodes.InvalidParams, "参数校验失败", null);
                invalid.Issues.AddRange(validation.Issues);
                return invalid;
            }

            switch (request.Phase)
            {
                case ToolPhase.Preview:
                    return Preview(p, ctx);
                case ToolPhase.Execute:
                    return Execute(p, ctx);
                default:
                    return Fail(ToolErrorCodes.InvalidParams, "未知 ToolPhase: " + request.Phase, null);
            }
        }

        private static TParams Deserialize(string json)
        {
            // v0.1 最小实现:用 JsonUtility 承载;空参数构造默认实例。结构化 schema 校验推迟。
            if (string.IsNullOrEmpty(json))
            {
                return Activator.CreateInstance<TParams>();
            }
            return JsonUtility.FromJson<TParams>(json);
        }

        private static ToolResult Fail(string code, string message, string detail)
        {
            return new ToolResult
            {
                Status = ToolStatus.Failed,
                Message = message,
                Error = new ToolError { Code = code, Message = message, Detail = detail, Retriable = false }
            };
        }
    }
}
