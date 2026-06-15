using System;
using System.Linq;
using NUnit.Framework;
using UIProbe.Core.Contract;
using UIProbe.Core.Services;
using UIProbe.Infrastructure.UnityAdapters;
using UIProbe.Tests.Editor.Fakes;

namespace UIProbe.Tests.Editor
{
    /// <summary>
    /// 用内存假体 + fake 只读工具验证 ToolRegistry 的注册 / 发现 / 只读调用闭环。
    /// 每个用例各 new 一个 Registry,互不污染。
    /// </summary>
    public sealed class ToolRegistryTests
    {
        private const string ReadOnlyId = "ui_probe.fake_readonly";
        private const string AdminId = "ui_probe.fake_admin";

        private InMemoryAssetGateway _assets;
        private InMemoryFileSystem _fs;
        private InMemoryEditorPrefs _prefs;

        [SetUp]
        public void SetUp()
        {
            _assets = new InMemoryAssetGateway();
            _assets.Seed("guid-001", "Assets/Foo.prefab", null, "Button");
            _assets.Seed("guid-002", "Assets/Bar.prefab", null, "Toggle");
            _fs = new InMemoryFileSystem();
            _prefs = new InMemoryEditorPrefs();
        }

        private ToolRegistry NewRegistry() => new ToolRegistry(_assets, _fs, _prefs);

        [Test]
        public void Register_ThenListAndDescribe_FindsTool()
        {
            ToolRegistry registry = NewRegistry();
            registry.Register(new FakeReadOnlyTool(_assets, _fs, _prefs));

            Assert.IsTrue(registry.ListTools().Any(d => d.Id == ReadOnlyId),
                "ListTools 应包含已注册工具");

            ToolResult describe = registry.DescribeTool(ReadOnlyId);
            Assert.AreEqual(ToolStatus.Success, describe.Status);
            StringAssert.Contains(ReadOnlyId, describe.Data, "DescribeTool 的 Data 应含工具 Id");
        }

        [Test]
        public void Register_DuplicateId_Throws()
        {
            ToolRegistry registry = NewRegistry();
            registry.Register(new FakeReadOnlyTool(_assets, _fs, _prefs));

            Assert.Throws<InvalidOperationException>(
                () => registry.Register(new FakeReadOnlyTool(_assets, _fs, _prefs)),
                "同 Id 重复注册应抛异常");
        }

        [Test]
        public void Invoke_UnknownTool_ReturnsToolNotFound()
        {
            ToolRegistry registry = NewRegistry();

            ToolResult result = registry.Invoke(new ToolRequest { ToolId = "does.not.exist", Phase = ToolPhase.Execute });

            Assert.AreEqual(ToolStatus.Failed, result.Status);
            Assert.IsNotNull(result.Error);
            Assert.AreEqual(ToolErrorCodes.ToolNotFound, result.Error.Code);
        }

        [Test]
        public void Invoke_ValidateFails_NoExecute_StatusNotSuccess()
        {
            ToolRegistry registry = NewRegistry();
            var tool = new FakeReadOnlyTool(_assets, _fs, _prefs);
            registry.Register(tool);

            ToolResult result = registry.Invoke(new ToolRequest
            {
                ToolId = ReadOnlyId,
                Phase = ToolPhase.Execute,
                Params = "{\"query\":\"INVALID\"}"
            });

            Assert.AreNotEqual(ToolStatus.Success, result.Status);
            Assert.AreEqual(ToolErrorCodes.InvalidParams, result.Error.Code);
            Assert.IsNotEmpty(result.Issues, "Validate 失败应携带 Issues");
            Assert.IsFalse(tool.ExecuteCalled, "Validate 失败不得进入 Execute");
        }

        [Test]
        public void Invoke_ReadOnlyTool_ReturnsSuccess_NoChanges()
        {
            ToolRegistry registry = NewRegistry();
            var tool = new FakeReadOnlyTool(_assets, _fs, _prefs);
            registry.Register(tool);

            ToolResult result = registry.Invoke(new ToolRequest { ToolId = ReadOnlyId, Phase = ToolPhase.Execute });

            Assert.AreEqual(ToolStatus.Success, result.Status);
            Assert.IsTrue(tool.ExecuteCalled);
            Assert.IsEmpty(result.AppliedChanges, "只读工具不应产生 AppliedChanges");
        }

        [Test]
        public void RegisterFromAssembly_DiscoversAttributedTool_InjectsAdapters()
        {
            ToolRegistry registry = NewRegistry();

            registry.RegisterFromAssembly(typeof(ToolRegistryTests).Assembly);

            Assert.IsTrue(registry.ListTools().Any(d => d.Id == ReadOnlyId),
                "Attribute 驱动发现应注册 [UIProbeTool] 工具");

            // 经反射注入的 Adapter 须真实可用:Execute 调 FindAssets,返回种子资源数。
            ToolResult result = registry.Invoke(new ToolRequest { ToolId = ReadOnlyId, Phase = ToolPhase.Execute });
            Assert.AreEqual(ToolStatus.Success, result.Status);
            StringAssert.Contains("\"assetCount\":2", result.Data, "注入的 AssetGateway 应可用并返回种子资源数");
        }

