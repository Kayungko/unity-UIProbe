namespace UIProbe
{
    /// <summary>
    /// 共享配置服务：包装唯一的 UIProbeConfig 实例与 Load/Save。
    /// 各模块通过同一实例读写配置，Settings 改子配置后其他模块即时可见。
    /// </summary>
    internal sealed class ConfigService
    {
        public UIProbeConfig Config { get; private set; }

        public ConfigService()
        {
            Load();
        }

        public void Load()
        {
            Config = UIProbeConfigManager.Load() ?? UIProbeConfigManager.MigrateFromEditorPrefs();
        }

        public void Save()
        {
            if (Config != null)
            {
                UIProbeConfigManager.Save(Config);
            }
        }
    }
}
