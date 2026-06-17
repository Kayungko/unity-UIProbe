using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UIProbe.Core.Contract;
using UIProbe.Core.Services;
using UIProbe.Editor.Infrastructure.Bridge;

namespace UIProbe.Tests.Editor
{
    /// <summary>
    /// UIProbeBridge 端点行为验证:/health 字段、/rpc token 鉴权与主线程调用、/tools 列表与描述。
    /// HTTP 后台线程经 HttpClient(Task.Run)发请求,测试线程充当主线程驱动 Dispatcher.Pump,避免死锁。
    /// </summary>
    public sealed class BridgeEndpointTests
    {
        private static readonly HttpClient Http = new HttpClient();

        private sealed class FakeEditorState : IEditorState
        {
            public bool IsCompiling { get; set; }
            public bool IsUpdating { get; set; }
            public bool IsPlaying { get; set; }
        }

        // 只读假体工具:Run 直接返回 Success,不需 Adapter,经 Register 直接登记。
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
        private MainThreadDispatcher _lastDispatcher;
        private FakeEditorState _lastState;

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

        private UIProbeBridge NewBridge(int rpcTimeoutMs = MainThreadDispatcher.DefaultTimeoutMs)
        {
            _lastState = new FakeEditorState();
            _lastDispatcher = new MainThreadDispatcher(_lastState);
            var registry = new ToolRegistry(null, null, null);
            registry.Register(new FakeEchoTool());

            string tempDir = Path.Combine(Path.GetTempPath(), "uiprobe-bridge-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            _tempDirs.Add(tempDir);

            var options = new BridgeOptions
            {
                ProjectPath = "E:/fake/project",
                UiProbeVersion = "0.1.0",
                ContractVersion = "0.1.0",
                DiscoveryDirectory = tempDir,
                TokenDirectory = tempDir,
                RpcTimeoutMs = rpcTimeoutMs
            };

            var bridge = new UIProbeBridge(_lastDispatcher, registry, _lastState, options);
            bridge.Start();
            _bridges.Add(bridge);
            return bridge;
        }

        private string BaseUrl(UIProbeBridge bridge) => "http://127.0.0.1:" + bridge.Port;

        private static (HttpStatusCode code, string body) GetSync(string url)
        {
            using HttpResponseMessage resp = Http.GetAsync(url).GetAwaiter().GetResult();
            string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return (resp.StatusCode, body);
        }

        private static Task<(HttpStatusCode code, string body)> PostRpcAsync(string url, string json, string token)
        {
            return Task.Run(() =>
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                if (token != null) req.Headers.Add(SessionToken.HeaderName, token);
                using HttpResponseMessage resp = Http.SendAsync(req).GetAwaiter().GetResult();
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return (resp.StatusCode, body);
            });
        }

        // 在请求在途期间驱动主线程 Pump,直到 HTTP 任务完成(或超出 10s 安全上限)。
        private (HttpStatusCode code, string body) PostRpcPumping(string url, string json, string token)
        {
            Task<(HttpStatusCode, string)> task = PostRpcAsync(url, json, token);
            var sw = Stopwatch.StartNew();
            while (!task.IsCompleted && sw.ElapsedMilliseconds < 10000)
            {
                _lastDispatcher.Pump();
                Thread.Sleep(5);
            }
            return task.GetAwaiter().GetResult();
        }

        private static string RpcBody(string toolId)
        {
            return JsonUtility.ToJson(new ToolRequest
            {
                ToolId = toolId,
                Phase = ToolPhase.Execute,
                Params = "{}"
            });
        }

        [Test]
        public void Health_Returns_Ok_With_ServerId_Pid_ProjectPath()
        {
            UIProbeBridge bridge = NewBridge();
            (HttpStatusCode code, string body) = GetSync(BaseUrl(bridge) + "/health");

            Assert.That(code, Is.EqualTo(HttpStatusCode.OK));
            HealthStatus health = JsonUtility.FromJson<HealthStatus>(body);
            Assert.That(health.status, Is.EqualTo("ok"));
            Assert.That(health.serverId, Is.EqualTo(bridge.ServerId));
            Assert.That(health.serverId, Is.Not.Null.And.Not.Empty);
            Assert.That(health.pid, Is.GreaterThan(0));
            Assert.That(health.projectPath, Is.EqualTo("E:/fake/project"));
            Assert.That(health.isCompiling, Is.False);
        }

