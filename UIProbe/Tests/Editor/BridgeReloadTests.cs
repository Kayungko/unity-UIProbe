using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using NUnit.Framework;
using UIProbe.Core.Contract;
using UIProbe.Core.Services;
using UIProbe.Editor.Infrastructure.Bridge;

namespace UIProbe.Tests.Editor
{
    /// <summary>
    /// BridgeReloadHandler 的 Domain Reload 生命周期单元级验证(Editor 内难完整复现 reload,
    /// 故对回调逻辑做单测):serverId 重建后变化、进行中 job 被标 Interrupted、
    /// 写 job 中断码 DOMAIN_RELOAD_INTERRUPTED、只读 ReloadSafe job 可自动重发、重建后工具可再次发现。
    /// </summary>
    public sealed class BridgeReloadTests
    {
        private static readonly HttpClient Http = new HttpClient();

        private sealed class FakeEditorState : IEditorState
        {
            public bool IsCompiling { get; set; }
            public bool IsUpdating { get; set; }
            public bool IsPlaying { get; set; }
        }

        private sealed class FakeEchoTool : IUIProbeTool
        {
            public const string ToolId = "ui_probe.fake_echo";

            public ToolDescriptor Descriptor => new ToolDescriptor
            {
                Id = ToolId,
                Name = "Fake Echo",
                Category = "Test",
                Source = ToolSource.Builtin,
                Safety = ToolSafety.ReadOnly,
                MinProfile = CapabilityProfile.SafeDefault,
                EnabledByDefault = true,
                ContractVersion = "0.1.0"
            };

            public ToolSchema DescribeParams() => new ToolSchema { Json = "{}" };

            public ToolResult Run(ToolRequest request, ToolContext ctx) => new ToolResult
            {
                Status = ToolStatus.Success,
                Data = "{\"echo\":\"ok\"}"
            };
        }

        private readonly List<UIProbeBridge> _bridges = new List<UIProbeBridge>();
        private readonly List<string> _tempDirs = new List<string>();
        private MainThreadDispatcher _dispatcher;

        [TearDown]
        public void TearDown()
        {
            foreach (UIProbeBridge b in _bridges)
            {
                try { b.Stop(); } catch { /* best effort */ }
            }
            _bridges.Clear();
            foreach (string dir in _tempDirs)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best effort */ }
            }
            _tempDirs.Clear();
        }

        // Bridge 工厂:每次调用建一个新 UIProbeBridge(新 serverId),复用同一 dispatcher。
        private Func<UIProbeBridge> NewFactory()
        {
            var state = new FakeEditorState();
            _dispatcher = new MainThreadDispatcher(state);
            var registry = new ToolRegistry(null, null, null);
            registry.Register(new FakeEchoTool());

            return () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "uiprobe-reload-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                _tempDirs.Add(tempDir);
                var options = new BridgeOptions
                {
                    ProjectPath = "E:/fake/project",
                    UiProbeVersion = "0.1.0",
                    ContractVersion = "0.1.0",
                    DiscoveryDirectory = tempDir,
                    TokenDirectory = tempDir
                };
                var bridge = new UIProbeBridge(_dispatcher, registry, state, options);
                _bridges.Add(bridge);
                return bridge;
            };
        }

        private BridgeReloadHandler NewHandler()
        {
            return new BridgeReloadHandler(NewFactory(), _dispatcher);
        }

        [Test]
        public void Ctor_Null_Args_Throw()
        {
            var state = new FakeEditorState();
            var disp = new MainThreadDispatcher(state);
            Func<UIProbeBridge> factory = () => null;
            Assert.Throws<ArgumentNullException>(() => new BridgeReloadHandler(null, disp));
            Assert.Throws<ArgumentNullException>(() => new BridgeReloadHandler(factory, null));
        }

        [Test]
        public void ServerId_Changes_After_Reload()
        {
            BridgeReloadHandler handler = NewHandler();
            handler.OnAfterAssemblyReload();
            string before = handler.Current.ServerId;

            handler.OnBeforeAssemblyReload();
            handler.OnAfterAssemblyReload();
            string after = handler.Current.ServerId;

            Assert.That(before, Is.Not.Null.And.Not.Empty);
            Assert.That(after, Is.Not.Null.And.Not.Empty);
            Assert.That(after, Is.Not.EqualTo(before), "serverId 应每次 reload 重新生成");
        }

        [Test]
        public void Running_Job_Marked_Interrupted_On_Before_Reload()
        {
            BridgeReloadHandler handler = NewHandler();
            handler.OnAfterAssemblyReload();

            string jobId = _dispatcher.EnqueueLong((progress, ct) => Thread.Sleep(0));
            Assert.That(_dispatcher.GetJob(jobId).Status, Is.EqualTo(JobStatus.Running));

            handler.OnBeforeAssemblyReload();

            Assert.That(_dispatcher.GetJob(jobId).Status, Is.EqualTo(JobStatus.Interrupted));
        }

        [Test]
        public void Write_Job_Interrupted_Returns_DomainReloadInterrupted()
        {
            BridgeReloadHandler handler = NewHandler();
            handler.OnAfterAssemblyReload();

            string jobId = _dispatcher.EnqueueLong((progress, ct) => Thread.Sleep(0), reloadSafe: false);
            handler.OnBeforeAssemblyReload();

            DispatchJob job = _dispatcher.GetJob(jobId);
            Assert.That(job.Status, Is.EqualTo(JobStatus.Interrupted));
            Assert.That(job.Error, Is.EqualTo(ToolErrorCodes.DomainReloadInterrupted));
            Assert.That(job.AutoResendable, Is.False, "写操作不应自动重发");
        }

        [Test]
        public void ReadOnly_ReloadSafe_Job_Marked_AutoResendable()
        {
            BridgeReloadHandler handler = NewHandler();
            handler.OnAfterAssemblyReload();

            string jobId = _dispatcher.EnqueueLong((progress, ct) => Thread.Sleep(0), reloadSafe: true);
            handler.OnBeforeAssemblyReload();

            DispatchJob job = _dispatcher.GetJob(jobId);
            Assert.That(job.Status, Is.EqualTo(JobStatus.Interrupted));
            Assert.That(job.AutoResendable, Is.True, "幂等只读应标记可自动重发");
        }

        [Test]
        public void Tools_Discoverable_After_Reload()
        {
            BridgeReloadHandler handler = NewHandler();
            handler.OnAfterAssemblyReload();
            handler.OnBeforeAssemblyReload();
            handler.OnAfterAssemblyReload();

            string url = "http://127.0.0.1:" + handler.Current.Port + "/tools/list";
            using HttpResponseMessage resp = Http.GetAsync(url).GetAwaiter().GetResult();
            string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain(FakeEchoTool.ToolId), "重载后工具应可再次发现");
        }

        [Test]
        public void HttpListener_Closed_After_Before_Reload()
        {
            BridgeReloadHandler handler = NewHandler();
            handler.OnAfterAssemblyReload();
            int port = handler.Current.Port;

            handler.OnBeforeAssemblyReload();

            Assert.Throws<HttpRequestException>(() =>
            {
                using HttpResponseMessage resp =
                    Http.GetAsync("http://127.0.0.1:" + port + "/health").GetAwaiter().GetResult();
            }, "reload 前应关闭 HttpListener,端口短暂不可用");
        }
    }
}
