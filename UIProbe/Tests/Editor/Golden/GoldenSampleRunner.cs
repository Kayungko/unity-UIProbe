using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace UIProbe.Tests.Editor.Golden
{
    /// <summary>
    /// 黄金样本回归设施:对模块输出录制基线快照,迁移前后逐行 diff。
    /// 这是后续 Service 抽离任务"行为零变化"的安全网。
    ///
    /// 用法:GoldenSampleRunner.AssertGolden("ui_dup", csvText, GoldenFormat.Csv);
    /// 首次运行(基线缺失)或显式刷新开关开启时写入基线并通过;
    /// 否则与基线逐行比较,不一致则 Assert.Fail 并打印首处差异 + before/after 全文。
    /// </summary>
    public static class GoldenSampleRunner
    {
        public enum GoldenFormat
        {
            Text,
            Csv,
            Json
        }

        /// <summary>
        /// 兜底刷新开关常量。改 true 可在不设环境变量时强制刷新基线;
        /// 默认 false,正常运行绝不覆盖已存在基线,避免误覆盖。
        /// </summary>
        private const bool ForceUpdateConstant = false;

        private const string UpdateEnvVar = "UIPROBE_UPDATE_GOLDEN";

        /// <summary>默认基线目录:经 [CallerFilePath] 定位本文件,推导到同级 ../GoldenSampleFixtures。不耦合 Unity 挂载点名。</summary>
        private static readonly string DefaultBaselineDir = ResolveFixturesDir();

        /// <summary>true 时写入/覆盖基线;环境变量 UIPROBE_UPDATE_GOLDEN=1 或常量任一开启即生效。</summary>
        public static bool ShouldUpdate =>
            ForceUpdateConstant ||
            string.Equals(Environment.GetEnvironmentVariable(UpdateEnvVar), "1", StringComparison.Ordinal);

        /// <summary>
        /// 断言 actual 与名为 name 的基线一致。baselineDir 为 null 时用默认夹具目录;
        /// 测试机制自测可传临时目录避免污染版本控制基线。
        /// </summary>
        public static void AssertGolden(
            string name,
            string actual,
            GoldenFormat format = GoldenFormat.Text,
            string baselineDir = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("golden 名称不能为空", nameof(name));
            }

            string dir = baselineDir ?? DefaultBaselineDir;
            string path = Path.Combine(dir, name + ExtensionFor(format));
            string normActual = Normalize(actual);

            bool exists = File.Exists(path);
            if (ShouldUpdate || !exists)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, normActual + "\n");
                Debug.Log("[Golden] " + (exists ? "刷新" : "首次写入") + "基线: " + path);
                return;
            }

            string normExpected = Normalize(File.ReadAllText(path));
            if (string.Equals(normExpected, normActual, StringComparison.Ordinal))
            {
                return;
            }

            Assert.Fail(BuildDiff(name, path, normExpected, normActual));
        }

        private static string ExtensionFor(GoldenFormat format)
        {
            switch (format)
            {
                case GoldenFormat.Csv: return ".csv";
                case GoldenFormat.Json: return ".json";
                default: return ".txt";
            }
        }

        /// <summary>统一换行为 \n 并去尾部空行,使 CRLF/LF 与末行差异不影响比较。</summary>
        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            return text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');
        }

        private static string BuildDiff(string name, string path, string expected, string actual)
        {
            string[] exp = expected.Split('\n');
            string[] act = actual.Split('\n');
            int max = Math.Max(exp.Length, act.Length);

            int firstDiff = -1;
            for (int i = 0; i < max; i++)
            {
                string e = i < exp.Length ? exp[i] : "<缺失行>";
                string a = i < act.Length ? act[i] : "<缺失行>";
                if (!string.Equals(e, a, StringComparison.Ordinal))
                {
                    firstDiff = i;
                    break;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("黄金样本不一致: " + name);
            sb.AppendLine("基线文件: " + path);
            if (firstDiff >= 0)
            {
                sb.AppendLine("首处差异 @ 第 " + (firstDiff + 1) + " 行:");
                sb.AppendLine("  expected: " + (firstDiff < exp.Length ? exp[firstDiff] : "<缺失行>"));
                sb.AppendLine("  actual  : " + (firstDiff < act.Length ? act[firstDiff] : "<缺失行>"));
            }
            sb.AppendLine("提示: 确认变更符合预期后,设 UIPROBE_UPDATE_GOLDEN=1 重跑以刷新基线。");
            sb.AppendLine("--- expected (" + exp.Length + " 行) ---");
            sb.AppendLine(expected);
            sb.AppendLine("--- actual (" + act.Length + " 行) ---");
            sb.AppendLine(actual);
            return sb.ToString();
        }

        private static string ResolveFixturesDir([CallerFilePath] string callerPath = "")
        {
            // callerPath = .../UIProbe/Tests/Editor/Golden/GoldenSampleRunner.cs
            string goldenDir = Path.GetDirectoryName(callerPath);     // .../Golden
            string editorDir = Path.GetDirectoryName(goldenDir);      // .../Editor
            return Path.Combine(editorDir, "GoldenSampleFixtures");
        }
    }
}
