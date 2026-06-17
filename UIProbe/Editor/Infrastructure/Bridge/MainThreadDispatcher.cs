using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UIProbe.Core.Contract;

namespace UIProbe.Editor.Infrastructure.Bridge
{
    /// <summary>
    /// 把后台线程(HTTP 监听)请求投递到 Unity 主线程执行的边界。
    /// 后台线程经 Enqueue 入并发队列并 await TaskCompletionSource;
    /// 主线程经 Pump 逐帧 drain 队列、执行、写回结果(生产订阅 EditorApplication.update,测试直接驱动)。
    /// 编译/更新中暂缓写操作 drain,只读放行;LongRunning 立即返回 jobId 供轮询。
    /// </summary>
    public sealed class MainThreadDispatcher
    {
        public const int DefaultTimeoutMs = 30000;

        private readonly IEditorState _state;
        private readonly ConcurrentQueue<WorkItem> _queue = new ConcurrentQueue<WorkItem>();
        private readonly ConcurrentDictionary<string, DispatchJob> _jobs =
            new ConcurrentDictionary<string, DispatchJob>(StringComparer.Ordinal);

        public MainThreadDispatcher(IEditorState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// 后台线程入队一个返回值的工作单元,返回可 await 的 Task。
        /// kind=Write 且 IsCompiling/IsUpdating 时暂缓执行直到稳定窗口;Read 放行。
        /// timeoutMs 内未被主线程执行 → 抛 MainThreadTimeoutException(MAIN_THREAD_TIMEOUT)并尝试取消。
        /// </summary>
        public Task<T> Enqueue<T>(Func<CancellationToken, T> work, JobKind kind = JobKind.Read, int timeoutMs = DefaultTimeoutMs)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cts = new CancellationTokenSource();

            var item = new WorkItem
            {
                Kind = kind,
                Cancellation = cts,
                Execute = ct =>
                {
                    try
                    {
                        T result = work(ct);
                        tcs.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }
            };
            _queue.Enqueue(item);

            return AwaitWithTimeout(tcs, item, cts, timeoutMs);
        }

        // 在 tcs 与超时之间竞争:超时胜出则标记取消、抛 MainThreadTimeoutException。
        private static async Task<T> AwaitWithTimeout<T>(
            TaskCompletionSource<T> tcs, WorkItem item, CancellationTokenSource cts, int timeoutMs)
        {
            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (completed != tcs.Task)
            {
                item.TimedOut = true;          // Pump 见到后跳过执行
                cts.Cancel();                  // 尽量取消正在/将要执行的工作
                throw new MainThreadTimeoutException(
                    "工作单元在 " + timeoutMs + "ms 内未被主线程执行 (MAIN_THREAD_TIMEOUT)");
            }
            return await tcs.Task.ConfigureAwait(false);
        }

        /// <summary>主线程逐帧调用:drain 队列,执行符合放行条件的工作单元,写回结果/异常。</summary>
        public void Pump()
        {
            bool busy = _state.IsCompiling || _state.IsUpdating;

            // 暂缓的写操作回填到本地缓冲,drain 完再重入队,避免本帧重复处理。
            List<WorkItem> deferred = null;
            int count = _queue.Count;
            for (int i = 0; i < count && _queue.TryDequeue(out WorkItem item); i++)
            {
                if (item.TimedOut)
                {
                    continue; // 已超时,丢弃(Task 已 faulted)
                }
                if (busy && item.Kind == JobKind.Write)
                {
                    (deferred ?? (deferred = new List<WorkItem>())).Add(item);
                    continue;
                }
                item.Execute(item.Cancellation.Token);
            }

            if (deferred != null)
            {
                foreach (WorkItem held in deferred)
                {
                    _queue.Enqueue(held);
                }
            }

            PumpJobs(busy);
        }

        // 执行长任务队列中尚未跑过的 job(写语义按需扩展;v0.1 长任务视作只读分析)。
        private void PumpJobs(bool busy)
        {
            foreach (KeyValuePair<string, DispatchJob> kv in _jobs)
            {
                DispatchJob job = kv.Value;
                LongWorkItem work = job.Work;
                if (work == null || job.Status != JobStatus.Running || work.Started)
                {
                    continue;
                }
                work.Started = true;
                try
                {
                    work.Run(new JobProgress(job), work.Cancellation.Token);
                    job.Status = JobStatus.Done;
                }
                catch (OperationCanceledException)
                {
                    job.Status = JobStatus.Interrupted;
                }
                catch (Exception ex)
                {
                    job.Status = JobStatus.Failed;
                    job.Error = ex.Message;
                }
            }
        }

        /// <summary>
        /// 入队一个长任务,立即返回 jobId(不阻塞调用方);主线程后续 Pump 执行,
        /// 进度经 IProgress&lt;ToolProgress&gt; 节流上报,结果/状态存 job 表供 GetJob 轮询。
        /// reloadSafe=true(幂等只读)在 Domain Reload 中断后可由 Orchestrator 自动重发;
        /// false(写操作)中断后标 DOMAIN_RELOAD_INTERRUPTED 交人确认,不自动重试。
        /// </summary>
        public string EnqueueLong(Action<IProgress<ToolProgress>, CancellationToken> work, bool reloadSafe = true)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            string jobId = Guid.NewGuid().ToString("N");
            var job = new DispatchJob
            {
                JobId = jobId,
                Status = JobStatus.Running,
                ReloadSafe = reloadSafe,
                Work = new LongWorkItem
                {
                    Run = work,
                    Cancellation = new CancellationTokenSource()
                }
            };
            _jobs[jobId] = job;
            return jobId;
        }

