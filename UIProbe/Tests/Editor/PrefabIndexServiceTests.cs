using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UIProbe.Core.Contract;
using UIProbe.Core.Services;
using UIProbe.Tests.Editor.Fakes;
using UIProbe.Tests.Editor.Golden;

namespace UIProbe.Tests.Editor
{
    /// <summary>
    /// 用 InMemoryAssetGateway/InMemoryFileSystem 验证 PrefabIndexService 的
    /// BuildIndex/LoadCache/SaveCache/Search/GetPrefabDetail 只读闭环 + 黄金样本回归。
    /// 全程零静态 AssetDatabase 调用,证明经接缝可脱离 Unity 运行。
    /// </summary>
    public sealed class PrefabIndexServiceTests
    {
        private const string PrefabFilter = "t:Prefab";
        private const string CachePath = "Library/UIProbe/prefab-index.json";

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

        private PrefabIndexService NewService() => new PrefabIndexService(_assets, _fs, _prefs);

        /// <summary>seed 一个会被 FindAssets("t:Prefab") 命中的 prefab 资源。</summary>
        private void SeedPrefab(string guid, string path) => _assets.Seed(guid, path, null, PrefabFilter);

        private sealed class ProgressRecorder : System.IProgress<float>
        {
            public readonly List<float> Values = new List<float>();
            public void Report(float value) => Values.Add(value);
        }

        // ---------------- BuildIndex ----------------

        [Test]
        public void BuildIndex_SeededPrefabs_ItemsSortedByAssetPathOrdinal()
        {
            SeedPrefab("g-zeta", "Assets/UI/Zeta.prefab");
            SeedPrefab("g-alpha", "Assets/UI/Alpha.prefab");
            SeedPrefab("g-beta", "Assets/UI/Beta.prefab");

            PrefabIndex index = NewService().BuildIndex(new PrefabIndexBuildOptions());

            CollectionAssert.AreEqual(
                new[] { "Assets/UI/Alpha.prefab", "Assets/UI/Beta.prefab", "Assets/UI/Zeta.prefab" },
                index.Items.Select(i => i.AssetPath).ToArray(),
                "Items 应按 AssetPath ordinal 升序确定排列");
            Assert.AreEqual(PrefabIndexService.SchemaVersion, index.SchemaVersion, "索引应打上当前 SchemaVersion");
            Assert.AreEqual("Alpha", index.Items[0].Name, "Name 应为不含扩展名的文件名");
            Assert.AreEqual("Assets/UI", index.Items[0].FolderPath, "FolderPath 应为正斜杠目录");
        }

        [Test]
        public void BuildIndex_EmptyRoot_ReturnsEmptyIndex_NoThrow()
        {
            SeedPrefab("g-1", "Assets/UI/Foo.prefab");

            PrefabIndex index = NewService().BuildIndex(
                new PrefabIndexBuildOptions { RootFolders = new[] { "Assets/Empty" } });

            Assert.AreEqual(0, index.Items.Count, "RootFolders 不命中任何 prefab 时索引为空");
        }

        [Test]
        public void BuildIndex_ReportsMonotonicProgressEndingAtOne()
        {
            SeedPrefab("g-a", "Assets/UI/A.prefab");
            SeedPrefab("g-b", "Assets/UI/B.prefab");
            SeedPrefab("g-c", "Assets/UI/C.prefab");
            var rec = new ProgressRecorder();

            NewService().BuildIndex(new PrefabIndexBuildOptions(), rec);

            Assert.IsNotEmpty(rec.Values, "应有进度上报");
            for (int i = 1; i < rec.Values.Count; i++)
            {
                Assert.GreaterOrEqual(rec.Values[i], rec.Values[i - 1], "进度须单调非降");
            }
            Assert.AreEqual(1f, rec.Values.Last(), 1e-6, "终值应为 1.0");
        }

