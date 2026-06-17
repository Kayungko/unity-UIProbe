using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UIProbe.Core.Contract;
using UIProbe.Infrastructure.UnityAdapters;

namespace UIProbe.Core.Services
{
    /// <summary>
    /// UI 检测(只读)。统一综合检测 / 重名重复 / 过滤节点扫描为单一 Issue 模型。
    /// 严格从 PrefabIndexService.Current 取目标 prefab,经 IAssetGateway.InspectPrefab 拿中立节点模型跑规则,
    /// 不加载也不修改 prefab。逻辑层产 List&lt;Issue&gt;(契约单一来源),序列化时映射为可读报告 DTO。
    /// </summary>
    public sealed class UICheckService
    {
        // --- 规则 ID(单一来源) ---
        public const string RuleDuplicateName = "duplicate-name";
        public const string RuleMissingSprite = "missing-sprite";
        public const string RuleMissingFont = "missing-font";
        public const string RuleUnnecessaryRaycastTarget = "unnecessary-raycast-target";
        public const string RuleBadNaming = "bad-naming";
        public const string RuleEmptyText = "empty-text";
        public const string RuleFilterNode = "filter-node";

        /// <summary>默认启用规则集(不含 opt-in 的 empty-text 与依赖关键字的 filter-node)。</summary>
        public static readonly IReadOnlyList<string> DefaultEnabledRules = new[]
        {
            RuleDuplicateName,
            RuleMissingSprite,
            RuleMissingFont,
            RuleUnnecessaryRaycastTarget,
            RuleBadNaming
        };

        // 重名检测白名单:这些名称天然可重复,跳过(移植自遗留 DuplicateNameRule)。
        private static readonly HashSet<string> DuplicateWhiteList = new HashSet<string>(StringComparer.Ordinal)
        {
            "Viewport", "Content", "Scrollbar", "Sliding Area", "Handle"
        };

        // 命名规范黑名单子串(移植自遗留 BadNamingRule)。
        private static readonly string[] BadNameTokens =
        {
            "(Clone)", "(1)", "(2)", "(3)", "(4)", "(5)",
            "GameObject", "Image", "Text", "Button", "Panel"
        };

        private readonly PrefabIndexService _index;
        private readonly IAssetGateway _assets;
        private ToolResult _lastResult;

        public UICheckService(PrefabIndexService index, IAssetGateway assets)
        {
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        }

        /// <summary>
        /// 跑检测。Targets 为空→索引内全部 prefab;EnabledRules 为空→DefaultEnabledRules;
        /// FilterKeyword 非空时启用 filter-node 规则。索引未构建→ExecutionFailed;
        /// 无问题→Success + 空 Issues + 非空 Summary。Data 为 UICheckReport JSON。
        /// </summary>
        public ToolResult RunChecks(UICheckRequest request, IProgress<float> progress = null)
        {
            request = request ?? new UICheckRequest();

            PrefabIndex index = _index.Current;
            if (index == null)
            {
                return Fail(ToolErrorCodes.ExecutionFailed,
                    "PrefabIndex 未构建,请先在「预制体索引」执行 build", true);
            }

            var enabled = (request.EnabledRules != null && request.EnabledRules.Count > 0)
                ? new HashSet<string>(request.EnabledRules, StringComparer.Ordinal)
                : new HashSet<string>(DefaultEnabledRules, StringComparer.Ordinal);

            string filterKeyword = request.FilterKeyword;
            bool useFilter = !string.IsNullOrEmpty(filterKeyword);

            List<PrefabIndexItem> targets = SelectTargets(index, request.Targets);

            var issues = new List<Issue>();
            for (int i = 0; i < targets.Count; i++)
            {
                string path = targets[i].AssetPath;
                IReadOnlyList<PrefabNodeRecord> nodes = _assets.InspectPrefab(path);

                if (enabled.Contains(RuleDuplicateName)) CollectDuplicateNames(issues, path, nodes);
                foreach (PrefabNodeRecord n in nodes)
                {
                    if (enabled.Contains(RuleMissingSprite)) CheckMissingSprite(issues, path, n);
                    if (enabled.Contains(RuleMissingFont)) CheckMissingFont(issues, path, n);
                    if (enabled.Contains(RuleUnnecessaryRaycastTarget)) CheckRaycastTarget(issues, path, n);
                    if (enabled.Contains(RuleBadNaming)) CheckBadNaming(issues, path, n);
                    if (enabled.Contains(RuleEmptyText)) CheckEmptyText(issues, path, n);
                    if (useFilter) CheckFilterNode(issues, path, n, filterKeyword);
                }

                progress?.Report((float)(i + 1) / Math.Max(1, targets.Count));
            }
            if (targets.Count == 0) progress?.Report(1f);

            issues.Sort(CompareIssues);

            UICheckReport report = BuildReport(issues);
            _lastResult = new ToolResult { Status = ToolStatus.Success, Data = JsonUtility.ToJson(report) };
            return _lastResult;
        }

