using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System;

namespace UIProbe
{
    /// <summary>
    /// 后台更新检测器 (静默无忧)
    /// 每天最多检测一次 GitHub Release API
    /// </summary>
    [InitializeOnLoad]
    public static class UIProbeUpdateChecker
    {
        public const string VERSION = "3.1.0";
        private static readonly string[] API_URLS = {
            "https://api.github.com/repos/Kayungko/unity-UIProbe/releases/latest"
        };
        private const string LAST_CHECK_KEY = "UIProbe_LastUpdateCheck";
        
        public static bool HasUpdateAvailable { get; private set; }
        public static string LatestVersion { get; private set; }
        public static string ReleaseUrl { get; private set; }

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
                            // 极简过滤：v3.1.0-alpha -> 3.1.0
                            string remoteVersionStr = info.tag_name.Replace("v", "").Replace("V", "").Split('-')[0].Trim();
                            string localVersionStr = VERSION.Split('-')[0].Trim();
                            
                            Version remoteVersion = new Version(remoteVersionStr);
                            Version localVersion = new Version(localVersionStr);

                            if (remoteVersion > localVersion)
                            {
                                HasUpdateAvailable = true;
                                LatestVersion = info.tag_name;
                                ReleaseUrl = !string.IsNullOrEmpty(info.html_url) ? info.html_url : "https://github.com/Kayungko/unity-UIProbe/releases";
                                
                                onComplete?.Invoke(true, $"发现新版本：{info.tag_name}\n\n是否立即前往下载？");
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

        [Serializable]
        private class GitHubReleaseInfo
        {
            public string tag_name;
            public string html_url;
            public string body;
        }
    }
}