        /// <summary>
        /// Domain Reload 前由 BridgeReloadHandler 调用:把所有进行中(Running)长任务取消并标 Interrupted,
        /// 返回受影响 job 供调用方按 ReloadSafe 分类(只读可自动重发 / 写交人确认)。
        /// </summary>
        public IReadOnlyList<DispatchJob> InterruptRunningJobs()
        {
            var interrupted = new List<DispatchJob>();
            foreach (KeyValuePair<string, DispatchJob> kv in _jobs)
            {
                DispatchJob job = kv.Value;
                if (job.Status != JobStatus.Running) continue;
                try { job.Work?.Cancellation?.Cancel(); } catch { /* 已释放 */ }
                job.Status = JobStatus.Interrupted;
                interrupted.Add(job);
            }
            return interrupted;
        }

        /// <summary>按 jobId 查长任务状态;未知 jobId 返回 null。</summary>
        public DispatchJob GetJob(string jobId)
        {
            if (string.IsNullOrEmpty(jobId)) return null;
            return _jobs.TryGetValue(jobId, out DispatchJob job) ? job : null;
        }

        // 把 IProgress 报告写入 job.Progress(节流策略在 v0.2 / MCP 层补;v0.1 直写最新值)。
        private sealed class JobProgress : IProgress<ToolProgress>
        {
            private readonly DispatchJob _job;
            public JobProgress(DispatchJob job) { _job = job; }
            public void Report(ToolProgress value) { _job.Progress = value; }
        }

        private sealed class WorkItem
        {
            public JobKind Kind;
            public Action<CancellationToken> Execute;
            public CancellationTokenSource Cancellation;
            public volatile bool TimedOut;
        }
    }

    /// <summary>工作单元类别:只读放行,写操作在编译/更新中暂缓。</summary>
    public enum JobKind
    {
        Read,
        Write
    }

    /// <summary>长任务状态(对应 wiki DispatchJob.Status)。</summary>
    public enum JobStatus
    {
        Running,
        Done,
        Interrupted,
        Failed
    }

    /// <summary>投递到主线程的长任务执行单元(LongRunning 轮询用)。</summary>
    public sealed class DispatchJob
    {
        public string JobId;
        public JobStatus Status;
        public ToolProgress Progress;
        public string Error;

        /// <summary>幂等只读(true)中断后可自动重发;写操作(false)中断后交人确认。</summary>
        public bool ReloadSafe = true;

        /// <summary>Domain Reload 中断后,Orchestrator 可安全自动重发此 job(仅 ReloadSafe 只读)。</summary>
        public bool AutoResendable;

        internal LongWorkItem Work;
    }

    // 长任务的待执行体(内部):首次 Pump 命中后置 Started 防重入。
    internal sealed class LongWorkItem
    {
        public Action<IProgress<ToolProgress>, CancellationToken> Run;
        public CancellationTokenSource Cancellation;
        public bool Started;
    }

    /// <summary>
    /// 主线程超时:工作单元未在期限内被 Pump 执行。Code = ToolErrorCodes.MainThreadTimeout,
    /// Bridge 层 catch 后转结构化 ToolResult。
    /// </summary>
    public sealed class MainThreadTimeoutException : Exception
    {
        public string Code => ToolErrorCodes.MainThreadTimeout;

        public MainThreadTimeoutException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Editor 状态接缝:把 EditorApplication 的编译/更新/播放标志从 Dispatcher 解耦,便于测试。
    /// 生产实现 <see cref="EditorApplicationState"/> 包 EditorApplication;测试用内存假体。
    /// </summary>
    public interface IEditorState
    {
        bool IsCompiling { get; }
        bool IsUpdating { get; }
        bool IsPlaying { get; }
    }

    /// <summary>生产 IEditorState:转发 UnityEditor.EditorApplication 标志。</summary>
    public sealed class EditorApplicationState : IEditorState
    {
        public bool IsCompiling => UnityEditor.EditorApplication.isCompiling;
        public bool IsUpdating => UnityEditor.EditorApplication.isUpdating;
        public bool IsPlaying => UnityEditor.EditorApplication.isPlaying;
    }
}
