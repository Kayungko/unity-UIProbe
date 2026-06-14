namespace UIProbe.Core.Contract
{
    /// <summary>
    /// 工具自描述模型。describe_tool / list_tools 直接序列化它。
    /// 字段以 Docs/ToolContract.md §2 为单一来源。
    /// </summary>
    public sealed class ToolDescriptor
    {
        public string Id;                 // "ui_probe.search_prefabs"，命名空间前缀决定 Source
        public string Name;               // 人类可读名
        public string Description;        // 面向 AI 的说明（质量要求见 ToolContract.md §8）
        public string Category;           // "UIProbe/Index"
        public ToolSource Source;         // builtin | project | experimental（无 legacy）
        public ToolSafety Safety;
        public CapabilityProfile MinProfile;  // 该工具要求的最低能力档位
        public bool EnabledByDefault;
        public bool SupportsPreview;
        public bool SupportsUndo;
        public bool RequiresConfirmation; // 即使自动放行模式也强制确认一次
        public bool AuditRequired;
        public bool LongRunning;          // true → 走 jobId + 进度/取消通道
        public bool ReloadSafe;           // Domain Reload 中断后是否可自动重试（幂等只读为 true）
        public ToolSchema ParamsSchema;   // 参数 JSON-Schema，供 MCP tools/list 直出
        public string ContractVersion;    // 该工具契约 schema 版本（SemVer）
    }

    /// <summary>工具来源命名空间。ui_probe.* / project.* / experimental.*，无 legacy。</summary>
    public enum ToolSource
    {
        Builtin,
        Project,
        Experimental
    }

    /// <summary>
    /// 安全等级，声明工具"有多危险"。是否能跑由 Safety + CapabilityProfile + 授权模式共同决定。
    /// 见 Docs/ToolContract.md §5。
    /// </summary>
    public enum ToolSafety
    {
        ReadOnly,          // 只读查询
        PreviewOnly,       // 只生成预览，不改项目
        WriteSafe,         // 可控写入，支持回滚或影响有限
        WriteDestructive,  // 批量覆盖 / 删除 / 重命名 / 改 prefab
        MenuExecution,     // 执行 Unity 菜单
        CodeExecution,     // 执行 C# / 动态脚本
        Reflection,        // 反射访问类型或方法
        ExternalProcess,   // 启动外部进程
        ExternalNetwork,   // 访问外部网络
        Experimental       // 实验能力
    }

    /// <summary>
    /// 能力档位。Profile 卡能力上限，授权模式管放行方式（二者正交）。
    /// 枚举属契约层；具体放行策略由 authorization 模块（M5）消费。见 MCPAuthorizationModel.md。
    /// </summary>
    public enum CapabilityProfile
    {
        SafeDefault,
        TeamAutomation,
        TrustedProject,
        AdminDebug
    }

    /// <summary>
    /// 参数 JSON-Schema 的契约层表示。v0.1 以原始 JSON-Schema 字符串承载，
    /// 结构化解析推迟到 ToolRegistry（T1-4）。对应 ToolContract.md 的 ToolSchema 占位。
    /// </summary>
    public sealed class ToolSchema
    {
        public string Json;   // JSON-Schema 文档原文
    }
}
