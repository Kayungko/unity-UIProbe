using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace UIProbe
{
    /// <summary>
    /// 界面记录会话，包含一次完整的录制数据
    /// </summary>
    [Serializable]
    public class UIRecordSession
    {
        public string Version = "1.0.0";
        public string Timestamp;
        public string Description;
        public string ScreenshotPath;  // 截图文件路径 (相对于记录文件)
        public List<UIRecordEvent> Events = new List<UIRecordEvent>();
        
        public UIRecordSession()
        {
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    /// <summary>
    /// 单个记录事件/节点
    /// </summary>
    [Serializable]
    public class UIRecordEvent
    {
        public string EventType;      // "Root" / "Instantiate" / "Activate" / "Child"
        public string NodeName;       // GameObject 名称
        public string NodePath;       // 完整路径
        public string PrefabName;     // 预制体名称 (如果有)
        public string PrefabPath;     // 预制体资源路径
        public string Tag = "";       // "一级界面" / "二级界面" / "弹窗" / "标签页"
        public string Timestamp;
        public bool IsPrefabInstance;
        public List<UIRecordEvent> Children = new List<UIRecordEvent>();
        
        // Non-serialized runtime reference
        [NonSerialized] public GameObject GameObjectRef;
        [NonSerialized] public bool IsExpanded = true;
    }

    /// <summary>
    /// 标签类型枚举
    /// </summary>
    public enum UITagType
    {
        None,
        PrimaryForm,    // 一级界面
        SecondaryForm,  // 二级界面
        TertiaryForm,   // 三级界面
        Popup,          // 弹窗
        Tab,            // 标签页
        Custom          // 自定义
    }

    /// <summary>
    /// 存储路径管理
    /// </summary>
    public static class UIRecordStorage
    {
        private const string AppDataFolderName = "UIProbe/Records";
        private const string AssetsFolderName = "Assets/UIProbeRecords";
        
        /// <summary>
        /// 获取默认存储路径 (AppData，避免 Git 提交)
        /// </summary>
        public static string GetDefaultStoragePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string path = Path.Combine(appData, "Unity", AppDataFolderName);
            
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            
            return path;
        }
        
        /// <summary>
        /// 获取 Assets 存储路径 (可选，需用户主动选择)
        /// </summary>
        public static string GetAssetsStoragePath()
        {
            string path = Path.Combine(Application.dataPath, "..", AssetsFolderName);
            path = Path.GetFullPath(path);
            
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                
                // Create .gitignore to prevent accidental commits
                string gitignorePath = Path.Combine(path, ".gitignore");
                File.WriteAllText(gitignorePath, "# UI Probe Records - Auto-generated\n*\n!.gitignore\n");
            }
            
            return path;
        }
        
        /// <summary>
        /// 保存记录会话 (带截图)
        /// </summary>
        public static string SaveSession(UIRecordSession session, string storagePath, Texture2D screenshot = null)
        {
            if (storagePath == null)
            {
                storagePath = GetDefaultStoragePath();
            }
            
            string fileBaseName = $"UIRecord_{session.Version}_{DateTime.Now:yyyyMMdd_HHmmss}";
            string jsonPath = Path.Combine(storagePath, fileBaseName + ".json");
            
            // Save screenshot if provided
            if (screenshot != null)
            {
                string screenshotName = fileBaseName + ".png";
                string screenshotPath = Path.Combine(storagePath, screenshotName);
                
                byte[] pngData = screenshot.EncodeToPNG();
                File.WriteAllBytes(screenshotPath, pngData);
                
                session.ScreenshotPath = screenshotName;
                Debug.Log($"[UI Probe] 截图已保存: {screenshotPath}");
            }
            
            string json = JsonUtility.ToJson(session, true);
            File.WriteAllText(jsonPath, json);
            
            Debug.Log($"[UI Probe] 记录已保存: {jsonPath}");
            return jsonPath;
        }
        
        /// <summary>
        /// 获取记录的截图路径
        /// </summary>
        public static string GetScreenshotPath(string jsonFilePath, UIRecordSession session)
        {
            if (string.IsNullOrEmpty(session.ScreenshotPath)) return null;
            
            string directory = Path.GetDirectoryName(jsonFilePath);
            string fullPath = Path.Combine(directory, session.ScreenshotPath);
            
            return File.Exists(fullPath) ? fullPath : null;
        }
        
        /// <summary>
        /// 加载所有记录会话
        /// </summary>
        public static List<(string Path, UIRecordSession Session)> LoadAllSessions(string storagePath = null)
        {
            var results = new List<(string, UIRecordSession)>();
            
            // Load from default path
            LoadFromPath(GetDefaultStoragePath(), results);
            
            // Also load from Assets path if exists
            string assetsPath = GetAssetsStoragePath();
            if (Directory.Exists(assetsPath))
            {
                LoadFromPath(assetsPath, results);
            }
            
            return results;
        }
        
        private static void LoadFromPath(string path, List<(string, UIRecordSession)> results)
        {
            if (!Directory.Exists(path)) return;
            
            string[] files = Directory.GetFiles(path, "*.json");
            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var session = JsonUtility.FromJson<UIRecordSession>(json);
                    if (session != null)
                    {
                        results.Add((file, session));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UI Probe] 加载记录失败: {file}, {e.Message}");
                }
            }
        }
        
        /// <summary>
        /// 删除记录
        /// </summary>
        public static void DeleteSession(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                
                // Also delete screenshot if exists
                string screenshotPath = Path.ChangeExtension(filePath, ".png");
                if (File.Exists(screenshotPath))
                {
                    File.Delete(screenshotPath);
                }
                
                Debug.Log($"[UI Probe] 记录已删除: {filePath}");
            }
        }
        
        /// <summary>
        /// 导出记录为 .uiprobe 文件 (实际上是 ZIP 包)
        /// </summary>
        public static bool ExportSession(string jsonFilePath, string exportPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(jsonFilePath);
                string baseName = Path.GetFileNameWithoutExtension(jsonFilePath);
                
                // Prepare files to export
                var filesToExport = new List<string> { jsonFilePath };
                
                string screenshotPath = Path.Combine(directory, baseName + ".png");
                if (File.Exists(screenshotPath))
                {
                    filesToExport.Add(screenshotPath);
                }
                
                // Create a simple archive (concatenated files with header)
                using (var fs = new FileStream(exportPath, FileMode.Create))
                using (var writer = new BinaryWriter(fs))
                {
                    // Magic header
                    writer.Write("UIPROBE1");
                    writer.Write(filesToExport.Count);
                    
                    foreach (var file in filesToExport)
                    {
                        string fileName = Path.GetFileName(file);
                        byte[] data = File.ReadAllBytes(file);
                        
                        writer.Write(fileName);
                        writer.Write(data.Length);
                        writer.Write(data);
                    }
                }
                
                Debug.Log($"[UI Probe] 导出成功: {exportPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UI Probe] 导出失败: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 导入 .uiprobe 文件
        /// </summary>
        public static bool ImportSession(string importPath, string targetDirectory = null)
        {
            try
            {
                if (targetDirectory == null)
                {
                    targetDirectory = GetDefaultStoragePath();
                }
                
                using (var fs = new FileStream(importPath, FileMode.Open))
                using (var reader = new BinaryReader(fs))
                {
                    // Read magic header
                    string magic = reader.ReadString();
                    if (magic != "UIPROBE1")
                    {
                        Debug.LogError("[UI Probe] 无效的 .uiprobe 文件格式");
                        return false;
                    }
                    
                    int fileCount = reader.ReadInt32();
                    
                    for (int i = 0; i < fileCount; i++)
                    {
                        string fileName = reader.ReadString();
                        int dataLength = reader.ReadInt32();
                        byte[] data = reader.ReadBytes(dataLength);
                        
                        string targetPath = Path.Combine(targetDirectory, fileName);
                        File.WriteAllBytes(targetPath, data);
                    }
                }
                
                Debug.Log($"[UI Probe] 导入成功: {importPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UI Probe] 导入失败: {e.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 自定义标签规则
    /// </summary>
    [Serializable]
    public class UICustomTagRule
    {
        public string Keyword;      // 匹配关键字 (小写)
        public string Tag;          // 目标标签
        public bool IsEnabled = true;
    }

    /// <summary>
    /// 自动标签推断器
    /// </summary>
    public static class UITagInferrer
    {
        private static List<UICustomTagRule> customRules;

        public static List<UICustomTagRule> GetCustomRules()
        {
            if (customRules == null)
            {
                LoadRules();
            }
            return customRules;
        }

        public static void AddRule(string keyword, string tag)
        {
            if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(tag)) return;
            
            var rules = GetCustomRules();
            rules.Add(new UICustomTagRule { Keyword = keyword.ToLower(), Tag = tag });
            SaveRules();
        }

        public static void RemoveRule(UICustomTagRule rule)
        {
            var rules = GetCustomRules();
            if (rules.Contains(rule))
            {
                rules.Remove(rule);
                SaveRules();
            }
        }
        
        public static void SaveRules()
        {
            if (customRules == null) return;
            string json = JsonUtility.ToJson(new RuleWrapper { Rules = customRules });
            EditorPrefs.SetString("UIProbe_CustomTagRules", json);
        }

        private static void LoadRules()
        {
            string json = EditorPrefs.GetString("UIProbe_CustomTagRules", "");
            if (!string.IsNullOrEmpty(json))
            {
                try 
                {
                    var wrapper = JsonUtility.FromJson<RuleWrapper>(json);
                    customRules = wrapper != null ? wrapper.Rules : new List<UICustomTagRule>();
                }
                catch
                {
                    customRules = new List<UICustomTagRule>();
                }
            }
            else
            {
                customRules = new List<UICustomTagRule>();
            }
        }

        [Serializable]
        private class RuleWrapper
        {
            public List<UICustomTagRule> Rules;
        }

        public static string InferTag(string nodeName, int depth)
        {
            string nameLower = nodeName.ToLower();
            
            // 1. Check custom rules first
            var rules = GetCustomRules();
            foreach (var rule in rules)
            {
                if (rule.IsEnabled && nameLower.Contains(rule.Keyword))
                {
                    return rule.Tag;
                }
            }
            
            // 2. Hardcoded rules
            // 弹窗检测
            if (nameLower.Contains("popup") || nameLower.Contains("dialog") || 
                nameLower.Contains("modal") || nameLower.Contains("alert"))
            {
                return "弹窗";
            }
            
            // 标签页检测
            if (nameLower.Contains("tab") || nameLower.Contains("page"))
            {
                return "标签页";
            }
            
            // 界面层级检测
            if (nameLower.Contains("form") || nameLower.Contains("panel") || 
                nameLower.Contains("view") || nameLower.Contains("screen"))
            {
                if (nameLower.Contains("main") || nameLower.Contains("home") || depth <= 1)
                {
                    return "一级界面";
                }
                else if (nameLower.Contains("sub") || nameLower.Contains("detail") || depth == 2)
                {
                    return "二级界面";
                }
                else if (depth >= 3)
                {
                    return "三级界面";
                }
            }
            
            return "";
        }
        
        public static Color GetTagColor(string tag)
        {
            switch (tag)
            {
                case "一级界面": return new Color(0.2f, 0.6f, 1f);      // Blue
                case "二级界面": return new Color(0.4f, 0.8f, 0.4f);    // Green
                case "三级界面": return new Color(0.6f, 0.4f, 0.8f);    // Purple
                case "弹窗": return new Color(1f, 0.6f, 0.2f);          // Orange
                case "标签页": return new Color(0.8f, 0.8f, 0.2f);      // Yellow
                default:
                    // Generate a color hash for custom tags
                    int hash = tag.GetHashCode();
                    float r = ((hash & 0xFF0000) >> 16) / 255f;
                    float g = ((hash & 0x00FF00) >> 8) / 255f;
                    float b = (hash & 0x0000FF) / 255f;
                    // Ensure it's not too dark or too bright
                    return new Color(0.5f + r * 0.5f, 0.5f + g * 0.5f, 0.5f + b * 0.5f);
            }
        }
    }
}
