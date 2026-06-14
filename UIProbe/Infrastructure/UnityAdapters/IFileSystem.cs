namespace UIProbe.Infrastructure.UnityAdapters
{
    /// <summary>
    /// 文件读写 / 备份 / 存在性检查的接缝,替代业务层直接调用 System.IO.File。
    /// 写路径将来受 authorization 模块 write_allow/write_deny 约束(本接口不接授权)。
    /// </summary>
    public interface IFileSystem
    {
        string ReadAllText(string path);

        void WriteAllText(string path, string contents);

        bool Exists(string path);

        /// <summary>
        /// 覆盖前备份指定文件,返回可用于撤销的 backup token(对应 UndoCapability.FileBackup)。
        /// 目标文件不存在时返回空字符串(无需备份)。
        /// </summary>
        string Backup(string path);

        /// <summary>用 backup token 还原到备份时的内容。token 无效时抛出。</summary>
        void Restore(string backupToken);
    }
}
