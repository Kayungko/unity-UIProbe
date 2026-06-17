using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System;
using System.IO;

namespace UIProbe
{
    /// <summary>
    /// 后台更新检测器 (静默无忧)
    /// 每天最多检测一次 GitHub Release API
    /// </summary>
    [InitializeOnLoad]
    public static class UIProbeUpdateChecker
    {
        public const string VERSION = "3.10.0";
        private static readonly string[] API_URLS = {
            "https://api.github.com/repos/Kayungko/unity-UIProbe/releases/latest"
        };
        private const string LAST_CHECK_KEY = "UIProbe_LastUpdateCheck";
        
        public static bool HasUpdateAvailable { get; private set; }
        public static string LatestVersion { get; private set; }
        public static string ReleaseUrl { get; private set; }
        // 最新 Release 中 .unitypackage 资源的下载地址与文件名（用于编辑器内一键更新）
        public static string DownloadUrl { get; private set; }
        public static string DownloadFileName { get; private set; }

        static UIProbeUpdateChecker()
        {
            HasUpdateAvailable = false;
            // 延迟调用以免卡顿 Unity 的启动流程
            EditorApplication.delayCall += CheckForUpdatesIfNeeded;
        }

        private static void CheckForUpdatesIfNeeded()
        {
            try
            {
                string lastCheckStr = EditorPrefs.GetString(LAST_CHECK_KEY, "");
                if (long.TryParse(lastCheckStr, out long lastCheckTicks))
                {
                    DateTime lastCheck = new DateTime(lastCheckTicks);
                    // 限制每 24 小时仅检测一次
                    if ((DateTime.Now - lastCheck).TotalHours < 24)
                        return;
                }
            }
            catch { }

            PerformCheck();
        }

        public static void PerformCheck(Action<bool, string> onComplete = null)
        {
            TryGetReleaseInfo(0, onComplete);
        }

