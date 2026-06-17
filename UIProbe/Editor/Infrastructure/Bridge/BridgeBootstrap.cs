using System.IO;
using UnityEditor;
using UnityEngine;
using UIProbe.Core.Services;

namespace UIProbe.Editor.Infrastructure.Bridge
{
    /// <summary>
    /// 生产入口:把静态 Unity 依赖(([InitializeOnLoad] 每次 domain load 跑、AssemblyReloadEvents、
    /// EditorApplication.update)集中在此,使 BridgeReloadHandler 逻辑可单测。
    /// 每次 domain load 构建 Dispatcher + 注册表 + Bridge 工厂,经 BridgeReloadHandler 重建 Bridge(新 serverId);
    /// 订阅 reload 事件做优雅中断/重建,并接线 update→Pump(兑现 T3-2 推迟的逐帧 drain 接线)。
    /// batchmode(CI/测试)下不自启,避免占端口与发现文件副作用;测试直接驱动 handler/dispatcher。
    /// </summary>
    [InitializeOnLoad]
    public static class BridgeBootstrap
    {
        private const string UiProbeVersion = "0.1.0";
        private const string ContractVersion = "0.1.0";

        private static readonly MainThreadDispatcher Dispatcher;
        private static readonly BridgeReloadHandler Handler;

        static BridgeBootstrap()
        {
            // 批处理(测试/CI)不自启:Bridge 仅服务交互式 Editor,避免测试期占端口/写发现文件。
            if (Application.isBatchMode) return;

            var state = new EditorApplicationState();
            Dispatcher = new MainThreadDispatcher(state);

            // v0.1 薄注册表:只读工具接线随 MCP MVP 推进补全,此处先空注册保 /tools 可达。
            var registry = new ToolRegistry(null, null, null);

            string projectPath = Path.GetDirectoryName(Application.dataPath);
            Handler = new BridgeReloadHandler(() => new UIProbeBridge(Dispatcher, registry, state, new BridgeOptions
            {
                ProjectPath = projectPath,
                UiProbeVersion = UiProbeVersion,
                ContractVersion = ContractVersion
            }), Dispatcher);

            // 逐帧 drain 主线程队列(T3-2 推迟的生产接线)。
            EditorApplication.update += Dispatcher.Pump;

            AssemblyReloadEvents.beforeAssemblyReload += Handler.OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += Handler.OnAfterAssemblyReload;

            // 当前 domain 首次启动(后续每次 reload 由 afterAssemblyReload 重建)。
            Handler.Install();
        }
    }
}