        [Test]
        public void BuildIndex_Incremental_PreservesUnchangedItemInstances()
        {
            SeedPrefab("g-alpha", "Assets/UI/Alpha.prefab");
            SeedPrefab("g-beta", "Assets/UI/Beta.prefab");
            PrefabIndexService svc = NewService();

            PrefabIndex first = svc.BuildIndex(new PrefabIndexBuildOptions());
            PrefabIndexItem alphaBefore = first.Items.Single(i => i.Guid == "g-alpha");
            PrefabIndexItem betaBefore = first.Items.Single(i => i.Guid == "g-beta");

            SeedPrefab("g-gamma", "Assets/UI/Gamma.prefab");
            PrefabIndex second = svc.BuildIndex(new PrefabIndexBuildOptions { Incremental = true });

            Assert.AreEqual(3, second.Items.Count, "增量后应含新出现的 prefab");
            Assert.AreSame(alphaBefore, second.Items.Single(i => i.Guid == "g-alpha"),
                "未变更项应保留原实例(增量复用)");
            Assert.AreSame(betaBefore, second.Items.Single(i => i.Guid == "g-beta"),
                "未变更项应保留原实例(增量复用)");
            Assert.IsTrue(second.Items.Any(i => i.Guid == "g-gamma"), "新项应被加入");
        }

        // ---------------- LoadCache / SaveCache ----------------

        [Test]
        public void SaveThenLoadCache_RoundTrips()
        {
            SeedPrefab("g-a", "Assets/UI/A.prefab");
            SeedPrefab("g-b", "Assets/UI/B.prefab");
            PrefabIndexService svc = NewService();
            PrefabIndex built = svc.BuildIndex(new PrefabIndexBuildOptions());

            svc.SaveCache(built, CachePath);
            LoadCacheResult loaded = svc.LoadCache(CachePath);

            Assert.IsTrue(loaded.Found, "已写入的缓存应被 LoadCache 命中");
            Assert.IsTrue(loaded.SchemaValid, "同版本缓存应有效");
            Assert.AreEqual(PrefabIndexService.SchemaVersion, loaded.Index.SchemaVersion);
            CollectionAssert.AreEqual(
                built.Items.Select(i => i.AssetPath).ToArray(),
                loaded.Index.Items.Select(i => i.AssetPath).ToArray(),
                "回读的 Items 应与写入一致");
        }

        [Test]
        public void LoadCache_SchemaMismatch_ReportsInvalid()
        {
            _fs.Seed(CachePath, "{\"SchemaVersion\":0,\"BuiltAt\":\"\",\"RootPath\":\"\",\"Items\":[]}");

            LoadCacheResult loaded = NewService().LoadCache(CachePath);

            Assert.IsTrue(loaded.Found, "文件存在应判为 Found");
            Assert.IsFalse(loaded.SchemaValid, "版本不符应判为失效(触发调用方重建)");
        }

        [Test]
        public void LoadCache_Missing_ReturnsNotFound_NoThrow()
        {
            LoadCacheResult loaded = NewService().LoadCache("Library/UIProbe/does-not-exist.json");

            Assert.IsFalse(loaded.Found, "文件不存在应判为未命中");
            Assert.DoesNotThrow(() => NewService().LoadCache("Library/UIProbe/does-not-exist.json"),
                "缺失缓存不得抛 IO 异常");
        }

        [Test]
        public void SaveCache_WritesFile_Exists()
        {
            PrefabIndex built = NewService().BuildIndex(new PrefabIndexBuildOptions());

            NewService().SaveCache(built, CachePath);

            Assert.IsTrue(_fs.Exists(CachePath), "SaveCache 后文件应存在");
        }

        [Test]
        public void SaveCache_OverwriteExisting_BacksUpBeforeOverwrite()
        {
            SeedPrefab("g-a", "Assets/UI/A.prefab");
            PrefabIndexService svc = NewService();

            string firstToken = svc.SaveCache(svc.BuildIndex(new PrefabIndexBuildOptions()), CachePath);
            string contentAfterFirst = _fs.ReadAllText(CachePath);

            // 第二次写入不同内容(空索引),应在覆盖前备份
            string secondToken = svc.SaveCache(new PrefabIndex { SchemaVersion = PrefabIndexService.SchemaVersion }, CachePath);

            Assert.IsEmpty(firstToken, "首次写入(无既有文件)无需备份,返回空令牌");
            Assert.IsNotEmpty(secondToken, "覆盖既有缓存应返回还原令牌");
            Assert.AreNotEqual(contentAfterFirst, _fs.ReadAllText(CachePath), "第二次写入应改变内容");
            _fs.Restore(secondToken);
            Assert.AreEqual(contentAfterFirst, _fs.ReadAllText(CachePath), "还原令牌应恢复覆盖前内容");
        }

        // ---------------- Search ----------------

