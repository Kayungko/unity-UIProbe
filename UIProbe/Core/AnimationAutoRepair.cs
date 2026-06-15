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
        private static TransformPathSnapshot lastSnapshot;

        private class TransformPathSnapshot
        {
            public string PrefabAssetPath;
            public int RootInstanceId;
            public Dictionary<int, string> PathsByInstanceId = new Dictionary<int, string>();
        }

        public static void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;

            RegisterCallbacks();
            if (lastSnapshot == null)
                CaptureCurrentPrefabSnapshot();

            if (enabled)
            {
                Log("动画路径自动修复已启用");
            }
            else
            {
                isPending = false;
                Log("动画路径自动修复已停用");
            }
        }

        public static void Initialize()
        {
            Initialize(IsEnabled);
        }

        public static void Initialize(bool enabled)
        {
            IsEnabled = enabled;
            RegisterCallbacks();
            CaptureCurrentPrefabSnapshot();
        }

        public static void Shutdown()
        {
            UnregisterCallbacks();
            isPending = false;
            lastSnapshot = null;
        }

        private static void RegisterCallbacks()
        {
            UnregisterCallbacks();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
        }

        private static void UnregisterCallbacks()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
        }

        private static void OnPrefabStageOpened(PrefabStage stage)
        {
            EditorApplication.delayCall += CaptureCurrentPrefabSnapshot;
        }

        private static void OnPrefabStageClosing(PrefabStage stage)
        {
            if (lastSnapshot != null && stage != null && lastSnapshot.PrefabAssetPath == stage.assetPath)
                lastSnapshot = null;
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

        private static void CaptureCurrentPrefabSnapshot()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null)
            {
                lastSnapshot = null;
                return;
            }

            lastSnapshot = CaptureSnapshot(stage.prefabContentsRoot, stage.assetPath);
        }

        private static TransformPathSnapshot CaptureSnapshot(GameObject root, string prefabAssetPath)
        {
            var snapshot = new TransformPathSnapshot
            {
                PrefabAssetPath = prefabAssetPath,
                RootInstanceId = root.GetInstanceID()
            };

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                snapshot.PathsByInstanceId[t.GetInstanceID()] = AnimationPathRepair.GetRelativePath(root.transform, t);
            }

            return snapshot;
        }

        // ===== 检测 =====

        /// <summary>
        /// 检测当前预制体中失效的动画绑定路径，不执行修复
        /// </summary>
        public static List<AnimationPathMapping> DetectBrokenPaths(GameObject root = null)
        {
            var result = new List<AnimationPathMapping>();
            string prefabAssetPath = "";

            if (root == null)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null) return result;
                root = stage.prefabContentsRoot;
                prefabAssetPath = stage.assetPath;
            }

            var snapshotMappings = DetectPathChangesFromSnapshot(root, prefabAssetPath);
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
                    var mapping = TryDetectBinding(clip, binding, root.transform, rootTransform, "float", clipGuid, snapshotMappings);
                    if (mapping != null)
                    {
                        mapping.componentName = component.name;
                        result.Add(mapping);
                    }
                }

                // Object reference curves
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var mapping = TryDetectBinding(clip, binding, root.transform, rootTransform, "objectReference", clipGuid, snapshotMappings);
                    if (mapping != null)
                    {
                        mapping.componentName = component.name;
                        result.Add(mapping);
                    }
                }
            }

            return result;
        }

        private static Dictionary<string, string> DetectPathChangesFromSnapshot(GameObject root, string prefabAssetPath)
        {
            var result = new Dictionary<string, string>();
            if (root == null || lastSnapshot == null) return result;
            if (lastSnapshot.RootInstanceId != root.GetInstanceID()) return result;
            if (!string.IsNullOrEmpty(lastSnapshot.PrefabAssetPath)
                && !string.IsNullOrEmpty(prefabAssetPath)
                && lastSnapshot.PrefabAssetPath != prefabAssetPath)
                return result;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int id = t.GetInstanceID();
                if (!lastSnapshot.PathsByInstanceId.TryGetValue(id, out string oldPath))
                    continue;

                string newPath = AnimationPathRepair.GetRelativePath(root.transform, t);
                if (oldPath != newPath && !result.ContainsKey(oldPath))
                    result.Add(oldPath, newPath);
            }

            return result;
        }

        private static AnimationPathMapping TryDetectBinding(
            AnimationClip clip,
            EditorCurveBinding binding,
            Transform prefabRoot,
            Transform animationRoot,
            string bindingType,
            string clipGuid,
            Dictionary<string, string> snapshotMappings)
        {
            if (string.IsNullOrEmpty(binding.path)) return null;

            Transform found = animationRoot.Find(binding.path);
            if (found != null) return null; // 路径有效

            if (TryCreateMappingFromSnapshot(binding, prefabRoot, animationRoot, bindingType, clipGuid, snapshotMappings, out var snapshotMapping))
            {
                snapshotMapping.clipName = clip.name;
                return snapshotMapping;
            }

            string nodeName = binding.path;
            int lastSlash = binding.path.LastIndexOf('/');
            if (lastSlash >= 0)
                nodeName = binding.path.Substring(lastSlash + 1);

            var matches = new List<Transform>();
            FindTransformsByName(animationRoot, nodeName, matches);

            if (matches.Count == 1)
            {
                string newPath = AnimationPathRepair.GetRelativePath(animationRoot, matches[0]);
                if (newPath == binding.path) return null;

                return new AnimationPathMapping
                {
                    status = "resolved",
                    clipAssetGuid = clipGuid,
                    clipName = clip.name,
                    oldPath = binding.path,
                    newPath = newPath,
                    bindingType = bindingType,
                    propertyName = binding.propertyName
                };
            }

            // 节点已删除: 按名称找不到任何匹配
            return new AnimationPathMapping
            {
                status = "unresolved",
                unresolvedNote = (matches.Count > 1)
                    ? $"存在 {matches.Count} 个同名节点 \"{nodeName}\"，无法确定目标"
                    : $"节点 \"{nodeName}\" 已被删除，无法自动修复",
                clipAssetGuid = clipGuid,
                clipName = clip.name,
                oldPath = binding.path,
                newPath = "",
                bindingType = bindingType,
                propertyName = binding.propertyName
            };
        }

        private static bool TryCreateMappingFromSnapshot(
            EditorCurveBinding binding,
            Transform prefabRoot,
            Transform animationRoot,
            string bindingType,
            string clipGuid,
            Dictionary<string, string> snapshotMappings,
            out AnimationPathMapping mapping)
        {
            mapping = null;
            if (snapshotMappings == null || snapshotMappings.Count == 0) return false;
            if (prefabRoot == null || animationRoot == null) return false;

            string currentAnimationRootPath = AnimationPathRepair.GetRelativePath(prefabRoot, animationRoot);
            string oldAnimationRootPath = currentAnimationRootPath;
            if (lastSnapshot != null
                && lastSnapshot.PathsByInstanceId.TryGetValue(animationRoot.GetInstanceID(), out string oldRootPath))
            {
                oldAnimationRootPath = oldRootPath;
            }

            string oldFullPath = CombinePrefabPath(oldAnimationRootPath, binding.path);
            if (!snapshotMappings.TryGetValue(oldFullPath, out string newFullPath))
                return false;

            string newBindingPath = MakeRelativeBindingPath(currentAnimationRootPath, newFullPath);
            if (newBindingPath == null || newBindingPath == binding.path)
                return false;

            mapping = new AnimationPathMapping
            {
                clipAssetGuid = clipGuid,
                oldPath = binding.path,
                newPath = newBindingPath,
                bindingType = bindingType,
                propertyName = binding.propertyName
            };
            return true;
        }

        private static string CombinePrefabPath(string rootPath, string childPath)
        {
            if (string.IsNullOrEmpty(rootPath)) return childPath ?? "";
            if (string.IsNullOrEmpty(childPath)) return rootPath;
            return rootPath + "/" + childPath;
        }

        private static string MakeRelativeBindingPath(string rootPath, string fullPath)
        {
            if (fullPath == null) return null;
            if (string.IsNullOrEmpty(rootPath)) return fullPath;
            if (fullPath == rootPath) return "";
            string prefix = rootPath + "/";
            if (!fullPath.StartsWith(prefix)) return null;
            return fullPath.Substring(prefix.Length);
        }

        // ===== 自动修复 =====

        public static int CheckAndRepair(GameObject root = null)
        {
            GameObject repairRoot = root;
            if (repairRoot == null)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null) repairRoot = stage.prefabContentsRoot;
            }

            var allMappings = DetectBrokenPaths(root);
            var resolvedOnly = allMappings.Where(m => m.status == "resolved").ToList();
            var unresolvedOnly = allMappings.Where(m => m.status == "unresolved").ToList();

            if (resolvedOnly.Count == 0 && unresolvedOnly.Count == 0)
            {
                if (repairRoot != null) CaptureSnapshotForRoot(repairRoot);
                return 0;
            }

            int fixedCount = 0;
            var fixedClipGuids = new HashSet<string>();
            foreach (var m in resolvedOnly)
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(m.clipAssetGuid);
                if (string.IsNullOrEmpty(clipPath)) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null) continue;

                ApplySingleMapping(clip, m);
                fixedClipGuids.Add(m.clipAssetGuid);
                fixedCount++;
                Log($"  [{m.clipName}] {m.oldPath} -> {m.newPath}");
            }

            if (unresolvedOnly.Count > 0)
            {
                Log($"⚠ {unresolvedOnly.Count} 条引用无法自动修复 (节点已删除):");
                foreach (var m in unresolvedOnly)
                    Log($"  [{m.clipName}] {m.oldPath} — {m.unresolvedNote}");
            }

            foreach (var guid in fixedClipGuids)
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip != null) EditorUtility.SetDirty(clip);
            }

            if (fixedCount > 0)
            {
                AssetDatabase.SaveAssets();
                Log($"已自动修复 {fixedCount} 个动画剪辑的路径引用");
            }

            if (repairRoot != null) CaptureSnapshotForRoot(repairRoot);
            return fixedCount;
        }

        private static void CaptureSnapshotForRoot(GameObject root)
        {
            if (root == null) return;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            string prefabAssetPath = stage != null && stage.prefabContentsRoot == root ? stage.assetPath : "";
            lastSnapshot = CaptureSnapshot(root, prefabAssetPath);
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

            int resolved = mappings.Count(m => m.status == "resolved");
            int unresolved = mappings.Count(m => m.status == "unresolved");

            var file = new AnimationRepairMappingFile
            {
                exportTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                prefabName = root.name,
                prefabAssetPath = prefabPath,
                exportedBy = System.Environment.UserName,
                resolvedCount = resolved,
                unresolvedCount = unresolved,
                mappings = mappings
            };

            Log(resolved > 0
                ? $"已导出 {resolved} 条可修复 + {unresolved} 条需人工处理 -> {outputPath}"
                : $"已导出 {unresolved} 条需人工处理 (无可自动修复项) -> {outputPath}");

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
            int skippedCount = 0;

            foreach (var m in file.mappings)
            {
                if (m.status == "unresolved")
                {
                    skippedCount++;
                    Log($"  跳过 [{m.clipName}] {m.oldPath}: {m.unresolvedNote}");
                    continue;
                }

                string clipPath = AssetDatabase.GUIDToAssetPath(m.clipAssetGuid);
                if (string.IsNullOrEmpty(clipPath))
                {
                    skippedCount++;
                    Log($"  跳过 [{m.clipName}]: GUID 无效 ({m.clipAssetGuid})");
                    continue;
                }

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null)
                {
                    skippedCount++;
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
            string summary = $"导入完成: 应用 {appliedCount} 条";
            if (skippedCount > 0) summary += $", 跳过 {skippedCount} 条 (需人工处理)";
            Log(summary);
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
