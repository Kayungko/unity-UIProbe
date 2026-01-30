using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace UIProbe
{
    /// <summary>
    /// 预重命名映射管理器
    /// 负责导出、导入、应用重命名映射
    /// </summary>
    public static class RenameMappingManager
    {
        /// <summary>
        /// 导出当前预制体的重命名映射
        /// </summary>
        /// <param name="renameInputs">当前所有输入框的内容</param>
        /// <param name="prefabRoot">预制体根节点</param>
        /// <returns>导出的文件路径</returns>
        public static string ExportRenameMappings(Dictionary<GameObject, string> renameInputs, GameObject prefabRoot)
        {
            if (renameInputs == null || renameInputs.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有要导出的重命名映射", "确定");
                return null;
            }
            
            // 获取预制体信息
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null || prefabRoot == null)
            {
                EditorUtility.DisplayDialog("错误", "请先打开预制体", "确定");
                return null;
            }
            
            string prefabPath = prefabStage.assetPath;
            string prefabName = prefabRoot.name;
            
            // 创建映射数据
            var mappingData = new RenameMappingData(prefabName, prefabPath);
            
            // 扫描所有有效的重命名输入
            int validCount = 0;
            foreach (var kvp in renameInputs)
            {
                GameObject obj = kvp.Key;
                string newName = kvp.Value;
                
                // 跳过无效输入
                if (obj == null || string.IsNullOrWhiteSpace(newName))
                    continue;
                
                // 跳过未修改的名称
                if (obj.name == newName)
                    continue;
                
                // 获取节点路径
                string nodePath = AnimationPathRepair.GetRelativePath(prefabRoot.transform, obj.transform);
                
                mappingData.AddMapping(nodePath, obj.name, newName, obj.GetInstanceID());
                validCount++;
            }
            
            if (validCount == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有有效的重命名映射可以导出", "确定");
                return null;
            }
            
            // 生成文件名: YYYYMMDD_HHmmss_预制体名_RenameMapping.json
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{timestamp}_{prefabName}_RenameMapping.json";
            
            // 获取存储路径
            string storagePath = UIProbeStorage.GetModificationLogsPath();
            if (!Directory.Exists(storagePath))
            {
                Directory.CreateDirectory(storagePath);
            }
            
            string filePath = Path.Combine(storagePath, fileName);
            
            // 写入JSON文件
            try
            {
                File.WriteAllText(filePath, mappingData.ToJson());
                EditorUtility.DisplayDialog("导出成功", 
                    $"已导出 {validCount} 个重命名映射到:\n{filePath}", 
                    "确定");
                
                // 打开文件夹
                EditorUtility.RevealInFinder(filePath);
                
                Debug.Log($"[UIProbe] 导出预重命名映射: {filePath}");
                return filePath;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("错误", $"导出失败: {e.Message}", "确定");
                Debug.LogError($"[UIProbe] 导出预重命名映射失败: {e}");
                return null;
            }
        }
        
        /// <summary>
        /// 导入重命名映射
        /// </summary>
        /// <param name="prefabRoot">预制体根节点</param>
        /// <returns>导入的映射数据（如果成功）</returns>
        public static RenameMappingData ImportRenameMappings(GameObject prefabRoot)
        {
            if (prefabRoot == null)
            {
                EditorUtility.DisplayDialog("错误", "请先打开预制体", "确定");
                return null;
            }
            
            // 选择JSON文件
            string storagePath = UIProbeStorage.GetModificationLogsPath();
            string filePath = EditorUtility.OpenFilePanel(
                "选择预重命名映射文件", 
                storagePath, 
                "json"
            );
            
            if (string.IsNullOrEmpty(filePath))
                return null; // 用户取消
            
            // 读取JSON
            try
            {
                string json = File.ReadAllText(filePath);
                RenameMappingData mappingData = RenameMappingData.FromJson(json);
                
                if (mappingData == null)
                {
                    EditorUtility.DisplayDialog("错误", "解析JSON文件失败", "确定");
                    return null;
                }
                
                // 验证映射的有效性
                ValidateMappings(mappingData, prefabRoot);
                
                Debug.Log($"[UIProbe] 导入预重命名映射: {mappingData.mappings.Count} 个，有效: {mappingData.validMappings}，无效: {mappingData.invalidMappings}");
                return mappingData;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("错误", $"导入失败: {e.Message}", "确定");
                Debug.LogError($"[UIProbe] 导入预重命名映射失败: {e}");
                return null;
            }
        }
        
        /// <summary>
        /// 验证映射是否有效（节点是否存在）
        /// </summary>
        private static void ValidateMappings(RenameMappingData mappingData, GameObject prefabRoot)
        {
            mappingData.validMappings = 0;
            mappingData.invalidMappings = 0;
            
            foreach (var mapping in mappingData.mappings)
            {
                // 尝试通过路径查找节点
                Transform targetNode = FindNodeByPath(prefabRoot.transform, mapping.nodePath);
                
                if (targetNode != null && targetNode.name == mapping.oldName)
                {
                    mappingData.validMappings++;
                }
                else
                {
                    mappingData.invalidMappings++;
                    Debug.LogWarning($"[UIProbe] 映射无效: {mapping.nodePath} (节点不存在或名称已变更)");
                }
            }
        }
        
        /// <summary>
        /// 通过路径查找节点
        /// </summary>
        private static Transform FindNodeByPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path))
                return root;
            
            return root.Find(path);
        }
        
        /// <summary>
        /// 应用单个重命名映射
        /// </summary>
        public static bool ApplySingleMapping(NodeRenameMapping mapping, GameObject prefabRoot, 
            Action<GameObject, string> applyRenameCallback)
        {
            Transform targetNode = FindNodeByPath(prefabRoot.transform, mapping.nodePath);
            
            if (targetNode == null || targetNode.name != mapping.oldName)
            {
                EditorUtility.DisplayDialog("错误", 
                    $"无法找到节点: {mapping.nodePath}\n或节点名称已变更", 
                    "确定");
                return false;
            }
            
            // 调用现有的重命名逻辑（包含动画修复）
            if (applyRenameCallback != null)
            {
                applyRenameCallback(targetNode.gameObject, mapping.newName);
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// 批量应用所有映射
        /// </summary>
        public static int ApplyAllMappings(RenameMappingData mappingData, GameObject prefabRoot, 
            Action<GameObject, string> applyRenameCallback)
        {
            if (mappingData == null || mappingData.mappings.Count == 0)
                return 0;
            
            // 确认对话框
            int validCount = mappingData.validMappings;
            int invalidCount = mappingData.invalidMappings;
            
            string message = $"即将批量应用 {validCount} 个重命名映射";
            if (invalidCount > 0)
            {
                message += $"\n（跳过 {invalidCount} 个无效映射）";
            }
            message += "\n\n是否继续？";
            
            if (!EditorUtility.DisplayDialog("批量应用确认", message, "继续", "取消"))
                return 0;
            
            int successCount = 0;
            
            foreach (var mapping in mappingData.mappings)
            {
                Transform targetNode = FindNodeByPath(prefabRoot.transform, mapping.nodePath);
                
                if (targetNode != null && targetNode.name == mapping.oldName)
                {
                    if (applyRenameCallback != null)
                    {
                        applyRenameCallback(targetNode.gameObject, mapping.newName);
                        successCount++;
                    }
                }
            }
            
            if (successCount > 0)
            {
                EditorUtility.DisplayDialog("完成", 
                    $"成功应用 {successCount} 个重命名", 
                    "确定");
            }
            
            return successCount;
        }
    }
}