        /// <summary>返回最近一次 RunChecks 的缓存结果;尚未跑过→ExecutionFailed 提示先执行。</summary>
        public ToolResult GetCheckResults()
        {
            if (_lastResult == null)
            {
                return Fail(ToolErrorCodes.ExecutionFailed, "尚未执行检测,请先 RunChecks", false);
            }
            return _lastResult;
        }

        private static List<PrefabIndexItem> SelectTargets(PrefabIndex index, List<string> targets)
        {
            if (targets == null || targets.Count == 0) return index.Items.ToList();
            var wanted = new HashSet<string>(targets, StringComparer.Ordinal);
            return index.Items.Where(it => wanted.Contains(it.AssetPath)).ToList();
        }

        // ---------------- 规则实现(中立节点模型) ----------------

        private static void CollectDuplicateNames(List<Issue> issues, string path, IReadOnlyList<PrefabNodeRecord> nodes)
        {
            IEnumerable<IGrouping<string, PrefabNodeRecord>> groups = nodes
                .Where(n => !n.IsRoot && !DuplicateWhiteList.Contains(n.Name))
                .GroupBy(n => n.Name, StringComparer.Ordinal);

            foreach (var group in groups)
            {
                List<PrefabNodeRecord> members = group
                    .OrderBy(n => n.NodePath, StringComparer.Ordinal)
                    .ToList();
                if (members.Count < 2) continue;

                for (int i = 0; i < members.Count; i++)
                {
                    issues.Add(new Issue
                    {
                        Severity = Severity.Warning,
                        RuleId = RuleDuplicateName,
                        PrefabPath = path,
                        NodePath = members[i].NodePath,
                        ComponentType = string.Empty,
                        Message = "节点名称重复: '" + group.Key + "' (" + (i + 1) + "/" + members.Count + ")",
                        SuggestedFixId = "rename-node",
                        CanAutoFix = false
                    });
                }
            }
        }

        private static void CheckMissingSprite(List<Issue> issues, string path, PrefabNodeRecord n)
        {
            if (!n.HasImage || n.ImageSpriteAssigned || n.ImageColorAlpha <= 0f) return;
            issues.Add(new Issue
            {
                Severity = Severity.Warning,
                RuleId = RuleMissingSprite,
                PrefabPath = path,
                NodePath = n.NodePath,
                ComponentType = "Image",
                Message = "Image 缺少 Sprite",
                SuggestedFixId = string.Empty,
                CanAutoFix = false
            });
        }

        private static void CheckMissingFont(List<Issue> issues, string path, PrefabNodeRecord n)
        {
            if (!n.HasText || n.TextFontAssigned) return;
            issues.Add(new Issue
            {
                Severity = Severity.Error,
                RuleId = RuleMissingFont,
                PrefabPath = path,
                NodePath = n.NodePath,
                ComponentType = "Text",
                Message = "Text 缺少 Font",
                SuggestedFixId = string.Empty,
                CanAutoFix = false
            });
        }

        private static void CheckRaycastTarget(List<Issue> issues, string path, PrefabNodeRecord n)
        {
            if (!n.HasGraphic || !n.GraphicRaycastTarget || n.IsInteractable) return;
            issues.Add(new Issue
            {
                Severity = Severity.Warning,
                RuleId = RuleUnnecessaryRaycastTarget,
                PrefabPath = path,
                NodePath = n.NodePath,
                ComponentType = "Graphic",
                Message = "RaycastTarget 可关闭以省点击检测开销",
                SuggestedFixId = "disable-raycast-target",
                CanAutoFix = true
            });
        }

        private static void CheckBadNaming(List<Issue> issues, string path, PrefabNodeRecord n)
        {
            if (!BadNameTokens.Any(tok => n.Name.IndexOf(tok, StringComparison.Ordinal) >= 0)) return;
            issues.Add(new Issue
            {
                Severity = Severity.Warning,
                RuleId = RuleBadNaming,
                PrefabPath = path,
                NodePath = n.NodePath,
                ComponentType = string.Empty,
                Message = "命名不规范: '" + n.Name + "'",
                SuggestedFixId = "rename-node",
                CanAutoFix = false
            });
        }

