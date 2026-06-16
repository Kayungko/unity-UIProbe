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
    /// 用 InMemoryAssetGateway 预置 prefab + 引用记录,经 PrefabIndexService 构建索引,
    /// 验证 AssetReferenceService 严格从索引派生引用查询 / CSV 导出(零静态 Unity 调用)。
    /// </summary>
    public sealed class AssetReferenceServiceTests
    {
        private const string PrefabFilter = "t:Prefab";
        private const string ReportPath = "Library/UIProbe/asset-references.csv";

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

        private void SeedPrefab(string guid, string path) => _assets.Seed(guid, path, null, PrefabFilter);

        private void SeedRef(string prefabPath, string kind, string assetPath, string guid, string node, string assetName, string extra = "")
        {
            _assets.SeedReference(prefabPath, new AssetReferenceRecord
            {
                AssetPath = assetPath,
                Guid = guid,
                NodePath = node,
                AssetName = assetName,
                Kind = kind,
                ExtraInfo = extra
            });
        }

        /// <summary>构建一个含引用的索引并返回已就绪的 AssetReferenceService。</summary>
        private AssetReferenceService BuildService()
        {
            SeedPrefab("guid-card", "Assets/UI/Card.prefab");
            SeedPrefab("guid-panel", "Assets/UI/Panel.prefab");

            SeedRef("Assets/UI/Card.prefab", "Image",
                "Assets/Art/icon_gold.png", "guid-gold", "Root/Icon", "icon_gold", "Image");
            SeedRef("Assets/UI/Card.prefab", "Material",
                "Assets/Art/wood.png", "guid-wood", "Root/Bg", "wood", "Material: BgMat (_MainTex)");
            SeedRef("Assets/UI/Panel.prefab", "Image",
                "Assets/Art/icon_gold.png", "guid-gold", "Header/Coin", "icon_gold", "Image");

            var index = new PrefabIndexService(_assets, _fs, _prefs);
            index.BuildIndex(new PrefabIndexBuildOptions());
            return new AssetReferenceService(index, _fs);
        }

        private static AssetReferenceResultSet Parse(ToolResult result) =>
            JsonUtility.FromJson<AssetReferenceResultSet>(result.Data);

        // ---------------- 维度查询 ----------------

        [Test]
        public void FindReferences_BySpriteName_ReturnsImageHits()
        {
            AssetReferenceService svc = BuildService();

            ToolResult result = svc.FindReferences(new AssetReferenceQuery { SpriteName = "icon_gold" });

            Assert.AreEqual(ToolStatus.Success, result.Status);
            AssetReferenceResultSet set = Parse(result);
            Assert.AreEqual(2, set.Results.Count, "icon_gold 被两个 prefab 的 Image 引用");
            CollectionAssert.AreEqual(
                new[] { "Assets/UI/Card.prefab", "Assets/UI/Panel.prefab" },
                set.Results.Select(r => r.PrefabPath).ToArray(),
                "结果应按 PrefabPath ordinal 确定排列");
        }

        [Test]
        public void FindReferences_ByGuid_ExactMatch()
        {
            AssetReferenceService svc = BuildService();

            ToolResult result = svc.FindReferences(new AssetReferenceQuery { Guid = "guid-wood" });

            Assert.AreEqual(ToolStatus.Success, result.Status);
            AssetReferenceResultSet set = Parse(result);
            Assert.AreEqual(1, set.Results.Count);
            Assert.AreEqual("Material", set.Results[0].ReferenceType);
            Assert.AreEqual("Assets/UI/Card.prefab", set.Results[0].PrefabPath);
        }

        [Test]
        public void FindReferences_ByReferenceTypeFilter_NarrowsKind()
        {
            AssetReferenceService svc = BuildService();

            ToolResult result = svc.FindReferences(new AssetReferenceQuery
            {
                AssetName = "icon_gold",
                ReferenceTypeFilter = "Image"
            });

            AssetReferenceResultSet set = Parse(result);
            Assert.AreEqual(2, set.Results.Count, "AssetName 命中 + 仅 Image 类型");
            Assert.IsTrue(set.Results.All(r => r.ReferenceType == "Image"));
        }

        [Test]
        public void FindReferences_Unreferenced_ReturnsEmptyNonFailed()
        {
            AssetReferenceService svc = BuildService();

            ToolResult result = svc.FindReferences(new AssetReferenceQuery { AssetName = "no-such-asset" });

            Assert.AreEqual(ToolStatus.Success, result.Status, "未被引用应为成功而非失败");
            Assert.IsNull(result.Error);
            Assert.AreEqual(0, Parse(result).Results.Count);
        }

        [Test]
        public void FindReferences_NoDimension_ReturnsInvalidParams()
        {
            AssetReferenceService svc = BuildService();

            ToolResult result = svc.FindReferences(new AssetReferenceQuery());

            Assert.AreEqual(ToolStatus.Failed, result.Status);
            Assert.IsNotNull(result.Error);
            Assert.AreEqual(ToolErrorCodes.InvalidParams, result.Error.Code);
        }

        [Test]
        public void FindReferences_IndexNotBuilt_ReturnsExecutionFailed()
        {
            var freshIndex = new PrefabIndexService(_assets, _fs, _prefs);
            var svc = new AssetReferenceService(freshIndex, _fs);

            ToolResult result = svc.FindReferences(new AssetReferenceQuery { AssetName = "icon_gold" });

            Assert.AreEqual(ToolStatus.Failed, result.Status);
            Assert.IsNotNull(result.Error);
            Assert.AreEqual(ToolErrorCodes.ExecutionFailed, result.Error.Code, "未构建索引应提示先 build");
        }

        // ---------------- ExportCsv ----------------

        [Test]
        public void ExportCsv_WritesReportPath_ReturnsPath()
        {
            AssetReferenceService svc = BuildService();

            ToolResult result = svc.ExportCsv(
                new AssetReferenceQuery { AssetName = "icon_gold" },
                new ExportCsvOptions { ReportPath = ReportPath });

            Assert.AreEqual(ToolStatus.Success, result.Status);
            Assert.AreEqual(ReportPath, result.Data);
            Assert.IsTrue(_fs.Exists(ReportPath), "ExportCsv 后受控目录应存在 CSV 文件");
        }

        // ---------------- Golden 回归 ----------------

        [Test]
        public void Golden_CsvExport_MatchesBaseline()
        {
            AssetReferenceService svc = BuildService();

            svc.ExportCsv(
                new AssetReferenceQuery { AssetName = "icon_gold" },
                new ExportCsvOptions { ReportPath = ReportPath });

            GoldenSampleRunner.AssertGolden("asset_references", _fs.ReadAllText(ReportPath), GoldenSampleRunner.GoldenFormat.Csv);
        }
    }
}
