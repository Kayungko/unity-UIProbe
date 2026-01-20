using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace UIProbe
{
    /// <summary>
    /// 动画路径修复工具
    /// 当重命名节点时，自动更新AnimationClip中的路径引用
    /// </summary>
    public static class AnimationPathRepair
    {
        /// <summary>
        /// 动画引用信息
        /// </summary>
        public class AnimationReference
        {
            public AnimationClip Clip;
            public List<EditorCurveBinding> AffectedBindings = new List<EditorCurveBinding>();
            
            public int AffectedCount => AffectedBindings.Count;
        }
        
        /// <summary>
        /// 查找预制体关联的所有AnimationClip
        /// </summary>
        public static List<AnimationClip> FindRelatedAnimationClips(GameObject prefabRoot)
        {
            var clips = new List<AnimationClip>();
            
            if (prefabRoot == null)
                return clips;
            
            // 方法1: 从Animator组件获取
            var animators = prefabRoot.GetComponentsInChildren<Animator>(true);
            foreach (var animator in animators)
            {
                if (animator.runtimeAnimatorController != null)
                {
                    clips.AddRange(animator.runtimeAnimatorController.animationClips);
                }
            }
            
            // 方法2: 从Animation组件获取 (Legacy动画)
            var animations = prefabRoot.GetComponentsInChildren<Animation>(true);
            foreach (var animation in animations)
            {
                foreach (AnimationState state in animation)
                {
                    if (state.clip != null)
                        clips.Add(state.clip);
                }
            }
            
            // 去重
            return clips.Distinct().ToList();
        }
        
        /// <summary>
        /// 查找AnimationClip中引用指定节点名称的所有绑定
        /// </summary>
        public static AnimationReference FindBindingsWithNodeName(AnimationClip clip, string nodeName)
        {
            var reference = new AnimationReference { Clip = clip };
            
            if (clip == null || string.IsNullOrEmpty(nodeName))
                return reference;
            
            // 检查float曲线
            var floatBindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in floatBindings)
            {
                if (PathContainsNodeName(binding.path, nodeName))
                {
                    reference.AffectedBindings.Add(binding);
                }
            }
            
            // 检查对象引用曲线 (如Sprite动画)
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (var binding in objectBindings)
            {
                if (PathContainsNodeName(binding.path, nodeName))
                {
                    reference.AffectedBindings.Add(binding);
                }
            }
            
            return reference;
        }
        
        /// <summary>
        /// 检查路径是否包含指定的节点名称
        /// </summary>
        private static bool PathContainsNodeName(string path, string nodeName)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            
            // 路径格式: "parent/child/nodeName" 或 "nodeName"
            string[] segments = path.Split('/');
            return segments.Any(s => s == nodeName);
        }
        
        /// <summary>
        /// 更新动画路径中的节点名称
        /// </summary>
        public static int UpdateAnimationPaths(AnimationClip clip, string oldName, string newName)
        {
            if (clip == null || string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
                return 0;
            
            int updatedCount = 0;
            
            // 处理float曲线
            var floatBindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in floatBindings)
            {
                string newPath = ReplaceNodeNameInPath(binding.path, oldName, newName);
                if (newPath != binding.path)
                {
                    // 获取曲线数据
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    
                    // 删除旧绑定
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                    
                    // 创建新绑定并设置曲线
                    EditorCurveBinding newBinding = binding;
                    newBinding.path = newPath;
                    AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                    
                    updatedCount++;
                }
            }
            
            // 处理对象引用曲线
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (var binding in objectBindings)
            {
                string newPath = ReplaceNodeNameInPath(binding.path, oldName, newName);
                if (newPath != binding.path)
                {
                    // 获取对象引用关键帧
                    var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    
                    // 删除旧绑定
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                    
                    // 创建新绑定并设置关键帧
                    EditorCurveBinding newBinding = binding;
                    newBinding.path = newPath;
                    AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keyframes);
                    
                    updatedCount++;
                }
            }
            
            if (updatedCount > 0)
            {
                EditorUtility.SetDirty(clip);
            }
            
            return updatedCount;
        }
        
        /// <summary>
        /// 替换路径中的节点名称
        /// </summary>
        private static string ReplaceNodeNameInPath(string path, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            
            string[] segments = path.Split('/');
            bool changed = false;
            
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == oldName)
                {
                    segments[i] = newName;
                    changed = true;
                }
            }
            
            return changed ? string.Join("/", segments) : path;
        }
        
        /// <summary>
        /// 重命名节点时检查并修复动画路径
        /// 返回: 是否应该继续重命名操作
        /// </summary>
        public static bool CheckAndRepairForRename(GameObject prefabRoot, Transform node, string newName)
        {
            if (prefabRoot == null || node == null || string.IsNullOrEmpty(newName))
                return true;
            
            string oldName = node.name;
            if (oldName == newName)
                return true;
            
            // 查找关联的动画剪辑
            var clips = FindRelatedAnimationClips(prefabRoot);
            if (clips.Count == 0)
                return true;
            
            // 查找受影响的动画引用
            var affectedReferences = new List<AnimationReference>();
            foreach (var clip in clips)
            {
                var reference = FindBindingsWithNodeName(clip, oldName);
                if (reference.AffectedCount > 0)
                {
                    affectedReferences.Add(reference);
                }
            }
            
            if (affectedReferences.Count == 0)
                return true;
            
            // 构建提示信息
            string message = $"重命名 \"{oldName}\" → \"{newName}\" 将影响以下动画:\n\n";
            int totalBindings = 0;
            
            foreach (var reference in affectedReferences)
            {
                message += $"📽 {reference.Clip.name}\n";
                foreach (var binding in reference.AffectedBindings.Take(3))
                {
                    message += $"   - {binding.propertyName}\n";
                }
                if (reference.AffectedCount > 3)
                {
                    message += $"   ... 及其他 {reference.AffectedCount - 3} 个属性\n";
                }
                totalBindings += reference.AffectedCount;
            }
            
            message += $"\n共 {affectedReferences.Count} 个动画剪辑，{totalBindings} 个属性引用";
            
            // 显示对话框
            int choice = EditorUtility.DisplayDialogComplex(
                "⚠️ 检测到动画引用",
                message,
                "重命名并修复动画",  // 0
                "取消",              // 1
                "仅重命名"           // 2
            );
            
            if (choice == 1) // 取消
                return false;
            
            if (choice == 0) // 重命名并修复动画
            {
                // 修复所有动画路径
                int totalFixed = 0;
                foreach (var reference in affectedReferences)
                {
                    int fixed_count = UpdateAnimationPaths(reference.Clip, oldName, newName);
                    totalFixed += fixed_count;
                }
                
                Debug.Log($"[UIProbe] 已修复 {totalFixed} 个动画路径引用");
            }
            
            return true; // 继续重命名
        }
        
        /// <summary>
        /// 获取节点相对于预制体根的路径
        /// </summary>
        public static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
                return "";
            
            if (target == root)
                return "";
            
            var path = new List<string>();
            Transform current = target;
            
            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }
            
            return string.Join("/", path);
        }
    }
}
