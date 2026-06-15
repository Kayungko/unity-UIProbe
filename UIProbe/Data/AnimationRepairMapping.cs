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
        public string clipAssetGuid;
        public string clipName;
        public string oldPath;
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
        public string exportedBy;       // 导出者标识
        public List<AnimationPathMapping> mappings = new List<AnimationPathMapping>();
    }
}
