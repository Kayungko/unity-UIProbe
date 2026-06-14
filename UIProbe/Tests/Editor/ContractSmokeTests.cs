using NUnit.Framework;
using UIProbe.Core.Contract;

namespace UIProbe.Tests.Editor
{
    public class ContractSmokeTests
    {
        [Test]
        public void ToolResult_DefaultLists_AreInitialized()
        {
            var result = new ToolResult();
            Assert.IsNotNull(result.Issues);
            Assert.IsNotNull(result.PlannedChanges);
            Assert.IsNotNull(result.AppliedChanges);
        }

        [Test]
        public void ToolErrorCodes_HaveStableStringValues()
        {
            Assert.AreEqual("INVALID_PARAMS", ToolErrorCodes.InvalidParams);
            Assert.AreEqual("TOOL_NOT_FOUND", ToolErrorCodes.ToolNotFound);
        }
    }
}
