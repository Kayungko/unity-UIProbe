using System;
using System.Collections.Generic;
using UIProbe.Infrastructure.UnityAdapters;

namespace UIProbe.Tests.Editor.Fakes
{
    /// <summary>IFileSystem 的内存假体,用 Dictionary 模拟文件表,不依赖 Unity 运行环境。</summary>
    public sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
        private readonly Dictionary<string, (string Origin, string Content)> _backups
            = new Dictionary<string, (string, string)>();

        /// <summary>可控数据规模上限。null 表示不限制;超过上限时 WriteAllText 抛出。</summary>
        public int? MaxEntries { get; set; }

        public int Count => _files.Count;

        public void Seed(string path, string contents)
        {
            _files[path] = contents;
        }

        public string ReadAllText(string path)
        {
            if (!_files.TryGetValue(path, out var contents))
            {
                throw new System.IO.FileNotFoundException(path);
            }
            return contents;
        }

        public void WriteAllText(string path, string contents)
        {
            if (!_files.ContainsKey(path) && MaxEntries.HasValue && _files.Count >= MaxEntries.Value)
            {
                throw new InvalidOperationException("InMemoryFileSystem capacity exceeded: " + MaxEntries.Value);
            }
            _files[path] = contents;
        }

        public bool Exists(string path)
        {
            return _files.ContainsKey(path);
        }

        public string Backup(string path)
        {
            if (!_files.TryGetValue(path, out var contents))
            {
                return string.Empty;
            }

            var token = Guid.NewGuid().ToString("N");
            _backups[token] = (path, contents);
            return token;
        }

        public void Restore(string backupToken)
        {
            if (string.IsNullOrEmpty(backupToken) || !_backups.TryGetValue(backupToken, out var snapshot))
            {
                throw new ArgumentException("Unknown backup token: " + backupToken, nameof(backupToken));
            }
            _files[snapshot.Origin] = snapshot.Content;
        }
    }
}
