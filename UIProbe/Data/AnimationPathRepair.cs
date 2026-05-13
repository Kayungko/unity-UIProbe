using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UIProbe
{
    /// <summary>
    /// 动画路径修复工具
    /// 当重命名节点时，自动更新AnimationClip中的路径引用
    /// 支持基于层级路径的精确匹配，避免同名节点误伤
    /// </summary>
    public static class AnimationPathRepair
    {
        /// <summary>
        /// 动画引用上下文
        /// </summary>
        public class AnimationContext
        {
            public Component AnimatorComponent; // Animator 或 Animation
            public Transform RootTransform;     // 动画根节点
            public string RelativeParamPath;    // 重命名节点相对于根节点的路径
            public List<AnimationClip> Clips = new List<AnimationClip>();
            
            // 记录受影响的 Clip 和 Binding 数量
            public Dictionary<AnimationClip, int> AffectedClips = new Dictionary<AnimationClip, int>();
            public int TotalAffectedBindings => AffectedClips.Values.Sum();
        }

        /// <summary>
        /// 主入口：重命名节点时检查并修复动画路径
        /// 返回: 是否应该继续重命名操作
        /// </summary>
        public static bool CheckAndRepairForRename(GameObject prefabRoot, Transform targetNode, string newName)
        {
            if (prefabRoot == null || targetNode == null || string.IsNullOrEmpty(newName))
                return true;
            
            string oldName = targetNode.name;
            if (oldName == newName)
                return true;

            // 1. 查找所有受影响的动画上下文
            var contexts = FindAffectedContexts(prefabRoot, targetNode);
            
            // 2. 扫描具体受影响的绑定
            int totalAffectedContexts = 0;
            foreach (var ctx in contexts)
            {
                ScanAffectedBindings(ctx);
                if (ctx.TotalAffectedBindings > 0)
                {
                    totalAffectedContexts++;
                }
            }

            if (totalAffectedContexts == 0)
                return true;

            // 3. 构建提示信息
            string message = $"重命名 \"{oldName}\" → \"{newName}\" 将影响以下动画:\n\n";
            int totalBindings = 0;

            foreach (var ctx in contexts)
            {
                if (ctx.TotalAffectedBindings == 0) continue;

                string rootName = ctx.RootTransform.name;
                message += $"🎮 动画组件位于: {rootName}\n"; // 区分不同的Animator
                
                foreach (var kv in ctx.AffectedClips)
                {
                    AnimationClip clip = kv.Key;
                    int count = kv.Value;
                    message += $"   🎬 {clip.name} ({count} 处引用)\n";
                }
                message += "\n";
                totalBindings += ctx.TotalAffectedBindings;
            }

            message += $"共 {totalAffectedContexts} 个动画组件，{totalBindings} 个属性引用将自动更新。";

            // 4. 显示对话框
            int choice = EditorUtility.DisplayDialogComplex(
                "⚠️ 检测到动画引用",
                message,
                "重命名并修复动画",  // 0
                "取消",              // 1
                "仅重命名"           // 2 - 不修复
            );

            if (choice == 1) // 取消
                return false;

            if (choice == 0) // 重命名并修复动画
            {
                // 执行修复
                int fixedCount = ExecuteRepair(contexts, newName);
                Debug.Log($"[UIProbe] 修复完成: 更新了 {fixedCount} 个动画路径引用");
            }

            return true; // 继续重命名
        }

        /// <summary>
        /// 查找所有可能受影响的 Animator/Animation 组件
        /// </summary>
        private static List<AnimationContext> FindAffectedContexts(GameObject prefabRoot, Transform targetNode)
        {
            var contexts = new List<AnimationContext>();

            // 获取预制体中所有的 Animator 和 Animation 组件
            var animators = prefabRoot.GetComponentsInChildren<Animator>(true);
            var animations = prefabRoot.GetComponentsInChildren<Animation>(true);

            // 检查 Animator
            foreach (var animator in animators)
            {
                if (IsDescendantOrSelf(animator.transform, targetNode))
                {
                    var ctx = new AnimationContext
                    {
                        AnimatorComponent = animator,
                        RootTransform = animator.transform,
                        RelativeParamPath = GetRelativePath(animator.transform, targetNode)
                    };

                    if (animator.runtimeAnimatorController != null)
                    {
                        ctx.Clips.AddRange(animator.runtimeAnimatorController.animationClips);
                        ctx.Clips = ctx.Clips.Distinct().ToList(); // 去重
                        contexts.Add(ctx);
                    }
                }
            }

            // 检查 Animation (Legacy)
            foreach (var animation in animations)
            {
                if (IsDescendantOrSelf(animation.transform, targetNode))
                {
                    var ctx = new AnimationContext
                    {
                        AnimatorComponent = animation,
                        RootTransform = animation.transform,
                        RelativeParamPath = GetRelativePath(animation.transform, targetNode)
                    };

                    foreach (AnimationState state in animation)
                    {
                        if (state.clip != null)
                            ctx.Clips.Add(state.clip);
                    }
                    ctx.Clips = ctx.Clips.Distinct().ToList();
                    contexts.Add(ctx);
                }
            }

            return contexts;
        }

        /// <summary>
        /// 扫描上下文中受影响的绑定
        /// </summary>
        private static void ScanAffectedBindings(AnimationContext ctx)
        {
            string targetPath = ctx.RelativeParamPath;
            
            // 如果 targetPath 为空，说明重命名的是 Animator 根节点本身
            // 这种情况下通常不影响 Clip 内部路径（除非 Clip 用了空路径来动画化根节点属性）
            // 但为了安全，我们还是检查
            
            foreach (var clip in ctx.Clips)
            {
                int affectedCount = 0;
                
                // 检查所有绑定
                var bindings = AnimationUtility.GetCurveBindings(clip);
                affectedCount += bindings.Count(b => IsPathAffected(b.path, targetPath));
                
                var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                affectedCount += objBindings.Count(b => IsPathAffected(b.path, targetPath));

                if (affectedCount > 0)
                {
                    ctx.AffectedClips[clip] = affectedCount;
                }
            }
        }

        /// <summary>
        /// 执行修复
        /// </summary>
        private static int ExecuteRepair(List<AnimationContext> contexts, string newNodeName)
        {
            int totalFixed = 0;

            foreach (var ctx in contexts)
            {
                string oldRelPath = ctx.RelativeParamPath;
                // 计算新的相对路径：把 oldRelPath 的最后一部分替换为 newNodeName
                string newRelPath = ReplaceLastPathSegment(oldRelPath, newNodeName);

                foreach (var kv in ctx.AffectedClips)
                {
                    AnimationClip clip = kv.Key;
                    
                    // Float Curves
                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    foreach (var binding in bindings)
                    {
                        if (IsPathAffected(binding.path, oldRelPath))
                        {
                            string newBindingPath = UpdatePath(binding.path, oldRelPath, newRelPath);
                            
                            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                            AnimationUtility.SetEditorCurve(clip, binding, null); // 删除旧的
                            
                            EditorCurveBinding newBinding = binding;
                            newBinding.path = newBindingPath;
                            AnimationUtility.SetEditorCurve(clip, newBinding, curve); // 添加新的
                            
                            totalFixed++;
                        }
                    }

                    // Object Reference Curves
                    var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    foreach (var binding in objBindings)
                    {
                        if (IsPathAffected(binding.path, oldRelPath))
                        {
                            string newBindingPath = UpdatePath(binding.path, oldRelPath, newRelPath);
                            
                            ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                            AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                            
                            EditorCurveBinding newBinding = binding;
                            newBinding.path = newBindingPath;
                            AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keyframes);
                            
                            totalFixed++;
                        }
                    }
                    
                    EditorUtility.SetDirty(clip);
                }
            }

            if (totalFixed > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return totalFixed;
        }

        // --- Helper Methods ---

        private static bool IsDescendantOrSelf(Transform root, Transform target)
        {
            return target.IsChildOf(root);
        }

        public static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target) return "";
            return AnimationUtility.CalculateTransformPath(target, root);
        }

        /// <summary>
        /// 判断绑定路径是否受影响
        /// 绑定路径必须 等于 目标路径，或者 以 "目标路径/" 开头
        /// </summary>
        private static bool IsPathAffected(string bindingPath, string targetPath)
        {
            if (string.IsNullOrEmpty(targetPath)) return false; // 如果重命名的是根节点，一般不包含在路径里
            
            if (bindingPath == targetPath) return true;
            if (bindingPath.StartsWith(targetPath + "/")) return true;
            
            return false;
        }

        /// <summary>
        /// 更新路径
        /// 例如: bindingPath="A/Old/B", targetPath="A/Old", newTargetPath="A/New"
        /// 结果 -> "A/New/B"
        /// </summary>
        private static string UpdatePath(string bindingPath, string oldTargetPath, string newTargetPath)
        {
            if (bindingPath == oldTargetPath) return newTargetPath;
            
            // 替换前缀
            // 这里的 Replace 实际上是安全的，因为我们已经确认了 bindingPath StartsWith oldTargetPath + "/"
            return newTargetPath + bindingPath.Substring(oldTargetPath.Length);
        }

        /// <summary>
        /// 替换路径的最后一段
        /// "A/B/OldName" -> "A/B/NewName"
        /// "OldName" -> "NewName"
        /// </summary>
        private static string ReplaceLastPathSegment(string path, string newName)
        {
            if (string.IsNullOrEmpty(path)) return newName; // 理论上不应该发生，因为空路径是根节点
            
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash == -1)
            {
                return newName;
            }
            else
            {
                return path.Substring(0, lastSlash + 1) + newName;
            }
        }
    }
}
