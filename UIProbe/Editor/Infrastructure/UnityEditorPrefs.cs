using UnityEditor;
using UIProbe.Infrastructure.UnityAdapters;

namespace UIProbe.Editor.Infrastructure
{
    /// <summary>IEditorPrefs 的生产实现,真实调用 UnityEditor.EditorPrefs。</summary>
    public sealed class UnityEditorPrefs : IEditorPrefs
    {
        public string GetString(string key, string defaultValue = "")
        {
            return EditorPrefs.GetString(key, defaultValue);
        }

        public void SetString(string key, string value)
        {
            EditorPrefs.SetString(key, value);
        }

        public bool HasKey(string key)
        {
            return EditorPrefs.HasKey(key);
        }

        public void DeleteKey(string key)
        {
            EditorPrefs.DeleteKey(key);
        }
    }
}