        private static void TryGetReleaseInfo(int urlIndex, Action<bool, string> onComplete)
        {
            if (urlIndex >= API_URLS.Length)
            {
                onComplete?.Invoke(false, "检查失败：网络连接超时或所有镜像节点均受到限制");
                return;
            }

            var request = UnityWebRequest.Get(API_URLS[urlIndex]);
            
            // 为了防止无 token 请求被过度限制，主节点提供5秒短超时，备用加速节点给8秒长超时
            request.timeout = urlIndex == 0 ? 5 : 8; 
            // GitHub API 强制要求设置 User-Agent
            request.SetRequestHeader("User-Agent", "unity-UIProbe-UpdateChecker");
            
            var op = request.SendWebRequest();
            op.completed += (asyncOp) =>
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var info = JsonUtility.FromJson<GitHubReleaseInfo>(request.downloadHandler.text);
                        if (info != null && !string.IsNullOrEmpty(info.tag_name))
                        {
                            // 极简过滤：v3.3.0-alpha -> 3.3.0
                            string remoteVersionStr = info.tag_name.Replace("v", "").Replace("V", "").Split('-')[0].Trim();
                            string localVersionStr = VERSION.Split('-')[0].Trim();
                            
                            Version remoteVersion = new Version(remoteVersionStr);
                            Version localVersion = new Version(localVersionStr);

                            if (remoteVersion > localVersion)
                            {
                                HasUpdateAvailable = true;
                                LatestVersion = info.tag_name;
                                ReleaseUrl = !string.IsNullOrEmpty(info.html_url) ? info.html_url : "https://github.com/Kayungko/unity-UIProbe/releases";

                                // 在 Release 资源中定位 .unitypackage，供编辑器内一键更新使用
                                DownloadUrl = null;
                                DownloadFileName = null;
                                if (info.assets != null)
                                {
                                    foreach (var asset in info.assets)
                                    {
                                        if (asset != null && !string.IsNullOrEmpty(asset.name) &&
                                            asset.name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase) &&
                                            !string.IsNullOrEmpty(asset.browser_download_url))
                                        {
                                            DownloadUrl = asset.browser_download_url;
                                            DownloadFileName = asset.name;
                                            break;
                                        }
                                    }
                                }

                                onComplete?.Invoke(true, $"发现新版本：{info.tag_name}\n\n当前版本：v{VERSION}");
                            }
                            else
                            {
                                onComplete?.Invoke(false, "当前已是最新版！无可用更新。");
                            }
                            
                            // 探测成功后才更新时间戳
                            EditorPrefs.SetString(LAST_CHECK_KEY, DateTime.Now.Ticks.ToString());
                        }
                        else
                        {
                            // 尝试换节解析（可能是中间人代理投递了广告页）
                            TryGetReleaseInfo(urlIndex + 1, onComplete);
                        }
                    }
                    catch (Exception)
                    {
                        // JSON 解析或版本号比对失败等异常：进入下个备用节点池重试
                        TryGetReleaseInfo(urlIndex + 1, onComplete);
                    }
                }
                else
                {
                    // 当前节点失败，尝试下一个备用节点
                    TryGetReleaseInfo(urlIndex + 1, onComplete);
                }
                
                request.Dispose();
            };
        }

        /// <summary>
        /// 编辑器内一键更新：下载最新 .unitypackage 并触发导入。
        /// 任意环节失败时通过 onComplete(false, msg) 上报，由调用方回退到浏览器下载。
        /// 全程不依赖 git/CLI，也不要求工程绑定 GitHub 仓库。
        /// </summary>
        public static void DownloadAndImportUpdate(Action<bool, string> onComplete = null)
        {
            if (string.IsNullOrEmpty(DownloadUrl))
            {
                onComplete?.Invoke(false, "未在 Release 中找到可下载的 .unitypackage，请前往下载页手动下载。");
                return;
            }

            string fileName = string.IsNullOrEmpty(DownloadFileName) ? "unity-UIProbe-update.unitypackage" : DownloadFileName;
            var request = UnityWebRequest.Get(DownloadUrl);
            request.SetRequestHeader("User-Agent", "unity-UIProbe-UpdateChecker");
            request.timeout = 120; // 安装包可能较大，给足下载时间
            var op = request.SendWebRequest();

            EditorApplication.CallbackFunction progressUpdater = null;
            progressUpdater = () =>
            {
                if (!op.isDone)
                {
                    bool cancel = EditorUtility.DisplayCancelableProgressBar(
                        "UIProbe 自动更新",
                        $"正在下载 {LatestVersion} ... {(request.downloadProgress * 100f):F0}%",
                        request.downloadProgress);
                    if (cancel)
                        request.Abort();
                    return;
                }

                EditorApplication.update -= progressUpdater;
                EditorUtility.ClearProgressBar();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string tempPath = Path.Combine(Path.GetTempPath(), fileName);
                        File.WriteAllBytes(tempPath, request.downloadHandler.data);
                        request.Dispose();
                        // interactive=true：弹出 Unity 原生导入窗口，由用户确认覆盖文件
                        AssetDatabase.ImportPackage(tempPath, true);
                        onComplete?.Invoke(true, $"已下载 {LatestVersion}，请在弹出的导入窗口中确认导入。");
                    }
                    catch (Exception e)
                    {
                        request.Dispose();
                        onComplete?.Invoke(false, "下载成功但导入失败：" + e.Message);
                    }
                }
                else
                {
                    string err = request.error;
                    request.Dispose();
                    onComplete?.Invoke(false, "下载失败：" + (string.IsNullOrEmpty(err) ? "已取消或网络异常" : err));
                }
            };
            EditorApplication.update += progressUpdater;
        }

        [Serializable]
        private class GitHubReleaseInfo
        {
            public string tag_name;
            public string html_url;
            public string body;
            public GitHubAsset[] assets;
        }

        [Serializable]
        private class GitHubAsset
        {
            public string name;
            public string browser_download_url;
        }
    }
}
