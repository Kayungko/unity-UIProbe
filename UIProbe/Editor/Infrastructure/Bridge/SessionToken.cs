using System;
using System.IO;
using System.Security.Cryptography;

namespace UIProbe.Editor.Infrastructure.Bridge
{
    /// <summary>
    /// Bridge 启动时生成的一次性会话令牌:写入仅当前用户可访问目录下的文件,/rpc 校验请求头,
    /// 防同机其它进程直连 loopback 端口。令牌仅存活于本进程,Domain Reload 后由 Bridge 重新生成。
    /// </summary>
    public sealed class SessionToken
    {
        /// <summary>/rpc 校验的请求头名。</summary>
        public const string HeaderName = "X-UIProbe-Token";

        private const string FileName = "session.token";

        public string Value { get; private set; }
        public string FilePath { get; private set; }

        private SessionToken() { }

        /// <summary>
        /// 生成密码学随机令牌并写入 directory/session.token。
        /// directory 由调用方选在仅当前用户可访问的目录(用户级临时/本地数据目录),
        /// 依赖其默认 ACL 实现"仅当前用户可读";更细粒度 ACL 收紧留后续。
        /// </summary>
        public static SessionToken Create(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentNullException(nameof(directory));
            Directory.CreateDirectory(directory);

            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            string value = BitConverter.ToString(bytes).Replace("-", string.Empty);

            string path = Path.Combine(directory, FileName);
            File.WriteAllText(path, value);

            return new SessionToken { Value = value, FilePath = path };
        }

        /// <summary>请求头令牌是否与本会话令牌匹配(定长比较,降低时序侧信道)。</summary>
        public bool Matches(string candidate)
        {
            if (string.IsNullOrEmpty(candidate) || Value == null || candidate.Length != Value.Length)
            {
                return false;
            }
            int diff = 0;
            for (int i = 0; i < Value.Length; i++)
            {
                diff |= Value[i] ^ candidate[i];
            }
            return diff == 0;
        }
    }
}
