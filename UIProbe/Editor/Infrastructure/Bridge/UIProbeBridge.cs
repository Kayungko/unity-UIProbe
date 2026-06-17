using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UIProbe.Core.Contract;
using UIProbe.Core.Services;

namespace UIProbe.Editor.Infrastructure.Bridge
{
    /// <summary>
    /// Bridge 启动参数:projectPath / 版本 / 发现文件与令牌目录 / RPC 主线程超时。
    /// 生产由 Bridge 生命周期接线(T3-3)填真实值;测试传临时目录与短超时。
    /// </summary>
    public sealed class BridgeOptions
    {
        public string ProjectPath;
        public string UiProbeVersion;
        public string ContractVersion;
        public string DiscoveryDirectory;
        public string TokenDirectory;
        public int RpcTimeoutMs = MainThreadDispatcher.DefaultTimeoutMs;
    }

    /// <summary>
    /// Unity Editor 内的本地 HTTP JSON-RPC bridge(v0.1 仅 HTTP + loopback)。
    /// 仅绑 127.0.0.1 随机端口,后台线程收请求:GET /health 返回 HealthStatus;
    /// POST /rpc 校验 session token 后经 MainThreadDispatcher 投递主线程调 ToolRegistry.Invoke;
    /// GET /tools/list、/tools/describe 代理 ToolRegistry。端口/projectPath 写发现文件供 Orchestrator 路由。
    /// 生产的 EditorApplication.update→Pump 接线与真实 projectPath/版本注入留 T3-3 生命周期任务。
    /// </summary>
    public sealed class UIProbeBridge : IDisposable
    {
        private const string DiscoveryFileName = "uiprobe-bridge.json";

        private readonly MainThreadDispatcher _dispatcher;
        private readonly ToolRegistry _registry;
        private readonly IEditorState _editorState;
        private readonly BridgeOptions _options;

        private HttpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        public string ServerId { get; }
        public int Port { get; private set; }
        public string DiscoveryFilePath { get; private set; }
        public SessionToken Token { get; private set; }

        public UIProbeBridge(
            MainThreadDispatcher dispatcher,
            ToolRegistry registry,
            IEditorState editorState,
            BridgeOptions options)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _editorState = editorState ?? throw new ArgumentNullException(nameof(editorState));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            ServerId = Guid.NewGuid().ToString("N");
        }

