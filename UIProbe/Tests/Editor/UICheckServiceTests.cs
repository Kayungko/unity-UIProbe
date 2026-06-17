using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UIProbe.Core.Contract;
using UIProbe.Core.Services;
using UIProbe.Infrastructure.UnityAdapters;
using UIProbe.Tests.Editor.Fakes;
using UIProbe.Tests.Editor.Golden;

namespace UIProbe.Tests.Editor
{
    /// <summary>
    /// 用 InMemoryAssetGateway 预置 prefab + 节点检视记录,经 PrefabIndexService 构建索引,
    /// 验证 UICheckService 在中立节点模型上跑 7 条规则并产出统一 Issue 报告(零静态 Unity 调用)。
    /// </summary>
    public sealed class UICheckServiceTests
    {
        private const string PrefabFilter = "t:Prefab";
        private const string SamplePrefab = "Assets/UI/Sample.prefab";

        private InMemoryAssetGateway _assets;
        private InMemoryFileSystem _fs;
        private InMemoryEditorPrefs _prefs;

        [SetUp]
        public void SetUp()
        {
            _assets = new InMemoryAssetGateway();
            _fs = new InMemoryFileSystem();
            _prefs = new InMemoryEditorPrefs();
        }

        private void SeedNode(string nodePath, string name, bool isRoot = false, bool interactable = false,
            bool hasImage = false, bool spriteAssigned = false, float alpha = 1f,
            bool hasText = false, bool fontAssigned = false, string textContent = "",
            bool hasGraphic = false, bool raycastTarget = false)
        {
            _assets.SeedNode(SamplePrefab, new PrefabNodeRecord
            {
                NodePath = nodePath,
                Name = name,
                IsRoot = isRoot,
                IsInteractable = interactable,
                HasImage = hasImage,
                ImageSpriteAssigned = spriteAssigned,
                ImageColorAlpha = alpha,
                HasText = hasText,
                TextFontAssigned = fontAssigned,
                TextContent = textContent,
                HasGraphic = hasGraphic,
                GraphicRaycastTarget = raycastTarget
            });
        }

        /// <summary>构建一个含 6 类问题的受控索引并返回已就绪的 UICheckService。</summary>
        private UICheckService BuildService()
        {
            _assets.Seed("guid-sample", SamplePrefab, null, PrefabFilter);

            SeedNode("Sample", "Sample", isRoot: true);
            SeedNode("Icon", "Icon", hasImage: true, spriteAssigned: false, alpha: 1f, hasGraphic: true);
            SeedNode("Title", "Title", hasText: true, fontAssigned: false, textContent: "Hi", hasGraphic: true);
            SeedNode("Bg", "Bg", hasGraphic: true, raycastTarget: true, interactable: false);
            SeedNode("Item", "Item");
            SeedNode("Group/Item", "Item");
            SeedNode("Image", "Image");

            var index = new PrefabIndexService(_assets, _fs, _prefs);
            index.BuildIndex(new PrefabIndexBuildOptions());
            return new UICheckService(index, _assets);
        }

        private static UICheckReport Parse(ToolResult result) =>
            JsonUtility.FromJson<UICheckReport>(result.Data);

        // ---------------- 规则命中 ----------------

        [Test]
        public void RunChecks_MissingSprite_EmitsIssueWithFullFields()
        {
            UICheckService svc = BuildService();

            ToolResult result = svc.RunChecks(new UICheckRequest());

            Assert.AreEqual(ToolStatus.Success, result.Status);
            UICheckReport report = Parse(result);
            UICheckIssue sprite = report.Issues.Single(i => i.RuleId == UICheckService.RuleMissingSprite);
            Assert.AreEqual("Warning", sprite.Severity);
            Assert.AreEqual("Icon", sprite.NodePath);
            Assert.AreEqual(SamplePrefab, sprite.PrefabPath);
            Assert.AreEqual("Image", sprite.ComponentType);
            Assert.IsFalse(string.IsNullOrEmpty(sprite.Message), "Issue 须带可读说明");
        }

        [Test]
        public void RunChecks_MissingFont_IsError()
        {
            UICheckService svc = BuildService();

            ToolResult result = svc.RunChecks(new UICheckRequest());

            UICheckIssue font = Parse(result).Issues.Single(i => i.RuleId == UICheckService.RuleMissingFont);
            Assert.AreEqual("Error", font.Severity, "缺失字体应为 Error");
            Assert.AreEqual("Title", font.NodePath);
        }

