using System;

namespace UIProbe.Editor.Infrastructure.Bridge
{
    /// <summary>
    /// /health 返回体,用于握手校验与 Domain Reload 检测(serverId 变化即判定重载)。
    /// 字段名以 wiki/modules/unity-bridge.md 的 HealthStatus 表为单一来源(camelCase);
    /// JsonUtility 按字段名原样序列化,故此处刻意用 camelCase 对齐 Node 端消费契约。
    /// </summary>
    [Serializable]
    public sealed class HealthStatus
    {
        public string status;          // "ok"
        public string serverId;        // 进程内新建,绝不持久化复用
        public int pid;                // 僵尸监听靠 pid 存活校验剔除
        public string projectPath;     // Orchestrator 按此路由多实例
        public string uiProbeVersion;  // 版本握手
        public string contractVersion; // 握手校验 major
        public bool isCompiling;       // 编译中暂缓写操作
        public bool isUpdating;
        public bool isPlaying;
    }
}