        /// <summary>选空闲端口、生成令牌、绑定 loopback、写发现文件、启动后台 accept 循环。</summary>
        public void Start()
        {
            if (_running) return;

            Token = SessionToken.Create(_options.TokenDirectory ?? Path.GetTempPath());

            Port = ReserveLoopbackPort();
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
            _listener.Start();
            _running = true;

            WriteDiscoveryFile();

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "UIProbeBridge" };
            _acceptThread.Start();
        }

        /// <summary>停止监听并释放资源。</summary>
        public void Stop()
        {
            if (!_running && _listener == null) return;
            _running = false;
            try { _listener?.Stop(); } catch { /* 已停止 */ }
            try { _listener?.Close(); } catch { /* 已释放 */ }
            _listener = null;
        }

        public void Dispose()
        {
            Stop();
        }

        // 用 TcpListener 绑 :0 拿系统分配的空闲端口再释放;HttpListener 不支持 :0 自动选端口。
        private static int ReserveLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            try
            {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        private void WriteDiscoveryFile()
        {
            string dir = _options.DiscoveryDirectory ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            DiscoveryFilePath = Path.Combine(dir, DiscoveryFileName);

            var info = new DiscoveryInfo
            {
                port = Port,
                pid = CurrentPid(),
                serverId = ServerId,
                projectPath = _options.ProjectPath,
                tokenPath = Token != null ? Token.FilePath : null
            };
            File.WriteAllText(DiscoveryFilePath, JsonUtility.ToJson(info));
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch
                {
                    break; // 监听已停止
                }
                ThreadPool.QueueUserWorkItem(_ => HandleSafe(ctx));
            }
        }

        private void HandleSafe(HttpListenerContext ctx)
        {
            try
            {
                Route(ctx);
            }
            catch (Exception ex)
            {
                TryWrite(ctx, 500, "{\"error\":\"" + Escape(ex.Message) + "\"}");
            }
        }

        private void Route(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath;
            string method = ctx.Request.HttpMethod;

            if (path == "/health" && method == "GET")
            {
                HandleHealth(ctx);
            }
            else if (path == "/rpc" && method == "POST")
            {
                HandleRpc(ctx);
            }
            else if (path == "/tools/list" && method == "GET")
            {
                HandleToolsList(ctx);
            }
            else if (path == "/tools/describe" && method == "GET")
            {
                HandleToolsDescribe(ctx);
            }
            else
            {
                Write(ctx, 404, "{\"error\":\"not found\"}");
            }
        }

        private void HandleHealth(HttpListenerContext ctx)
        {
            var health = new HealthStatus
            {
                status = "ok",
                serverId = ServerId,
                pid = CurrentPid(),
                projectPath = _options.ProjectPath,
                uiProbeVersion = _options.UiProbeVersion,
                contractVersion = _options.ContractVersion,
                isCompiling = _editorState.IsCompiling,
                isUpdating = _editorState.IsUpdating,
                isPlaying = _editorState.IsPlaying
            };
            Write(ctx, 200, JsonUtility.ToJson(health));
        }

        private void HandleRpc(HttpListenerContext ctx)
        {
            string presented = ctx.Request.Headers[SessionToken.HeaderName];
            if (Token == null || !Token.Matches(presented))
            {
                Write(ctx, 401, "{\"error\":\"invalid session token\"}");
                return;
            }

            string body = ReadBody(ctx.Request);
            ToolRequest request = JsonUtility.FromJson<ToolRequest>(body);

            ToolResult result;
            try
            {
                // v0.1 只读路径,统一按 Read 投递;写工具的 JobKind 路由留后续。
                result = _dispatcher
                    .Enqueue(ct => _registry.Invoke(request), JobKind.Read, _options.RpcTimeoutMs)
                    .GetAwaiter().GetResult();
            }
            catch (MainThreadTimeoutException ex)
            {
                result = Failed(ToolErrorCodes.MainThreadTimeout, "主线程执行超时", ex.Message, retriable: true);
            }
            catch (Exception ex)
            {
                result = Failed(ToolErrorCodes.ExecutionFailed, "工具执行失败", ex.Message, retriable: false);
            }

            Write(ctx, 200, JsonUtility.ToJson(result));
        }

        private void HandleToolsList(HttpListenerContext ctx)
        {
            // 逐个 ToJson 再手工拼数组:ToolDescriptor 未标 [Serializable],嵌套进 wrapper 会被序列化成空对象;
            // 顶层 ToJson(descriptor) 可用(同 ToolRegistry.DescribeTool),故按元素拼接保全字段。
            System.Collections.Generic.List<ToolDescriptor> tools = _registry.ListTools();
            var sb = new StringBuilder();
            sb.Append("{\"tools\":[");
            for (int i = 0; i < tools.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(JsonUtility.ToJson(tools[i]));
            }
            sb.Append("]}");
            Write(ctx, 200, sb.ToString());
        }

        private void HandleToolsDescribe(HttpListenerContext ctx)
        {
            string id = ctx.Request.QueryString["id"];
            ToolResult result = _registry.DescribeTool(id);
            int code = result.Status == ToolStatus.Success ? 200 : 404;
            Write(ctx, code, JsonUtility.ToJson(result));
        }

        private static ToolResult Failed(string code, string message, string detail, bool retriable)
        {
            return new ToolResult
            {
                Status = ToolStatus.Failed,
                Message = message,
                Error = new ToolError { Code = code, Message = message, Detail = detail, Retriable = retriable }
            };
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static void Write(HttpListenerContext ctx, int statusCode, string json)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(json ?? string.Empty);
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.OutputStream.Close();
        }

        private static void TryWrite(HttpListenerContext ctx, int statusCode, string json)
        {
            try { Write(ctx, statusCode, json); } catch { /* 客户端可能已断开 */ }
        }

        private static int CurrentPid()
        {
            using (Process p = Process.GetCurrentProcess())
            {
                return p.Id;
            }
        }

        private static string Escape(string s)
        {
            return (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        [Serializable]
        private sealed class DiscoveryInfo
        {
            public int port;
            public int pid;
            public string serverId;
            public string projectPath;
            public string tokenPath;
        }
    }
}
