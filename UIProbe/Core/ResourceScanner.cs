using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UIProbe
{
    public class ResourceScanner
    {
        private float scanProgress = 0f;
        public float Progress => scanProgress;

        public async Task<ScanResult> ScanAsync(ScanOptions options, IProgress<float> progress = null)
        {
            var result = new ScanResult();
            result.ScanTime = DateTime.Now;

            // 1. 收集目标资源
            List<string> targetAssets = CollectTargetAssets(options);
            
            if (targetAssets.Count == 0)
                return result;

            // 2. 收集项目中的所有潜在引用者 (Prefab, Scene, Material, etc.)
            List<string> referrerAssets = CollectReferrerAssets(options);

            // 3. 构建依赖图 (耗时操作，需异步)
            // Key: 依赖 (被引用的资源), Value: 引用者列表
            Dictionary<string, HashSet<string>> referenceMap = await BuildReferenceMapAsync(referrerAssets, progress);

            // 4. 分析结果
            AnalyzeResults(targetAssets, referenceMap, result, options);

            return result;
        }

        private List<string> CollectTargetAssets(ScanOptions options)
        {
            HashSet<string> assets = new HashSet<string>();
            string[] searchTypes = new string[] { };
            
            if (options.IncludeSprites && options.IncludeTextures)
                searchTypes = new[] { "t:Sprite", "t:Texture2D" };
            else if (options.IncludeSprites)
                searchTypes = new[] { "t:Sprite" };
            else if (options.IncludeTextures)
                searchTypes = new[] { "t:Texture2D" };

            if (searchTypes.Length == 0)
                return new List<string>();

            // 在指定文件夹中搜索
            string[] guids = AssetDatabase.FindAssets(string.Join(" ", searchTypes), options.TargetFolders.ToArray());
            
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                
                // 排除文件夹检查
                if (IsExcluded(path, options.ExcludeFolders))
                    continue;
                    
                assets.Add(path);
            }

            return assets.ToList();
        }

        private bool IsExcluded(string path, List<string> excludeFolders)
        {
            if (excludeFolders == null || excludeFolders.Count == 0)
                return false;
                
            foreach (var exclude in excludeFolders)
            {
                if (path.StartsWith(exclude))
                    return true;
            }
            return false;
        }

        private List<string> CollectReferrerAssets(ScanOptions options)
        {
            List<string> filters = new List<string>();
            
            if (options.CheckPrefabs) filters.Add("t:Prefab");
            if (options.CheckScenes) filters.Add("t:Scene");
            if (options.CheckMaterials) filters.Add("t:Material");
            if (options.CheckAnimations) filters.Add("t:AnimationClip"); // AnimationClip usually references sprites
            if (options.CheckParticles) filters.Add("t:GameObject"); // Particles are usually on GameObjects/Prefabs, t:Prefab covers most, but maybe checking specific controller assets if needed? 
            // Stick to standard types for now. Particles are checked via Prefabs mostly. 
            
            // Controller for Animator
            if (options.CheckAnimations) filters.Add("t:AnimatorController");

            if (filters.Count == 0)
                return new List<string>();

            // fix: FindAssets filter string syntax "t:Type1 t:Type2" means AND, not OR. But we need OR.
            // Actuall AssetDatabase.FindAssets with filter string searches for ALL given filters combined? 
            // "t:Type1 t:Type2" finds assets that match both type1 and type2? No, documentation says "The filter string can contain search data... 't:Texture2D' ... 'l:MyLabel' ... 'MyName' ".
            // Actually multiple types in one string works as OR if separated by space? No, usually separate calls or combine results.
            // Let's do separate calls for safety.
            
            HashSet<string> allReferrers = new HashSet<string>();
            
            foreach(var filter in filters)
            {
                string[] guids = AssetDatabase.FindAssets(filter);
                foreach (var guid in guids)
                {
                    allReferrers.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
            }

            return allReferrers.ToList();
        }

        private async Task<Dictionary<string, HashSet<string>>> BuildReferenceMapAsync(List<string> referrers, IProgress<float> progress)
        {
            // 1. Load Forward Map (Referrer -> Dependencies)
            Dictionary<string, HashSet<string>> forwardMap = null;
            DateTime cacheTime = DateTime.MinValue;
            string cachePath = ResourceCacheManager.GetCacheFilePath();

            // Try load cache
            if (File.Exists(cachePath))
            {
                forwardMap = ResourceCacheManager.LoadDependencyCache();
                if (forwardMap != null)
                {
                    cacheTime = File.GetLastWriteTime(cachePath);
                }
            }

            if (forwardMap == null) forwardMap = new Dictionary<string, HashSet<string>>();

            // 2. Scan
            int total = referrers.Count;
            int batchSize = 20; // Increase frequency of updates slightly
            int processedCount = 0;

            // In Editor, we can only run AssetDatabase calls on main thread. 
            // We split work over multiple frames.
            
            // Limit per frame time
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long maxMillisecondsPerFrame = 16; 

            for (int i = 0; i < total; i++)
            {
                string referrerPath = referrers[i];
                bool needScan = true;

                // Check cache validity
                if (forwardMap.ContainsKey(referrerPath))
                {
                     // If file not modified since cache save
                     if (File.Exists(referrerPath) && File.GetLastWriteTime(referrerPath) <= cacheTime)
                     {
                         needScan = false;
                     }
                }

                if (needScan)
                {
                    string[] deps = AssetDatabase.GetDependencies(referrerPath, true);
                    forwardMap[referrerPath] = new HashSet<string>(deps);
                }

                processedCount++;
                
                // Yield control to keep editor responsive
                if (stopwatch.ElapsedMilliseconds > maxMillisecondsPerFrame)
                {
                    progress?.Report((float)i / total);
                    await Task.Delay(1);
                    stopwatch.Restart();
                }
            }
            
            // 3. Save Cache (Save the Forward Map)
            // Cleanup stale keys (assets no longer in project/scope)
            var currentReferrerSet = new HashSet<string>(referrers);
            var keys = forwardMap.Keys.ToList();
            bool cacheDirty = false;
            foreach(var key in keys)
            {
                if (!currentReferrerSet.Contains(key))
                {
                    forwardMap.Remove(key);
                    cacheDirty = true;
                }
            }

            // Always save to ensure timestamp is updated for next run, 
            // or we can optimize to save only if dirty or if scanned new items.
            // For simplicity and correctness of timestamp check, saving is safer.
            ResourceCacheManager.SaveDependencyCache(forwardMap);

            // 4. Invert to Reverse Map (Dependency -> Referrers)
            var reverseMap = new Dictionary<string, HashSet<string>>();
            
            int invertCount = 0;
            stopwatch.Restart();
            
            foreach (var kvp in forwardMap)
            {
                string referrer = kvp.Key;
                foreach (var dep in kvp.Value)
                {
                    // Filter self-reference
                    if (dep == referrer) continue;
                    
                    if (!reverseMap.ContainsKey(dep)) reverseMap[dep] = new HashSet<string>();
                    reverseMap[dep].Add(referrer);
                }
                
                invertCount++;
                if (stopwatch.ElapsedMilliseconds > maxMillisecondsPerFrame)
                {
                    await Task.Delay(1);
                    stopwatch.Restart();
                }
            }
            
            return reverseMap;
        }

        private void AnalyzeResults(List<string> targetAssets, Dictionary<string, HashSet<string>> referenceMap, ScanResult result, ScanOptions options)
        {
            Dictionary<string, FolderStatistics> folderStatsMap = new Dictionary<string, FolderStatistics>();

            foreach (var assetPath in targetAssets)
            {
                var info = new ResourceInfo
                {
                    AssetPath = assetPath,
                    AssetName = Path.GetFileNameWithoutExtension(assetPath),
                    FileSize = GetFileSize(assetPath),
                    AssetType = assetPath.EndsWith(".png") || assetPath.EndsWith(".jpg") ? "Texture/Sprite" : "Other", // 简化类型判断
                    IsUsed = false
                };

                if (referenceMap.ContainsKey(assetPath))
                {
                    var referrers = referenceMap[assetPath];
                    if (referrers.Count > 0)
                    {
                        info.IsUsed = true;
                        foreach (var refPath in referrers)
                        {
                            info.References.Add(new ReferenceInfo
                            {
                                ReferrerPath = refPath,
                                ReferrerType = GetAssetType(refPath),
                                ComponentType = "Unknown" // 获取组件类型比较耗时，暂时标记未知，后续优化
                            });
                        }
                    }
                }
                
                result.Resources.Add(info);

                // 统计
                UpdateStatistics(result, info, folderStatsMap);
            }
            
            result.FolderStats = folderStatsMap.Values.ToList();
        }

        private long GetFileSize(string path)
        {
            try
            {
                // AssetPath is relative to project root, need system path?
                // Actually FileInfo needs absolute path or relative to working dir (project root)
                // Unity paths work if we convert them? 
                // Let's assuming working directory is project root which is standard for Unity Editor.
                if(File.Exists(path))
                    return new FileInfo(path).Length;
            }
            catch {}
            return 0;
        }

        private string GetAssetType(string path)
        {
            if (path.EndsWith(".prefab")) return "Prefab";
            if (path.EndsWith(".unity")) return "Scene";
            if (path.EndsWith(".mat")) return "Material";
            if (path.EndsWith(".anim")) return "Animation";
            if (path.EndsWith(".controller")) return "Animator";
            return "Other";
        }

        private void UpdateStatistics(ScanResult result, ResourceInfo info, Dictionary<string, FolderStatistics> folderStatsMap)
        {
            string folder = Path.GetDirectoryName(info.AssetPath).Replace("\\", "/");
            
            if (!folderStatsMap.ContainsKey(folder))
            {
                folderStatsMap[folder] = new FolderStatistics { FolderPath = folder };
            }

            var stats = folderStatsMap[folder];
            stats.TotalCount++;
            stats.TotalSize += info.FileSize;

            result.Overall.TotalCount++;
            result.Overall.TotalSize += info.FileSize;

            if (info.IsUsed)
            {
                stats.UsedCount++;
                result.Overall.UsedCount++;
            }
            else
            {
                stats.UnusedCount++;
                stats.UnusedSize += info.FileSize;
                result.Overall.UnusedCount++;
                result.Overall.UnusedSize += info.FileSize;
            }
        }
    }
}
