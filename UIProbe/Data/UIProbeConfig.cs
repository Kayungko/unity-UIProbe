using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UIProbe
{
    /// <summary>
    /// 索引器配置
    /// </summary>
    [Serializable]
    public class IndexerConfig
    {
        public string rootPath = "";
        public string[] bookmarks = new string[0];
        public string[] searchHistory = new string[0];
    }
    
    /// <summary>
    /// 拾取器配置
    /// </summary>
    [Serializable]
    public class PickerConfig
    {
        public bool autoMode = false;
    }
    
    /// <summary>
    /// 重名检测配置
    /// </summary>
    [Serializable]
    public class DuplicateCheckerConfig
    {
        public bool checkUIElements = true;
        public bool checkComponents = true;
        public string[] excludedFolders = new string[0];
        
        // Settings from DuplicateDetectionSettings
        public string mode = "Smart"; // DetectionMode enum as string
        public string detectionScope = "Global"; // DuplicateDetectionMode enum as string
        
        public bool enableWhitelist = true;
        public string[] allowedDuplicateNames = new string[] {
            "Viewport", "Content", "Scrollbar", "Scrollbar Horizontal", 
            "Scrollbar Vertical", "Sliding Area", "Handle"
        };
        
        public bool checkUGUIComponentNames = true;
        public string[] uguiComponentsToCheck = new string[] {
            "Image", "Text", "Button", "Toggle"
        };
        
        public bool enablePrefixFilter = false;
        public string[] requiredPrefixes = new string[] {
            "c_", "m_"
        };
        
        // Blacklist (Forbidden)
        public string[] forbiddenDuplicateNames = new string[0];
    }
    
    /// <summary>
    /// 图片规范化工具配置
    /// </summary>
    [Serializable]
    public class ImageNormalizerConfig
    {
        public string lastSourceFolder = "";
        public bool includeSubfolders = true;
        public int targetWidth = 512;
        public int targetHeight = 512;
        public bool forceSquare = true;
        public string alignment = "Center";  // Center 或 KeepOriginal
        public bool overwrite = true;
        public string namingSuffix = "_normalized";
    }
    
    /// <summary>
    /// 记录器配置
    /// </summary>
    [Serializable]
    public class RecorderConfig
    {
        public string storagePath = "";
    }
    
    /// <summary>
    /// UIProbe 统一配置
    /// </summary>
    [Serializable]
    public class UIProbeConfig
    {
        public string version = "2.1.0";
        public string lastUpdated = "";
        
        public IndexerConfig indexer = new IndexerConfig();
        public PickerConfig picker = new PickerConfig();
        public DuplicateCheckerConfig duplicateChecker = new DuplicateCheckerConfig();
        public ImageNormalizerConfig imageNormalizer = new ImageNormalizerConfig();
        public RecorderConfig recorder = new RecorderConfig();
    }
    
    /// <summary>
    /// UIProbe 配置管理器
    /// </summary>
    public static class UIProbeConfigManager
    {
        private static string ConfigPath => Path.Combine(UIProbeStorage.GetSettingsPath(), "config.json");
        
        /// <summary>
        /// 加载配置
        /// </summary>
        public static UIProbeConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    UIProbeConfig config = JsonUtility.FromJson<UIProbeConfig>(json);
                    Debug.Log($"[UIProbeConfig] 配置已加载: {ConfigPath}");
                    return config;
                }
                else
                {
                    Debug.Log($"[UIProbeConfig] 配置文件不存在，将尝试从EditorPrefs迁移");
                    return null;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbeConfig] 加载配置失败: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 保存配置
        /// </summary>
        public static void Save(UIProbeConfig config)
        {
            try
            {
                config.lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string json = JsonUtility.ToJson(config, true);
                
                // 确保目录存在
                string directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                File.WriteAllText(ConfigPath, json);
                Debug.Log($"[UIProbeConfig] 配置已保存: {ConfigPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbeConfig] 保存配置失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 从 EditorPrefs 迁移配置（一次性）
        /// </summary>
        public static UIProbeConfig MigrateFromEditorPrefs()
        {
            Debug.Log("[UIProbeConfig] 开始从EditorPrefs迁移配置...");
            
            UIProbeConfig config = new UIProbeConfig();
            
            try
            {
                // 迁移索引器配置
                config.indexer.rootPath = UnityEditor.EditorPrefs.GetString("UIProbe_IndexRootPath", "");
                
                string bookmarksStr = UnityEditor.EditorPrefs.GetString("UIProbe_Bookmarks", "");
                if (!string.IsNullOrEmpty(bookmarksStr))
                {
                    config.indexer.bookmarks = bookmarksStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                }
                
                string historyStr = UnityEditor.EditorPrefs.GetString("UIProbe_History", "");
                if (!string.IsNullOrEmpty(historyStr))
                {
                    config.indexer.searchHistory = historyStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                }
                
                // 迁移拾取器配置
                config.picker.autoMode = UnityEditor.EditorPrefs.GetBool("UIProbe_AutoPickerMode", false);
                
                // 迁移重名检测配置
                string excludedFoldersJson = UnityEditor.EditorPrefs.GetString("UIProbe_ExcludedFolders", "");
                if (!string.IsNullOrEmpty(excludedFoldersJson))
                {
                    try
                    {
                        var list = JsonUtility.FromJson<System.Collections.Generic.List<string>>(excludedFoldersJson);
                        if (list != null)
                        {
                            config.duplicateChecker.excludedFolders = list.ToArray();
                        }
                    }
                    catch { }
                }
                
                // 迁移记录器配置
                config.recorder.storagePath = UnityEditor.EditorPrefs.GetString("UIProbe_StoragePath", "");
                
                // 保存迁移后的配置
                Save(config);
                
                Debug.Log("[UIProbeConfig] 迁移完成！配置已保存到文件");
                return config;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UIProbeConfig] 迁移失败: {e.Message}");
                return new UIProbeConfig();  // 返回默认配置
            }
        }
    }
}
