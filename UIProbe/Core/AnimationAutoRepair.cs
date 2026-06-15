using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UIProbe
{
    public static class AnimationAutoRepair
    {
        public static bool IsEnabled { get; private set; }
        public static event System.Action<string> OnRepairLog;

        private static float lastChangeTime;
        private const float DEBOUNCE_SECONDS = 0.5f;
        private static bool isPending;

        public static void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            if (enabled)
            {
                EditorApplication.hierarchyChanged += OnHierarchyChanged;
                Log("动画路径自动修复已启用");
            }
            else
            {
                EditorApplication.hierarchyChanged -= OnHierarchyChanged;
                isPending = false;
                Log("动画路径自动修复已停用");
            }
        }

        public static void Initialize()
        {
            if (IsEnabled)
                EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        public static void Shutdown()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            isPending = false;
        }

        private static void OnHierarchyChanged()
        {
            if (!IsEnabled) return;
            if (isPending) return;
            lastChangeTime = Time.realtimeSinceStartup;
            isPending = true;
            EditorApplication.delayCall += DelayedCheck;
        }

        private static void DelayedCheck()
        {
            if (Time.realtimeSinceStartup - lastChangeTime < DEBOUNCE_SECONDS)
            {
                EditorApplication.delayCall += DelayedCheck;
                return;
            }
            isPending = false;
            CheckAndRepair();
        }

        // ===== 检测 =====

        /// <summary>
        /// 检测当前预制体中失效的动画绑定路径，不执行修复
        /// </summary>
        public static List<AnimationPathMapping> DetectBrokenPaths(GameObject root = null)
        {
            var result = new List<AnimationPathMapping>();

            if (root == null)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null) return result;
                root = stage.prefabContentsRoot;
            }

            var animators = root.GetComponentsInChildren<Animator>(true);
            var animations = root.GetComponentsInChildren<Animation>(true);

            var allClips = new HashSet<AnimationClip>();
            var componentMap = new Dictionary<AnimationClip, Component>();

            foreach (var a in animators)
            {
                if (a.runtimeAnimatorController == null) continue;
                foreach (var clip in a.runtimeAnimatorController.animationClips)
                {
                    if (clip != null && allClips.Add(clip))
                        componentMap[clip] = a;
                }
            }
            foreach (var a in animations)
            {
                foreach (AnimationState state in a)
                {
                    if (state.clip != null && allClips.Add(state.clip))
                        componentMap[state.clip] = a;
                }
            }

            foreach (var clip in allClips)
            {
                var component = componentMap[clip];
                var rootTransform = component.transform;
                string clipGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip));

                // Float curves
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var mapping = TryDetectBinding(clip, binding, rootTransform, "float", clipGuid);
                    if (mapping != null)
                    {
                        mapping.componentName = component.name;
                        result.Add(mapping);
                    }
                }

                // Object reference curves
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var mapping = TryDetectBinding(clip, binding, rootTransform, "objectReference", clipGuid);
                    if (mapping != null)
                    {
                        mapping.componentName = component.name;
                        result.Add(mapping);
                    }
                }
            }

            return result;
        }

        private static AnimationPathMapping TryDetectBinding(AnimationClip clip, EditorCurveBinding binding, Transform root, string bindingType, string clipGuid)
        {
            if (string.IsNullOrEmpty(binding.path)) return null;

            Transform found = root.Find(binding.path);
            if (found != null) return null; // 路径有效

            string nodeName = binding.path;
            int lastSlash = binding.path.LastIndexOf('/');
            if (lastSlash >= 0)
                nodeName = binding.path.Substring(lastSlash + 1);

            var matches = new List<Transform>();
            FindTransformsByName(root, nodeName, matches);

            if (matches.Count != 1) return null;

            string newPath = AnimationPathRepair.GetRelativePath(root, matches[0]);
            if (newPath == binding.path) return null;

            return new AnimationPathMapping
            {
                clipAssetGuid = clipGuid,
                clipName = clip.name,
                oldPath = binding.path,
                newPath = newPath,
                bindingType = bindingType,
                propertyName = binding.propertyName
            };
        }

        // ===== 自动修复 =====

        public static int CheckAndRepair(GameObject root = null)
        {
            var mappings = DetectBrokenPaths(root);
            if (mappings.Count == 0) return 0;

            var fixedClipGuids = new HashSet<string>();
            foreach (var m in mappings)
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(m.clipAssetGuid);
                if (string.IsNullOrEmpty(clipPath)) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null) continue;

                ApplySingleMapping(clip, m);
                fixedClipGuids.Add(m.clipAssetGuid);
                Log($"  [{m.clipName}] {m.oldPath} -> {m.newPath}");
            }

            foreach (var guid in fixedClipGuids)
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip != null) EditorUtility.SetDirty(clip);
            }

            AssetDatabase.SaveAssets();
            Log($"已自动修复 {fixedClipGuids.Count} 个动画剪辑的路径引用");
            return fixedClipGuids.Count;
        }

        // ===== 导出 =====

        /// <summary>
        /// 导出修复映射到 JSON 文件
        /// </summary>
        public static string ExportMappings(string outputPath = null)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                Log("导出失败: 请先进入预制体编辑模式");
                return null;
            }

            var root = stage.prefabContentsRoot;
            var prefabPath = stage.assetPath;
            var mappings = DetectBrokenPaths(root);

            if (mappings.Count == 0)
            {
                Log("导出: 未发现需要修复的动画路径");
                return null;
            }

            var file = new AnimationRepairMappingFile
            {
                exportTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                prefabName = root.name,
                prefabAssetPath = prefabPath,
                exportedBy = System.Environment.UserName,
                mappings = mappings
            };

            if (string.IsNullOrEmpty(outputPath))
            {
                string dir = UIProbeStorage.GetAnimRepairPath();
                string fileName = $"AnimRepair_{root.name}_{System.DateTime.Now:yyyyMMdd_HHmmss}.json";
                outputPath = Path.Combine(dir, fileName);
            }

            string json = JsonUtility.ToJson(file, true);
            string dirPath = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
            File.WriteAllText(outputPath, json);

            Log($"已导出 {mappings.Count} 条修复映射 -> {outputPath}");
            EditorUtility.RevealInFinder(outputPath);
            return outputPath;
        }

        // ===== 导入 =====

        /// <summary>
        /// 导入 JSON 映射文件并应用修复
        /// </summary>
        public static int ApplyMappings(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                Log($"导入失败: 文件不存在 ({jsonPath})");
                return 0;
            }

            string json = File.ReadAllText(jsonPath);
            AnimationRepairMappingFile file;
            try
            {
                file = JsonUtility.FromJson<AnimationRepairMappingFile>(json);
            }
            catch (System.Exception e)
            {
                Log($"导入失败: JSON 解析错误 ({e.Message})");
                return 0;
            }

            if (file == null || file.mappings == null || file.mappings.Count == 0)
            {
                Log("导入: 文件中无有效修复映射");
                return 0;
            }

            var fixedClipGuids = new HashSet<string>();
            int appliedCount = 0;

            foreach (var m in file.mappings)
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(m.clipAssetGuid);
                if (string.IsNullOrEmpty(clipPath))
                {
                    Log($"  跳过 [{m.clipName}]: GUID 无效 ({m.clipAssetGuid})");
                    continue;
                }

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null)
                {
                    Log($"  跳过 [{m.clipName}]: 剪辑不存在 ({clipPath})");
                    continue;
                }

                ApplySingleMapping(clip, m);
                fixedClipGuids.Add(m.clipAssetGuid);
                appliedCount++;
                Log($"  应用 [{m.clipName}] {m.oldPath} -> {m.newPath}");
            }

            foreach (var guid in fixedClipGuids)
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip != null) EditorUtility.SetDirty(clip);
            }

            AssetDatabase.SaveAssets();
            Log($"导入完成: 应用了 {appliedCount} 条修复映射 (涉及 {fixedClipGuids.Count} 个剪辑)");
            return fixedClipGuids.Count;
        }

        // ===== 内部工具 =====

        private static void ApplySingleMapping(AnimationClip clip, AnimationPathMapping m)
        {
            if (m.bindingType == "objectReference")
            {
                var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                foreach (var b in bindings)
                {
                    if (b.path == m.oldPath && b.propertyName == m.propertyName)
                    {
                        var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, b);
                        AnimationUtility.SetObjectReferenceCurve(clip, b, null);
                        var newBinding = b;
                        newBinding.path = m.newPath;
                        AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keyframes);
                        break;
                    }
                }
            }
            else
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var b in bindings)
                {
                    if (b.path == m.oldPath && b.propertyName == m.propertyName)
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, b);
                        AnimationUtility.SetEditorCurve(clip, b, null);
                        var newBinding = b;
                        newBinding.path = m.newPath;
                        AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                        break;
                    }
                }
            }
        }

        private static void FindTransformsByName(Transform root, string name, List<Transform> results)
        {
            if (root.name == name)
                results.Add(root);
            for (int i = 0; i < root.childCount; i++)
                FindTransformsByName(root.GetChild(i), name, results);
        }

        private static void Log(string msg)
        {
            Debug.Log($"[UIProbe 动画修复] {msg}");
            OnRepairLog?.Invoke(msg);
        }
    }
}