        [Test]
        public void ListTools_FiltersByProfile()
        {
            ToolRegistry registry = NewRegistry();
            registry.Register(new FakeReadOnlyTool(_assets, _fs, _prefs)); // SafeDefault
            registry.Register(new FakeAdminTool(_assets));                 // AdminDebug

            var safe = registry.ListTools(CapabilityProfile.SafeDefault).Select(d => d.Id).ToList();
            Assert.Contains(ReadOnlyId, safe);
            Assert.IsFalse(safe.Contains(AdminId), "SafeDefault 档位应过滤掉 AdminDebug 工具");

            var admin = registry.ListTools(CapabilityProfile.AdminDebug).Select(d => d.Id).ToList();
            Assert.Contains(ReadOnlyId, admin);
            Assert.Contains(AdminId, admin);
        }

        [Test]
        public void Invoke_DescribePhase_ReturnsParamsSchema()
        {
            ToolRegistry registry = NewRegistry();
            registry.Register(new FakeReadOnlyTool(_assets, _fs, _prefs));

            ToolResult result = registry.Invoke(new ToolRequest { ToolId = ReadOnlyId, Phase = ToolPhase.Describe });

            Assert.AreEqual(ToolStatus.Success, result.Status);
            StringAssert.Contains("\"type\":\"object\"", result.Data, "Describe 分派应回 DescribeParams 的 schema");
        }

        [Test]
        public void Invoke_MalformedParams_ReturnsInvalidParams()
        {
            ToolRegistry registry = NewRegistry();
            var tool = new FakeReadOnlyTool(_assets, _fs, _prefs);
            registry.Register(tool);

            ToolResult result = registry.Invoke(new ToolRequest
            {
                ToolId = ReadOnlyId,
                Phase = ToolPhase.Execute,
                Params = "{ not valid json :::"
            });

            Assert.AreEqual(ToolStatus.Failed, result.Status);
            Assert.AreEqual(ToolErrorCodes.InvalidParams, result.Error.Code);
            Assert.IsFalse(tool.ExecuteCalled, "反序列化失败不得进入 Execute");
        }

        [Test]
        public void Invoke_UnknownPhase_ReturnsInvalidParams()
        {
            ToolRegistry registry = NewRegistry();
            var tool = new FakeReadOnlyTool(_assets, _fs, _prefs);
            registry.Register(tool);

            ToolResult result = registry.Invoke(new ToolRequest
            {
                ToolId = ReadOnlyId,
                Phase = (ToolPhase)99
            });

            Assert.AreEqual(ToolStatus.Failed, result.Status);
            Assert.AreEqual(ToolErrorCodes.InvalidParams, result.Error.Code);
            Assert.IsFalse(tool.ExecuteCalled, "未知 Phase 不得进入 Execute");
        }

        // --- fake 工具 ---

        [Serializable]
        private sealed class FakeParams
        {
            public string query;
        }

        /// <summary>带 [UIProbeTool] 的只读工具:ctor 收三 Adapter,Execute 真实调用 AssetGateway。</summary>
        [UIProbeTool(Id = ReadOnlyId, Name = "Fake ReadOnly", Category = "UIProbe/Test", Safety = ToolSafety.ReadOnly)]
        private sealed class FakeReadOnlyTool : ToolRunnerBase<FakeParams>
        {
            private readonly IAssetGateway _assets;
            public bool ExecuteCalled;

            public FakeReadOnlyTool(IAssetGateway assets, IFileSystem fs, IEditorPrefs prefs)
            {
                _assets = assets;
            }

            public override ToolDescriptor Descriptor => new ToolDescriptor
            {
                Id = ReadOnlyId,
                Name = "Fake ReadOnly",
                Category = "UIProbe/Test",
                Source = ToolSource.Experimental,
                Safety = ToolSafety.ReadOnly,
                MinProfile = CapabilityProfile.SafeDefault,
                EnabledByDefault = true,
                ReloadSafe = true,
                ContractVersion = "0.1.0"
            };

            public override ToolSchema DescribeParams() => new ToolSchema { Json = "{\"type\":\"object\"}" };

            protected override ValidationResult Validate(FakeParams p)
            {
                if (p != null && p.query == "INVALID")
                {
                    return ValidationResult.Fail(new Issue
                    {
                        Severity = Severity.Error,
                        RuleId = "FAKE_001",
                        Message = "query 非法"
                    });
                }
                return ValidationResult.Ok;
            }

            protected override ToolResult Execute(FakeParams p, ToolContext ctx)
            {
                ExecuteCalled = true;
                string[] guids = _assets.FindAssets(string.Empty);
                return new ToolResult
                {
                    Status = ToolStatus.Success,
                    Data = "{\"assetCount\":" + guids.Length + "}"
                };
            }
        }

        /// <summary>无 Attribute、高 Profile 的只读工具,仅用于 profile 过滤用例(手动注册)。</summary>
        private sealed class FakeAdminTool : ToolRunnerBase<FakeParams>
        {
            public FakeAdminTool(IAssetGateway assets) { }

            public override ToolDescriptor Descriptor => new ToolDescriptor
            {
                Id = AdminId,
                Name = "Fake Admin",
                Category = "UIProbe/Test",
                Source = ToolSource.Experimental,
                Safety = ToolSafety.ReadOnly,
                MinProfile = CapabilityProfile.AdminDebug,
                ContractVersion = "0.1.0"
            };

            public override ToolSchema DescribeParams() => new ToolSchema { Json = "{\"type\":\"object\"}" };

            protected override ToolResult Execute(FakeParams p, ToolContext ctx)
                => new ToolResult { Status = ToolStatus.Success };
        }
    }
}
