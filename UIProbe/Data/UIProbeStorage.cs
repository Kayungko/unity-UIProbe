using System;
using System.IO;
using UnityEngine;

namespace UIProbe
{
    /// <summary>
    /// UIProbe 统一存储路径管理
    /// </summary>
    public static class UIProbeStorage
    {
        // 主文件夹名称
        private const string MAIN_FOLDER_NAME = "UIProbe";
        
        // 子文件夹名称
        private const string UI_HISTORY_FOLDER = "UI_Interface_History";
        private const string RENAME_HISTORY_FOLDER = "Rename_History";
        private const string MODIFICATION_LOGS_FOLDER = "Modification_Logs";
        private const string CSV_EXPORTS_FOLDER = "CSV_Exports";
        private const string BATCH_RESULTS_FOLDER = "Batch_Results";
        private const string SETTINGS_FOLDER = "Settings";

        /// <summary>
        /// 获取主文件夹路径 (GetStoragePath 别名)
        /// </summary>
        public static string GetStoragePath()
        {
            return GetMainFolderPath();
        }

        /// <summary>
        /// 获取主文件夹路径
        /// </summary>
        public static string GetMainFolderPath()
        {
            // 优先使用用户配置的路径
            string customPath = UnityEditor.EditorPrefs.GetString("UIProbe_MainStoragePath", "");
            
            if (!string.IsNullOrEmpty(customPath))
            {
                return Path.Combine(customPath, MAIN_FOLDER_NAME);
            }
            
            // 默认路径：AppData
            return GetDefaultMainPath();
        }

        /// <summary>
        /// 获取默认主文件夹路径（AppData）
        /// </summary>
        public static string GetDefaultMainPath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataPath, MAIN_FOLDER_NAME);
        }

        /// <summary>
        /// 获取界面记录存储路径
        /// </summary>
        public static string GetUIHistoryPath()
        {
            string path = Path.Combine(GetMainFolderPath(), UI_HISTORY_FOLDER);
            EnsureDirectoryExists(path);
            return path;
        }

        /// <summary>
        /// 获取重命名历史存储路径
        /// </summary>
        public static string GetRenameHistoryPath()
        {
            string path = Path.Combine(GetMainFolderPath(), RENAME_HISTORY_FOLDER);
            EnsureDirectoryExists(path);
            return path;
        }

        /// <summary>
        /// 获取修改日志存储路径
        /// </summary>
        public static string GetModificationLogsPath()
        {
            string path = Path.Combine(GetMainFolderPath(), MODIFICATION_LOGS_FOLDER);
            EnsureDirectoryExists(path);
            return path;
        }

        /// <summary>
        /// 获取 CSV 导出默认路径
        /// </summary>
        public static string GetCSVExportPath()
        {
            string path = Path.Combine(GetMainFolderPath(), CSV_EXPORTS_FOLDER);
            EnsureDirectoryExists(path);
            return path;
        }

        /// <summary>
        /// 获取批量检测结果存储路径
        /// </summary>
        public static string GetBatchResultsPath()
        {
            string path = Path.Combine(GetMainFolderPath(), BATCH_RESULTS_FOLDER);
            EnsureDirectoryExists(path);
            return path;
        }

        /// <summary>
        /// 获取设置数据存储路径
        /// </summary>
        public static string GetSettingsPath()
        {
            string path = Path.Combine(GetMainFolderPath(), SETTINGS_FOLDER);
            EnsureDirectoryExists(path);
            return path;
        }

        /// <summary>
        /// 设置自定义主文件夹路径
        /// </summary>
        /// <param name="customBasePath">自定义基础路径，主文件夹会创建在此路径下</param>
        public static void SetCustomMainPath(string customBasePath)
        {
            if (string.IsNullOrEmpty(customBasePath))
            {
                // 清空表示使用默认路径
                UnityEditor.EditorPrefs.SetString("UIProbe_MainStoragePath", "");
            }
            else
            {
                UnityEditor.EditorPrefs.SetString("UIProbe_MainStoragePath", customBasePath);
            }
        }

        /// <summary>
        /// 确保目录存在
        /// </summary>
        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        /// <summary>
        /// 获取文件夹结构说明
        /// </summary>
        public static string GetFolderStructureDescription()
        {
            return $@"UIProbe 文件夹结构：
{GetMainFolderPath()}/
├── IndexCache.json              (索引缓存)
├── {UI_HISTORY_FOLDER}/          (界面记录)
├── {RENAME_HISTORY_FOLDER}/      (重命名历史)
├── {MODIFICATION_LOGS_FOLDER}/   (CSV修改日志-新增)
├── {CSV_EXPORTS_FOLDER}/         (检测结果导出)
├── {BATCH_RESULTS_FOLDER}/       (批量检测结果)
└── {SETTINGS_FOLDER}/            (设置数据)";
        }
    }
}
