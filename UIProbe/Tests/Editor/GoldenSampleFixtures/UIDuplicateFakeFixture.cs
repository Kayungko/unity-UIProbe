using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UIProbe.Core.Contract;
using UIProbe.Infrastructure.UnityAdapters;
using UIProbe.Tests.Editor.Fakes;

namespace UIProbe.Tests.Editor.Golden
{
    /// <summary>
    /// 最小可控夹具 + 确定性 fake 检测器,模拟未来 ui-check Service 的产出形态。
    /// 数据经内存假体喂入,不依赖真实工程资源;规模固定为 3 个预制,无大图 GetPixels 内存峰值。
    /// 检测结果按 PrefabPath ordinal 排序,保证录制基线稳定可复现。
    /// </summary>
    public static class UIDuplicateFakeFixture
    {
        /// <summary>seed 三个受控 prefab 的内存资源网关。</summary>
        public static InMemoryAssetGateway BuildAssets()
        {
            var assets = new InMemoryAssetGateway { MaxEntries = 16 };
            assets.Seed("guid-login", "Assets/UI/LoginButton.prefab", null, "Button");
            assets.Seed("guid-toggle", "Assets/UI/SoundToggle.prefab", null, "Toggle");
            assets.Seed("guid-title", "Assets/UI/TitleText.prefab", null, "Text");
            return assets;
        }

        /// <summary>对夹具资源跑固定规则,产出确定性 Issue 列表(按 PrefabPath 排序)。</summary>
        public static List<Issue> Detect(IAssetGateway assets)
        {
            var issues = new List<Issue>();
            foreach (string guid in assets.FindAssets(string.Empty))
            {
                string path = assets.GUIDToAssetPath(guid);
                if (path.IndexOf("Button", StringComparison.Ordinal) >= 0)
                {
                    issues.Add(Make(Severity.Info, "UIDUP_BTN", path, "按钮预制建议复用基础组件"));
                }
                else if (path.IndexOf("Toggle", StringComparison.Ordinal) >= 0)
                {
                    issues.Add(Make(Severity.Warning, "UIDUP_TGL", path, "Toggle 命名建议统一前缀"));
                }
                else if (path.IndexOf("Text", StringComparison.Ordinal) >= 0)
                {
                    issues.Add(Make(Severity.Info, "UIDUP_TXT", path, "文本节点建议挂 TMP"));
                }
            }
            return issues
                .OrderBy(i => i.PrefabPath, StringComparer.Ordinal)
                .ToList();
        }

        private static Issue Make(Severity severity, string ruleId, string prefabPath, string message)
        {
            return new Issue
            {
                Severity = severity,
                RuleId = ruleId,
                PrefabPath = prefabPath,
                NodePath = string.Empty,
                ComponentType = string.Empty,
                Message = message,
                SuggestedFixId = string.Empty,
                CanAutoFix = false
            };
        }

        // --- 三类快照导出(对应未来 export_report 的 md/csv/json) ---

        /// <summary>纯文本快照:每行 "PrefabPath\tSeverity\tRuleId"。</summary>
        public static string ToText(List<Issue> issues)
        {
            var sb = new StringBuilder();
            foreach (Issue i in issues)
            {
                sb.Append(i.PrefabPath).Append('\t')
                  .Append(i.Severity).Append('\t')
                  .Append(i.RuleId).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>CSV 快照:表头 + 每条 Issue 一行。夹具消息无逗号,无需转义。</summary>
        public static string ToCsv(List<Issue> issues)
        {
            var sb = new StringBuilder();
            sb.Append("Severity,RuleId,PrefabPath,Message\n");
            foreach (Issue i in issues)
            {
                sb.Append(i.Severity).Append(',')
                  .Append(i.RuleId).Append(',')
                  .Append(i.PrefabPath).Append(',')
                  .Append(i.Message).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>JSON 快照:pretty 输出,Severity 以字符串落地保可读。</summary>
        public static string ToJson(List<Issue> issues)
        {
            var snapshot = new IssueSnapshot
            {
                issues = issues.Select(i => new IssueRow
                {
                    severity = i.Severity.ToString(),
                    ruleId = i.RuleId,
                    prefabPath = i.PrefabPath,
                    message = i.Message
                }).ToList()
            };
            return JsonUtility.ToJson(snapshot, true);
        }

        [Serializable]
        private sealed class IssueSnapshot
        {
            public List<IssueRow> issues = new List<IssueRow>();
        }

        [Serializable]
        private sealed class IssueRow
        {
            public string severity;
            public string ruleId;
            public string prefabPath;
            public string message;
        }
    }
}
