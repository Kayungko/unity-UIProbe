namespace UIProbe.Infrastructure.UnityAdapters
{
    /// <summary>
    /// 配置读写接缝,替代业务层直接调用 UnityEditor.EditorPrefs 静态 API。
    /// </summary>
    public interface IEditorPrefs
    {
        string GetString(string key, string defaultValue = "");

        void SetString(string key, string value);

        bool HasKey(string key);

        void DeleteKey(string key);
    }
}
