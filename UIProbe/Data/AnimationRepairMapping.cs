using System;
using System.Collections.Generic;
using UnityEngine;

namespace UIProbe
{
    /// <summary>
    /// 单条动画路径修复映射
    /// </summary>
    [Serializable]
    public class AnimationPathMapping
    {
        /// <summary>resolved = 可自动修复, unresolved = 节点已删除需人工处理</summary>
        public string status = "resolved";
        /// <summary>unresolved 时的原因说明</summary>
        public string unresolvedNote = "";
        public string clipAssetGuid;
        public string clipName;
        /// <summary>动画中的旧路径（已失效）</summary>
        public string oldPath;
        /// <summary>修复后的新路径（unresolved 时为空）</summary>
        public string newPath;
        public string bindingType;      // "float" 或 "objectReference"
        public string propertyName;
        public string componentName;    // Animator/Animation 所在节点名
    }

    /// <summary>
    /// 动画修复映射文件（导出/导入格式）
    /// </summary>
    [Serializable]
    public class AnimationRepairMappingFile
    {
        public string version = "1.0";
        public string exportTime;
        public string prefabName;
        public string prefabAssetPath;
        public string exportedBy;
        public int resolvedCount;
        public int unresolvedCount;
        public List<AnimationPathMapping> mappings = new List<AnimationPathMapping>();
    }
}
