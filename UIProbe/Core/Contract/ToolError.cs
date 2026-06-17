using System;

namespace UIProbe.Core.Contract
{
    /// <summary>
    /// 统一错误。C# 异常 / Node 协议错误 / 审计错误共用一套码，AI 可据码决策重试/换工具。
    /// 字段以 Docs/ToolContract.md §6 为单一来源。
    /// </summary>
    [Serializable]
    public sealed class ToolError
    {
        public string Code;       // 见 ToolErrorCodes
        public string Message;    // 人类可读
        public string Detail;     // 可选，调试用
        public bool Retriable;    // AI 是否值得重试
    }

    /// <summary>
    /// 统一错误码常量。括号内为 Retriable 语义（见 ToolContract.md §6 错误码表）。
    /// </summary>
    public static class ToolErrorCodes
    {
        public const string OK = "OK";                                          // 成功
        public const string InvalidParams = "INVALID_PARAMS";                   // 否
        public const string ToolNotFound = "TOOL_NOT_FOUND";                    // 否
        public const string NotInProfile = "NOT_IN_PROFILE";                    // 否
        public const string PermissionDenied = "PERMISSION_DENIED";             // 否
        public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";     // 否（需走批准流程）
        public const string OperationExpired = "OPERATION_EXPIRED";             // 否（需重新 Preview）
        public const string UnityOffline = "UNITY_OFFLINE";                     // 是
        public const string UnityBusy = "UNITY_BUSY";                           // 是
        public const string DomainReloadInterrupted = "DOMAIN_RELOAD_INTERRUPTED"; // 视 ReloadSafe
        public const string MainThreadTimeout = "MAIN_THREAD_TIMEOUT";          // 是
        public const string ExecutionFailed = "EXECUTION_FAILED";               // 否
        public const string IoError = "IO_ERROR";                              // 视情况
    }
}
