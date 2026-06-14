using System;
using System.Collections.Generic;
using System.IO;
using UIProbe.Infrastructure.UnityAdapters;

namespace UIProbe.Editor.Infrastructure
{
    /// <summary>IFileSystem 的生产实现,真实调用 System.IO.File。</summary>
    public sealed class UnityFileSystem : IFileSystem
    {
        // backup token -> 原始路径。备份内容存放在 token 指向的 .bak 文件中。
        private readonly Dictionary<string, string> _backupOrigins = new Dictionary<string, string>();

        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public void WriteAllText(string path, string contents)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, contents);
        }

        public bool Exists(string path)
        {
            return File.Exists(path);
        }

        public string Backup(string path)
        {
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            var token = path + "." + Guid.NewGuid().ToString("N") + ".bak";
            File.Copy(path, token, overwrite: true);
            _backupOrigins[token] = path;
            return token;
        }

        public void Restore(string backupToken)
        {
            if (string.IsNullOrEmpty(backupToken) || !_backupOrigins.TryGetValue(backupToken, out var origin))
            {
                throw new ArgumentException("Unknown backup token: " + backupToken, nameof(backupToken));
            }

            File.Copy(backupToken, origin, overwrite: true);
        }
    }
}
