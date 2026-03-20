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
        private const string API_URL = "https://api.github.com/repos/Kayungko/unity-UIProbe/releases/latest";
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

        public static void PerformCheck()
        {
            var request = UnityWebRequest.Get(API_URL);
            
            // 为了防止无 token 请求被过度限制，可以设置短超时
            request.timeout = 5; 
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
                            }
                            
                            // 探测成功后才更新时间戳
                            EditorPrefs.SetString(LAST_CHECK_KEY, DateTime.Now.Ticks.ToString());
                        }
                    }
                    catch (Exception)
                    {
                        // JSON 解析或版本号比对失败等异常：静默吞弃，决不干扰用户
                    }
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
