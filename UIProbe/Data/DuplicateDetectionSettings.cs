using System;
using System.Collections.Generic;
using UnityEngine;

namespace UIProbe
{
    /// <summary>
    /// 检测模式
    /// </summary>
    public enum DetectionMode
    {
        Strict,  // 严格模式：报告所有重名
        Smart    // 智能模式：使用规则过滤
    }

    /// <summary>
    /// 重名检测设置
    /// </summary>
    [Serializable]
    public class DuplicateDetectionSettings
    {
        // 检测模式（过滤规则）
        public DetectionMode Mode = DetectionMode.Smart;
        
        // 检测范围
        public DuplicateDetectionMode DetectionScope = DuplicateDetectionMode.Global;
        
        // 项目特定：允许重复的名称（白名单）
        // 这些名称即使重复也不会被报告（如 Viewport, Content 等UGUI常见节点）
        public bool EnableWhitelist = true;  // 是否启用白名单
        public List<string> AllowedDuplicateNames = new List<string>
        {
            "Viewport",
            "Content", 
            "Scrollbar",
            "Scrollbar Horizontal",
            "Scrollbar Vertical",
            "Sliding Area",
            "Handle"
        };
        
        // UGUI组件类型检测
        public bool CheckUGUIComponentNames = true;
        public List<string> UGUIComponentsToCheck = new List<string>
        {
            "Image", "Text", "Button", "Toggle"
        };
        
        // 项目特定：禁止重复的名称（黑名单）
        // 即使在白名单中，黑名单优先级更高
        public List<string> ForbiddenDuplicateNames = new List<string>
        {
            // 例如：滚动列表的Content节点不允许重复
            // "Content"
        };

        // 前缀过滤：是否只检测特定前缀的节点
        public bool EnablePrefixFilter = false;
        public List<string> RequiredPrefixes = new List<string>
        {
            "c_",  // 例如：组件节点
            "m_"   // 例如：成员节点
        };

        /// <summary>
        /// 判断节点名称是否应该检测重复
        /// </summary>
        public bool ShouldCheckDuplicate(string nodeName, GameObject obj)
        {
            // 前缀过滤：如果启用，只检测符合前缀的节点
            if (EnablePrefixFilter)
            {
                bool hasRequiredPrefix = false;
                foreach (var prefix in RequiredPrefixes)
                {
                    if (nodeName.StartsWith(prefix))
                    {
                        hasRequiredPrefix = true;
                        break;
                    }
                }
                
                // 如果不符合任何前缀，跳过检测
                if (!hasRequiredPrefix)
                    return false;
            }
            
            // 严格模式：检测所有
            if (Mode == DetectionMode.Strict)
                return true;
            
            // 智能模式：应用规则
            
            // 1. 黑名单优先：如果在禁止列表中，必须检测
            if (ForbiddenDuplicateNames.Contains(nodeName))
                return true;
            
            // 2. 白名单：如果启用且在允许列表中，跳过检测
            if (EnableWhitelist && AllowedDuplicateNames.Contains(nodeName))
                return false;
            
            // 3. UGUI组件检测
            if (CheckUGUIComponentNames && obj != null)
            {
                var component = GetMainComponentType(obj);
                if (UGUIComponentsToCheck.Contains(component))
                {
                    // 如果节点名称就是组件类型名，检测重名
                    if (nodeName == component)
                        return true;
                }
            }
            
            // 4. 默认检测
            return true;
        }

        /// <summary>
        /// 获取节点的主要 UGUI 组件类型
        /// </summary>
        private string GetMainComponentType(GameObject obj)
        {
            if (obj.GetComponent<UnityEngine.UI.Button>() != null) return "Button";
            if (obj.GetComponent<UnityEngine.UI.Toggle>() != null) return "Toggle";
            if (obj.GetComponent<UnityEngine.UI.Slider>() != null) return "Slider";
            if (obj.GetComponent<UnityEngine.UI.ScrollRect>() != null) return "ScrollRect";
            if (obj.GetComponent<UnityEngine.UI.InputField>() != null) return "InputField";
            if (obj.GetComponent<UnityEngine.UI.Dropdown>() != null) return "Dropdown";
            if (obj.GetComponent<UnityEngine.UI.Image>() != null) return "Image";
            if (obj.GetComponent<UnityEngine.UI.Text>() != null) return "Text";
            if (obj.GetComponent<UnityEngine.UI.RawImage>() != null) return "RawImage";
            
            return "";
        }

        /// <summary>
        /// 获取默认设置
        /// </summary>
        public static DuplicateDetectionSettings GetDefault()
        {
            return new DuplicateDetectionSettings();
        }
    }
}