        [Test]
        public void Search_SubstringMatch_ReturnsStableSubset()
        {
            SeedPrefab("g-alpha", "Assets/UI/Alpha.prefab");
            SeedPrefab("g-beta", "Assets/UI/Beta.prefab");
            SeedPrefab("g-gamma", "Assets/UI/Gamma.prefab");
            PrefabIndexService svc = NewService();
            svc.BuildIndex(new PrefabIndexBuildOptions());

            IReadOnlyList<PrefabIndexItem> hits = svc.Search("Alpha");

            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual("g-alpha", hits[0].Guid);
        }

        [Test]
        public void Search_EmptyQuery_ReturnsAll()
        {
            SeedPrefab("g-a", "Assets/UI/A.prefab");
            SeedPrefab("g-b", "Assets/UI/B.prefab");
            PrefabIndexService svc = NewService();
            svc.BuildIndex(new PrefabIndexBuildOptions());

            Assert.AreEqual(2, svc.Search(string.Empty).Count, "空 query 应返回全部");
        }

        [Test]
        public void Search_NoHit_ReturnsEmptyNonNull()
        {
            SeedPrefab("g-a", "Assets/UI/A.prefab");
            PrefabIndexService svc = NewService();
            svc.BuildIndex(new PrefabIndexBuildOptions());

            IReadOnlyList<PrefabIndexItem> hits = svc.Search("zzz-no-such");

            Assert.IsNotNull(hits, "无命中应返回空列表而非 null");
            Assert.AreEqual(0, hits.Count);
        }

        // ---------------- GetPrefabDetail ----------------

        [Test]
        public void GetPrefabDetail_Existing_ReturnsDetail()
        {
            SeedPrefab("g-alpha", "Assets/UI/Alpha.prefab");
            PrefabIndexService svc = NewService();
            svc.BuildIndex(new PrefabIndexBuildOptions());

            ToolResult byGuid = svc.GetPrefabDetail("g-alpha");
            ToolResult byPath = svc.GetPrefabDetail("Assets/UI/Alpha.prefab");

            Assert.AreEqual(ToolStatus.Success, byGuid.Status);
            StringAssert.Contains("g-alpha", byGuid.Data, "详情 Data 应含 GUID");
            Assert.AreEqual(ToolStatus.Success, byPath.Status, "应支持按路径查询");
        }

        [Test]
        public void GetPrefabDetail_Missing_ReturnsToolNotFound()
        {
            PrefabIndexService svc = NewService();
            svc.BuildIndex(new PrefabIndexBuildOptions());

            ToolResult result = svc.GetPrefabDetail("g-missing");

            Assert.AreEqual(ToolStatus.Failed, result.Status);
            Assert.IsNotNull(result.Error);
            Assert.AreEqual(ToolErrorCodes.ToolNotFound, result.Error.Code);
        }

        // ---------------- Golden 回归 ----------------

        [Test]
        public void Golden_IndexExport_MatchesBaseline_AllFormats()
        {
            // 固定 guid+path,确保导出确定性
            SeedPrefab("guid-card", "Assets/UI/Card.prefab");
            SeedPrefab("guid-button", "Assets/UI/Common/Button.prefab");
            SeedPrefab("guid-panel", "Assets/UI/Panel.prefab");

            PrefabIndex index = NewService().BuildIndex(new PrefabIndexBuildOptions());
            index.BuiltAt = string.Empty; // 抹掉时间戳,避免非确定性

            GoldenSampleRunner.AssertGolden("prefab_index", ToText(index), GoldenSampleRunner.GoldenFormat.Text);
            GoldenSampleRunner.AssertGolden("prefab_index", ToCsv(index), GoldenSampleRunner.GoldenFormat.Csv);
            GoldenSampleRunner.AssertGolden("prefab_index", ToJson(index), GoldenSampleRunner.GoldenFormat.Json);
        }

        private static string ToText(PrefabIndex index)
        {
            var sb = new StringBuilder();
            foreach (PrefabIndexItem item in index.Items)
            {
                sb.AppendLine($"{item.AssetPath}\t{item.Guid}\t{item.Name}\t{item.FolderPath}\trefs={item.ReferencedAssets.Count}");
            }
            return sb.ToString();
        }

        private static string ToCsv(PrefabIndex index)
        {
            var sb = new StringBuilder();
            sb.AppendLine("AssetPath,Guid,Name,FolderPath,RefCount");
            foreach (PrefabIndexItem item in index.Items)
            {
                sb.AppendLine($"{item.AssetPath},{item.Guid},{item.Name},{item.FolderPath},{item.ReferencedAssets.Count}");
            }
            return sb.ToString();
        }

        private static string ToJson(PrefabIndex index) => JsonUtility.ToJson(index, true);
    }
}
