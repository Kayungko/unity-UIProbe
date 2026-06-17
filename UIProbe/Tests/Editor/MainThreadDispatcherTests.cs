using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UIProbe.Core.Contract;
using UIProbe.Editor.Infrastructure.Bridge;

namespace UIProbe.Tests.Editor
{
    /// <summary>
    /// MainThreadDispatcher 行为验证:后台入队→主线程 Pump 执行→结果写回;超时→MAIN_THREAD_TIMEOUT;
    /// IsCompiling 时写操作暂缓只读放行;LongRunning jobId 轮询。
    /// 用可设置的 FakeEditorState 模拟编译状态;用 Task.Run 模拟 HTTP 后台线程,测试线程充当主线程驱动 Pump。
    /// </summary>
    public sealed class MainThreadDispatcherTests
    {
        private sealed class FakeEditorState : IEditorState
        {
            public bool IsCompiling { get; set; }
            public bool IsUpdating { get; set; }
            public bool IsPlaying { get; set; }
        }

        private static MainThreadDispatcher NewDispatcher(FakeEditorState state = null)
        {
            return new MainThreadDispatcher(state ?? new FakeEditorState());
        }

        [Test]
        public void Enqueue_Executes_On_Pump_And_Returns_Result()
        {
            var d = NewDispatcher();
            Task<int> task = Task.Run(() => d.Enqueue(_ => 42));

            // 入队后、Pump 前不应完成。
            Assert.That(task.Wait(50), Is.False, "Pump 前不应执行");

            // 主线程驱动一帧。
            d.Pump();

            Assert.That(task.Wait(2000), Is.True, "Pump 后应完成");
            Assert.That(task.Result, Is.EqualTo(42));
        }

        [Test]
        public void Enqueue_Propagates_Work_Exception()
        {
            var d = NewDispatcher();
            Task<int> task = Task.Run(() => d.Enqueue<int>(_ => throw new InvalidOperationException("boom")));
            Thread.Sleep(50);
            d.Pump();

            var ex = Assert.Throws<AggregateException>(() => task.Wait(2000));
            Assert.That(ex.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(ex.InnerException.Message, Is.EqualTo("boom"));
        }

        [Test]
        public void Enqueue_Times_Out_When_Never_Pumped()
        {
            var d = NewDispatcher();
            Task<int> task = Task.Run(() => d.Enqueue(_ => 7, JobKind.Read, timeoutMs: 100));

            var ex = Assert.Throws<AggregateException>(() => task.Wait(2000));
            Assert.That(ex.InnerException, Is.TypeOf<MainThreadTimeoutException>());
            Assert.That(((MainThreadTimeoutException)ex.InnerException).Code,
                Is.EqualTo(ToolErrorCodes.MainThreadTimeout));
        }

        [Test]
        public void Write_Held_While_Compiling_ReadPasses()
        {
            var state = new FakeEditorState { IsCompiling = true };
            var d = NewDispatcher(state);

            Task<string> write = Task.Run(() => d.Enqueue(_ => "W", JobKind.Write, timeoutMs: 3000));
            Task<string> read = Task.Run(() => d.Enqueue(_ => "R", JobKind.Read, timeoutMs: 3000));
            Thread.Sleep(80);

            d.Pump();
            // 只读放行,写操作暂缓。
            Assert.That(read.Wait(2000), Is.True, "只读应在编译中放行");
            Assert.That(read.Result, Is.EqualTo("R"));
            Assert.That(write.IsCompleted, Is.False, "写操作应在编译中暂缓");

            // 编译结束 + 再 Pump,写操作放行。
            state.IsCompiling = false;
            d.Pump();
            Assert.That(write.Wait(2000), Is.True, "编译结束后写操作应放行");
            Assert.That(write.Result, Is.EqualTo("W"));
        }

        [Test]
        public void EnqueueLong_Returns_JobId_Immediately_And_Pollable()
        {
            var d = NewDispatcher();
            int ran = 0;
            string jobId = d.EnqueueLong((progress, ct) =>
            {
                ran++;
                progress.Report(new ToolProgress { Fraction = 1f, Stage = "done", Done = 1, Total = 1 });
            });

            Assert.That(jobId, Is.Not.Null.And.Not.Empty, "应立即返回 jobId");
            DispatchJob before = d.GetJob(jobId);
            Assert.That(before, Is.Not.Null);
            Assert.That(before.Status, Is.EqualTo(JobStatus.Running), "Pump 前应为 Running");
            Assert.That(ran, Is.EqualTo(0), "Pump 前不应执行");

            d.Pump();

            DispatchJob after = d.GetJob(jobId);
            Assert.That(after.Status, Is.EqualTo(JobStatus.Done), "Pump 后应为 Done");
            Assert.That(after.Progress.Fraction, Is.EqualTo(1f));
            Assert.That(ran, Is.EqualTo(1));
        }

        [Test]
        public void EnqueueLong_Marks_Failed_On_Exception()
        {
            var d = NewDispatcher();
            string jobId = d.EnqueueLong((progress, ct) => throw new InvalidOperationException("long-boom"));
            d.Pump();

            DispatchJob job = d.GetJob(jobId);
            Assert.That(job.Status, Is.EqualTo(JobStatus.Failed));
            Assert.That(job.Error, Does.Contain("long-boom"));
        }

        [Test]
        public void GetJob_Unknown_Returns_Null()
        {
            var d = NewDispatcher();
            Assert.That(d.GetJob("no-such-job"), Is.Null);
        }

        [Test]
        public void Ctor_Null_State_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MainThreadDispatcher(null));
        }
    }
}
