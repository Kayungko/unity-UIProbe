using NUnit.Framework;
using UIProbe.Tests.Editor.Fakes;

namespace UIProbe.Tests.Editor
{
    public class AdapterFakesTests
    {
        [Test]
        public void InMemoryFileSystem_WriteThenRead_RoundTrips()
        {
            var fs = new InMemoryFileSystem();
            fs.WriteAllText("a.txt", "hello");

            Assert.IsTrue(fs.Exists("a.txt"));
            Assert.AreEqual("hello", fs.ReadAllText("a.txt"));
        }

        [Test]
        public void InMemoryFileSystem_Backup_RestoresPreOverwriteContent()
        {
            var fs = new InMemoryFileSystem();
            fs.WriteAllText("a.txt", "original");

            var token = fs.Backup("a.txt");
            fs.WriteAllText("a.txt", "overwritten");
            Assert.AreEqual("overwritten", fs.ReadAllText("a.txt"));

            fs.Restore(token);
            Assert.AreEqual("original", fs.ReadAllText("a.txt"));
        }

        [Test]
        public void InMemoryAssetGateway_FindAssets_MatchesSeededGuidByFilter()
        {
            var gw = new InMemoryAssetGateway();
            gw.Seed("guid-001", "Assets/UI/Button.prefab", null, "t:Prefab", "Button");
            gw.Seed("guid-002", "Assets/UI/Icon.png", null, "t:Texture2D", "Icon");

            var hits = gw.FindAssets("Button");

            Assert.AreEqual(1, hits.Length);
            Assert.AreEqual("guid-001", hits[0]);
        }

        [Test]
        public void InMemoryEditorPrefs_SetGetHasKey_MatchContract()
        {
            var prefs = new InMemoryEditorPrefs();

            Assert.IsFalse(prefs.HasKey("k"));
            Assert.AreEqual("fallback", prefs.GetString("k", "fallback"));

            prefs.SetString("k", "v");
            Assert.IsTrue(prefs.HasKey("k"));
            Assert.AreEqual("v", prefs.GetString("k"));

            prefs.DeleteKey("k");
            Assert.IsFalse(prefs.HasKey("k"));
        }
    }
}