        private static void CheckEmptyText(List<Issue> issues, string path, PrefabNodeRecord n)
        {
            if (!n.HasText || !string.IsNullOrEmpty(n.TextContent)) return;
            issues.Add(new Issue
            {
                Severity = Severity.Warning,
                RuleId = RuleEmptyText,
                PrefabPath = path,
                NodePath = n.NodePath,
                ComponentType = "Text",
                Message = "Text 内容为空",
                SuggestedFixId = string.Empty,
                CanAutoFix = false
            });
        }

        private static void CheckFilterNode(List<Issue> issues, string path, PrefabNodeRecord n, string keyword)
        {
            if (n.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) return;
            issues.Add(new Issue
            {
                Severity = Severity.Info,
                RuleId = RuleFilterNode,
                PrefabPath = path,
                NodePath = n.NodePath,
                ComponentType = string.Empty,
                Message = "匹配过滤关键字: '" + keyword + "'",
                SuggestedFixId = string.Empty,
                CanAutoFix = false
            });
        }

        // ---------------- 排序 / 摘要 / 序列化映射 ----------------

        private static int CompareIssues(Issue a, Issue b)
        {
            int c = string.CompareOrdinal(a.PrefabPath, b.PrefabPath);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.RuleId, b.RuleId);
            if (c != 0) return c;
            return string.CompareOrdinal(a.NodePath, b.NodePath);
        }

        private static UICheckReport BuildReport(List<Issue> issues)
        {
            var report = new UICheckReport();
            var byRule = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (Issue i in issues)
            {
                report.Issues.Add(new UICheckIssue
                {
                    Severity = i.Severity.ToString(),
                    RuleId = i.RuleId,
                    PrefabPath = i.PrefabPath,
                    NodePath = i.NodePath,
                    ComponentType = i.ComponentType,
                    Message = i.Message,
                    SuggestedFixId = i.SuggestedFixId,
                    CanAutoFix = i.CanAutoFix
                });

                switch (i.Severity)
                {
                    case Severity.Error: report.Summary.ErrorCount++; break;
                    case Severity.Warning: report.Summary.WarningCount++; break;
                    case Severity.Info: report.Summary.InfoCount++; break;
                }
                byRule[i.RuleId] = byRule.TryGetValue(i.RuleId, out int cnt) ? cnt + 1 : 1;
            }

            report.Summary.Total = issues.Count;
            report.Summary.ByRule = byRule
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new UICheckRuleCount { RuleId = kv.Key, Count = kv.Value })
                .ToList();
            return report;
        }

        private static ToolResult Fail(string code, string message, bool retriable) => new ToolResult
        {
            Status = ToolStatus.Failed,
            Error = new ToolError { Code = code, Message = message, Retriable = retriable }
        };
    }

    /// <summary>检测请求。Targets/EnabledRules 为空走默认;FilterKeyword 非空启用过滤节点扫描。</summary>
    public sealed class UICheckRequest
    {
        public List<string> Targets;
        public List<string> EnabledRules;
        public string FilterKeyword;
    }

    /// <summary>可序列化检测报告(JsonUtility 用,Severity 以字符串落地保金样可读)。</summary>
    [Serializable]
    public sealed class UICheckReport
    {
        public List<UICheckIssue> Issues = new List<UICheckIssue>();
        public UICheckSummary Summary = new UICheckSummary();
    }

    /// <summary>报告内单条 Issue 的可序列化投影(对应契约 Issue,Severity 转字符串)。</summary>
    [Serializable]
    public sealed class UICheckIssue
    {
        public string Severity;
        public string RuleId;
        public string PrefabPath;
        public string NodePath;
        public string ComponentType;
        public string Message;
        public string SuggestedFixId;
        public bool CanAutoFix;
    }

    /// <summary>检测摘要。Total + 按严重度计数 + 按规则计数;无问题时仍返回(全 0 + 空 ByRule)。</summary>
    [Serializable]
    public sealed class UICheckSummary
    {
        public int Total;
        public int ErrorCount;
        public int WarningCount;
        public int InfoCount;
        public List<UICheckRuleCount> ByRule = new List<UICheckRuleCount>();
    }

    /// <summary>单规则命中计数。</summary>
    [Serializable]
    public sealed class UICheckRuleCount
    {
        public string RuleId;
        public int Count;
    }
}
