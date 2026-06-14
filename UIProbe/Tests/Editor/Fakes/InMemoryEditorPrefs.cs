using System.Collections.Generic;
using UIProbe.Infrastructure.UnityAdapters;

namespace UIProbe.Tests.Editor.Fakes
{
    /// <summary>IEditorPrefs 的内存假体,用 Dictionary 模拟 prefs 表,不依赖 Unity 运行环境。</summary>
    public sealed class InMemoryEditorPrefs : IEditorPrefs
    {
        private readonly Dictionary<string, string> _store = new Dictionary<string, string>();

        public string GetString(string key, string defaultValue = "")
        {
            return _store.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public void SetString(string key, string value)
        {
            _store[key] = value;
        }

        public bool HasKey(string key)
        {
            return _store.ContainsKey(key);
        }

        public void DeleteKey(string key)
        {
            _store.Remove(key);
        }
    }
}