        [Test]
        public void RunChecks_DuplicateName_EmitsOneIssuePerDuplicateNode()
        {
            UICheckService svc = BuildService();

            ToolResult result = svc.RunChecks(new UICheckRequest());

            var dups = Parse(result).Issues.Where(i => i.RuleId == UICheckService.RuleDuplicateName).ToList();
            Assert.AreEqual(2, dups.Count, "两个同名 Item 节点各产一条");
            CollectionAssert.AreEqual(
                new[] { "Group/Item", "Item" },
                dups.Select(i => i.NodePath).ToArray(),
                "重名结果应按 NodePath ordinal 确定排列");
        }

        [Test]
        public void RunChecks_UnnecessaryRaycastTarget_CanAutoFix()
        {
            UICheckService svc = BuildService();

            ToolResult result = svc.RunChecks(new UICheckRequest());

            UICheckIssue ray = Parse(result).Issues
                .Single(i => i.RuleId == UICheckService.RuleUnnecessaryRaycastTarget);
            Assert.AreEqual("Bg", ray.NodePath);
            Assert.IsTrue(ray.CanAutoFix, "关闭 RaycastTarget 可自动修复");
            Assert.IsFalse(string.IsNullOrEmpty(ray.SuggestedFixId));
        }

        [Test]
        public void RunChecks_BadNaming_HitsImageNamedNode()
        {
            UICheckService svc = BuildService();

            ToolResult result = svc.RunChecks(new UICheckRequest());

            UICheckIssue bad = Parse(result).Issues.Single(i => i.RuleId == UICheckService.RuleBadNaming);
            Assert.AreEqual("Image", bad.NodePath);
        }

        // ---------------- Summary / 空集 / 索引未构建 ----------------

        [Test]
        public void RunChecks_Summary_CountsBySeverityAndRule()
        {
            UICheckService svc = BuildService();

            ToolResult result = svc.RunChecks(new UICheckRequest());

            UICheckSummary summary = Parse(result).Summary;
            Assert.AreEqual(6, summary.Total);
            Assert.AreEqual(1, summary.ErrorCount);
            Assert.AreEqual(5, summary.WarningCount);
            Assert.AreEqual(0, summary.InfoCount);
            Assert.AreEqual(2, summary.ByRule.Single(r => r.RuleId == UICheckService.RuleDuplicateName).Count);
        }

        [Test]
        public void RunChecks_NoIssues_ReturnsEmptyIssuesWithSummary()
        {
            _assets.Seed("guid-clean", "Assets/UI/Clean.prefab", null, PrefabFilter);
            _assets.SeedNode("Assets/UI/Clean.prefab", new PrefabNodeRecord
            {
                NodePath = "Clean", Name = "Clean", IsRoot = true
            });
            var index = new PrefabIndexService(_assets, _fs, _prefs);
            index.BuildIndex(new PrefabIndexBuildOptions());
            var svc = new UICheckService(index, _assets);

            ToolResult result = svc.RunChecks(new UICheckRequest());

            Assert.AreEqual(ToolStatus.Success, result.Status);
            UICheckReport report = Parse(result);
            Assert.AreEqual(0, report.Issues.Count, "干净 prefab 无 Issue");
            Assert.IsNotNull(report.Summary, "无问题仍须返回 Summary");
            Assert.AreEqual(0, report.Summary.Total);
        }

        [Test]
        public void RunChecks_IndexNotBuilt_ReturnsExecutionFailed()
        {
            var freshIndex = new PrefabIndexService(_assets, _fs, _prefs);
            var svc = new UICheckService(freshIndex, _assets);

            ToolResult result = svc.RunChecks(new UICheckRequest());

            Assert.AreEqual(ToolStatus.Failed, result.Status);
            Assert.IsNotNull(result.Error);
            Assert.AreEqual(ToolErrorCodes.ExecutionFailed, result.Error.Code);
        }

        // ---------------- 过滤节点扫描(作为规则) ----------------

        [Test]
        public void RunChecks_FilterKeyword_EmitsInfoPerMatchedNode()
        {
            UICheckService svc = BuildService();

            ToolResult result = svc.RunChecks(new UICheckRequest { FilterKeyword = "Item" });

            var filtered = Parse(result).Issues.Where(i => i.RuleId == UICheckService.RuleFilterNode).ToList();
            Assert.AreEqual(2, filtered.Count, "两个名含 Item 的节点各产一条 filter Issue");
            Assert.IsTrue(filtered.All(i => i.Severity == "Info"));
        }

        // ---------------- Golden 回归 ----------------

        [Test]
        public void Golden_Report_MatchesBaseline()
        {
            UICheckService svc = BuildService();

            ToolResult result = svc.RunChecks(new UICheckRequest());

            GoldenSampleRunner.AssertGolden("ui_check", result.Data, GoldenSampleRunner.GoldenFormat.Json);
        }
    }
}
