using System;
using System.Collections.Generic;
using UIProbe.Core.Contract;

namespace UIProbe.Editor.Infrastructure.Bridge
{
    /// <summary>
    /// Domain Reload 优雅处理:把 Domain Reload 当常态。
    /// beforeAssemblyReload 中断进行中 job(写→DOMAIN_RELOAD_INTERRUPTED 交人确认,
    /// 只读 ReloadSafe→标记可自动重发)并关闭 HttpListener;
    /// afterAssemblyReload 重建 Bridge(新 serverId,绝不持久化)并重报 capabilities。
    /// 静态 Unity 依赖(AssemblyReloadEvents/[InitializeOnLoad])隔离在 BridgeBootstrap,
    /// 本类逻辑可单测(直接调 OnBeforeAssemblyReload/OnAfterAssemblyReload)。
    /// </summary>
    public sealed class BridgeReloadHandler
    {
        private readonly Func<UIProbeBridge> _bridgeFactory;
        private readonly MainThreadDispatcher _dispatcher;

        /// <summary>当前在用的 Bridge 实例;每次 afterAssemblyReload 被替换为新实例(新 serverId)。</summary>
        public UIProbeBridge Current { get; private set; }

        public BridgeReloadHandler(Func<UIProbeBridge> bridgeFactory, MainThreadDispatcher dispatcher)
        {
            _bridgeFactory = bridgeFactory ?? throw new ArgumentNullException(nameof(bridgeFactory));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>首次建并启动 Bridge(生产入口,经 BridgeBootstrap 调用;reload 事件订阅在 BridgeBootstrap)。</summary>
        public void Install()
        {
            OnAfterAssemblyReload();
        }

        /// <summary>reload 前:中断进行中 job 并分类,关闭 HttpListener。</summary>
        public void OnBeforeAssemblyReload()
        {
            IReadOnlyList<DispatchJob> interrupted = _dispatcher.InterruptRunningJobs();
            foreach (DispatchJob job in interrupted)
            {
                if (job.ReloadSafe)
                {
                    // 幂等只读:Orchestrator 可在重建后安全自动重发。
                    job.AutoResendable = true;
                }
                else
                {
                    // 写操作:标中断码交人确认,绝不自动重试。
                    job.Error = ToolErrorCodes.DomainReloadInterrupted;
                    job.AutoResendable = false;
                }
            }

            try { Current?.Stop(); } catch { /* best effort:重载在即,端口短暂不可用为常态 */ }
            Current = null;
        }

        /// <summary>reload 后:重建 Bridge(新 serverId)并启动,重报 capabilities。</summary>
        public void OnAfterAssemblyReload()
        {
            Current = _bridgeFactory();
            Current.Start();
        }
    }
}