        [Test]
        public void Rpc_Without_Token_Rejected()
        {
            UIProbeBridge bridge = NewBridge();
            (HttpStatusCode code, _) = PostRpcPumping(BaseUrl(bridge) + "/rpc", RpcBody(FakeEchoTool.ToolId), token: null);
            Assert.That(code, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public void Rpc_With_Bad_Token_Rejected()
        {
            UIProbeBridge bridge = NewBridge();
            (HttpStatusCode code, _) = PostRpcPumping(BaseUrl(bridge) + "/rpc", RpcBody(FakeEchoTool.ToolId), token: "wrong-token");
            Assert.That(code, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public void Rpc_With_Token_Invokes_ReadOnly_Tool()
        {
            UIProbeBridge bridge = NewBridge();
            (HttpStatusCode code, string body) =
                PostRpcPumping(BaseUrl(bridge) + "/rpc", RpcBody(FakeEchoTool.ToolId), bridge.Token.Value);

            Assert.That(code, Is.EqualTo(HttpStatusCode.OK));
            ToolResult result = JsonUtility.FromJson<ToolResult>(body);
            Assert.That(result.Status, Is.EqualTo(ToolStatus.Success));
            Assert.That(result.Data, Does.Contain("echo"));
        }

        [Test]
        public void Rpc_Times_Out_When_Not_Pumped()
        {
            UIProbeBridge bridge = NewBridge(rpcTimeoutMs: 300);
            // 不驱动 Pump:Dispatcher 端超时 -> ToolResult 携带 MAIN_THREAD_TIMEOUT。
            (HttpStatusCode code, string body) =
                PostRpcAsync(BaseUrl(bridge) + "/rpc", RpcBody(FakeEchoTool.ToolId), bridge.Token.Value)
                    .GetAwaiter().GetResult();

            Assert.That(code, Is.EqualTo(HttpStatusCode.OK));
            ToolResult result = JsonUtility.FromJson<ToolResult>(body);
            Assert.That(result.Status, Is.EqualTo(ToolStatus.Failed));
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Error.Code, Is.EqualTo(ToolErrorCodes.MainThreadTimeout));
        }

        [Test]
        public void ToolsList_Contains_Registered_Tool()
        {
            UIProbeBridge bridge = NewBridge();
            (HttpStatusCode code, string body) = GetSync(BaseUrl(bridge) + "/tools/list");
            Assert.That(code, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain(FakeEchoTool.ToolId));
        }

        [Test]
        public void ToolsDescribe_Returns_Descriptor()
        {
            UIProbeBridge bridge = NewBridge();
            (HttpStatusCode code, string body) =
                GetSync(BaseUrl(bridge) + "/tools/describe?id=" + FakeEchoTool.ToolId);
            Assert.That(code, Is.EqualTo(HttpStatusCode.OK));
            ToolResult result = JsonUtility.FromJson<ToolResult>(body);
            Assert.That(result.Status, Is.EqualTo(ToolStatus.Success));
            Assert.That(result.Data, Does.Contain(FakeEchoTool.ToolId));
        }

        [Test]
        public void Start_Binds_Loopback_And_Writes_Discovery_File()
        {
            UIProbeBridge bridge = NewBridge();
            Assert.That(bridge.Port, Is.GreaterThan(0));
            Assert.That(File.Exists(bridge.DiscoveryFilePath), Is.True, "应写发现文件");
            string discovery = File.ReadAllText(bridge.DiscoveryFilePath);
            Assert.That(discovery, Does.Contain(bridge.Port.ToString()));
            Assert.That(discovery, Does.Contain("E:/fake/project"));
        }

        [Test]
        public void Token_Written_To_File_And_Matches()
        {
            UIProbeBridge bridge = NewBridge();
            Assert.That(bridge.Token, Is.Not.Null);
            Assert.That(File.Exists(bridge.Token.FilePath), Is.True, "令牌应写文件");
            string onDisk = File.ReadAllText(bridge.Token.FilePath).Trim();
            Assert.That(onDisk, Is.EqualTo(bridge.Token.Value));
            Assert.That(bridge.Token.Matches(bridge.Token.Value), Is.True);
            Assert.That(bridge.Token.Matches("nope"), Is.False);
        }

        [Test]
        public void Ctor_Null_Args_Throw()
        {
            var state = new FakeEditorState();
            var disp = new MainThreadDispatcher(state);
            var reg = new ToolRegistry(null, null, null);
            var opts = new BridgeOptions();
            Assert.Throws<ArgumentNullException>(() => new UIProbeBridge(null, reg, state, opts));
            Assert.Throws<ArgumentNullException>(() => new UIProbeBridge(disp, null, state, opts));
            Assert.Throws<ArgumentNullException>(() => new UIProbeBridge(disp, reg, null, opts));
            Assert.Throws<ArgumentNullException>(() => new UIProbeBridge(disp, reg, state, null));
        }
    }
}
