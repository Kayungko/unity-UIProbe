using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UIProbe.Core.Contract;
using UIProbe.Tests.Editor.Golden;
using GF = UIProbe.Tests.Editor.Golden.GoldenSampleRunner.GoldenFormat;

namespace UIProbe.Tests.Editor
{
    /// <summary>
    /// 验证黄金样本机制:temp 目录做"首次写入->二次 diff->篡改失败"闭环,
    /// 默认夹具目录验证已录制的三格式示例基线全绿。
    /// </summary>
    public sealed class GoldenSampleTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "uiprobe-golden-" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Test]
        public void RoundTrip_FirstWritesBaseline_SecondDiffPasses()
        {
            const string name = "roundtrip";
            string payload = "line-a\nline-b\nline-c";

            // 首次:基线缺失 -> 写入并通过。
            Assert.DoesNotThrow(() => GoldenSampleRunner.AssertGolden(name, payload, GF.Text, _tempDir));
            Assert.IsTrue(File.Exists(Path.Combine(_tempDir, name + ".txt")), "首次运行应写入基线文件");

            // 二次:相同输出 -> diff 通过。
            Assert.DoesNotThrow(() => GoldenSampleRunner.AssertGolden(name, payload, GF.Text, _tempDir));
        }

        [Test]
        public void TamperedOutput_FailsWithDiff()
        {
            const string name = "tamper";
            GoldenSampleRunner.AssertGolden(name, "expected-line-1\nexpected-line-2", GF.Text, _tempDir);

            var ex = Assert.Throws<AssertionException>(
                () => GoldenSampleRunner.AssertGolden(name, "expected-line-1\nTAMPERED", GF.Text, _tempDir),
                "篡改输出应使黄金样本失败");

            StringAssert.Contains("首处差异", ex.Message, "失败信息应定位首处差异");
            StringAssert.Contains("expected-line-2", ex.Message, "失败信息应含基线原值");
            StringAssert.Contains("TAMPERED", ex.Message, "失败信息应含篡改后实际值");
        }

        [Test]
        public void Sample_TextGolden_Passes()
        {
            List<Issue> issues = UIDuplicateFakeFixture.Detect(UIDuplicateFakeFixture.BuildAssets());
            Assert.DoesNotThrow(() => GoldenSampleRunner.AssertGolden("ui_dup", UIDuplicateFakeFixture.ToText(issues), GF.Text));
        }

        [Test]
        public void Sample_CsvGolden_Passes()
        {
            List<Issue> issues = UIDuplicateFakeFixture.Detect(UIDuplicateFakeFixture.BuildAssets());
            Assert.DoesNotThrow(() => GoldenSampleRunner.AssertGolden("ui_dup", UIDuplicateFakeFixture.ToCsv(issues), GF.Csv));
        }

        [Test]
        public void Sample_JsonGolden_Passes()
        {
            List<Issue> issues = UIDuplicateFakeFixture.Detect(UIDuplicateFakeFixture.BuildAssets());
            Assert.DoesNotThrow(() => GoldenSampleRunner.AssertGolden("ui_dup", UIDuplicateFakeFixture.ToJson(issues), GF.Json));
        }
    }
}
