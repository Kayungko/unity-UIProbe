using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UIProbe.Core.Contract;
using UIProbe.Infrastructure.UnityAdapters;

namespace UIProbe.Core.Services
{
    /// <summary>
    /// 资源引用查询/导出(只读)。严格从 PrefabIndexService.Current 派生,不另存副本。
    /// 经 IFileSystem 接缝写 CSV,可在内存假体下单测。
    /// </summary>
    public sealed class AssetReferenceService
    {
        private const string ImageKind = "Image";
        private const string RawImageKind = "RawImage";

        private readonly PrefabIndexService _index;
        private readonly IFileSystem _fs;

        public AssetReferenceService(PrefabIndexService index, IFileSystem fs)
        {
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        }

        /// <summary>
        /// 基于 PrefabIndex 派生引用列表。至少一个查询维度否则 INVALID_PARAMS;
        /// 索引未构建 → ExecutionFailed 提示先 build;资源未被引用 → Success + 空列表(非错误)。
        /// Data 为 AssetReferenceResultSet 的 JSON。
        /// </summary>
        public ToolResult FindReferences(AssetReferenceQuery query)
        {
            query = query ?? new AssetReferenceQuery();

            if (IsEmpty(query.AssetPath) && IsEmpty(query.AssetName) && IsEmpty(query.Guid)
                && IsEmpty(query.SpriteName) && IsEmpty(query.ReferenceTypeFilter))
            {
                return Fail(ToolErrorCodes.InvalidParams,
                    "至少需提供 AssetPath/AssetName/Guid/SpriteName/ReferenceType 之一", false);
            }

            PrefabIndex index = _index.Current;
            if (index == null)
            {
                return Fail(ToolErrorCodes.ExecutionFailed,
                    "PrefabIndex 未构建,请先在「预制体索引」执行 build", true);
            }

            var results = new List<AssetReferenceResult>();
            foreach (PrefabIndexItem item in index.Items)
            {
                foreach (AssetRef r in item.ReferencedAssets)
                {
                    if (!Matches(query, r)) continue;
                    results.Add(new AssetReferenceResult
                    {
                        AssetPath = r.AssetPath,
                        PrefabPath = item.AssetPath,
                        NodePath = r.NodePath,
                        AssetName = r.AssetName,
                        ComponentType = r.Kind,
                        ReferenceType = r.Kind,
                        ExtraInfo = r.ExtraInfo
                    });
                }
            }

            results.Sort(CompareResults);

            var set = new AssetReferenceResultSet { Results = results };
            return new ToolResult { Status = ToolStatus.Success, Data = JsonUtility.ToJson(set) };
        }

        /// <summary>按同一 query 派生后写 CSV 到受控目录,Data 返回 reportPath;写失败 → IO_ERROR。</summary>
        public ToolResult ExportCsv(AssetReferenceQuery query, ExportCsvOptions options)
        {
            options = options ?? new ExportCsvOptions();
            if (IsEmpty(options.ReportPath))
            {
                return Fail(ToolErrorCodes.InvalidParams, "ReportPath 不能为空", false);
            }

            ToolResult found = FindReferences(query);
            if (found.Status != ToolStatus.Success) return found;

            AssetReferenceResultSet set = JsonUtility.FromJson<AssetReferenceResultSet>(found.Data);
            string csv = BuildCsv(set.Results);

            try
            {
                _fs.WriteAllText(options.ReportPath, csv);
            }
            catch (Exception ex)
            {
                return Fail(ToolErrorCodes.IoError, "写 CSV 失败: " + ex.Message, true);
            }

            return new ToolResult { Status = ToolStatus.Success, Data = options.ReportPath };
        }

        private static bool Matches(AssetReferenceQuery q, AssetRef r)
        {
            bool pathOk = IsEmpty(q.AssetPath) || Contains(r.AssetPath, q.AssetPath);
            bool nameOk = IsEmpty(q.AssetName) || Contains(r.AssetName, q.AssetName);
            bool guidOk = IsEmpty(q.Guid) || string.Equals(r.Guid, q.Guid, StringComparison.Ordinal);
            bool spriteOk = IsEmpty(q.SpriteName)
                || ((r.Kind == ImageKind || r.Kind == RawImageKind) && Contains(r.AssetName, q.SpriteName));
            bool typeOk = IsEmpty(q.ReferenceTypeFilter)
                || string.Equals(r.Kind, q.ReferenceTypeFilter, StringComparison.Ordinal);
            return pathOk && nameOk && guidOk && spriteOk && typeOk;
        }

        private static int CompareResults(AssetReferenceResult a, AssetReferenceResult b)
        {
            int c = string.CompareOrdinal(a.PrefabPath, b.PrefabPath);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.NodePath, b.NodePath);
            if (c != 0) return c;
            return string.CompareOrdinal(a.AssetPath, b.AssetPath);
        }

        private static string BuildCsv(IReadOnlyList<AssetReferenceResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ReferenceType,PrefabPath,NodePath,ComponentType,AssetName,AssetPath,ExtraInfo");
            foreach (AssetReferenceResult r in results)
            {
                sb.AppendLine(string.Join(",",
                    Escape(r.ReferenceType), Escape(r.PrefabPath), Escape(r.NodePath),
                    Escape(r.ComponentType), Escape(r.AssetName), Escape(r.AssetPath), Escape(r.ExtraInfo)));
            }
            return sb.ToString();
        }

        private static string Escape(string field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;
            if (field.IndexOf(',') >= 0 || field.IndexOf('"') >= 0
                || field.IndexOf('\n') >= 0 || field.IndexOf('\r') >= 0)
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsEmpty(string s) => string.IsNullOrEmpty(s);

        private static ToolResult Fail(string code, string message, bool retriable) => new ToolResult
        {
            Status = ToolStatus.Failed,
            Error = new ToolError { Code = code, Message = message, Retriable = retriable }
        };
    }

    /// <summary>引用查询条件。AssetPath/AssetName/Guid/SpriteName/ReferenceTypeFilter 至少一维度非空。</summary>
    public sealed class AssetReferenceQuery
    {
        public string AssetPath;
        public string AssetName;
        public string Guid;
        public string SpriteName;
        public string ReferenceTypeFilter;
    }

    /// <summary>单条引用派生结果。PrefabPath=持有引用的 prefab;其余字段来自 AssetRef。</summary>
    [Serializable]
    public sealed class AssetReferenceResult
    {
        public string AssetPath;
        public string PrefabPath;
        public string NodePath;
        public string AssetName;
        public string ComponentType;
        public string ReferenceType;
        public string ExtraInfo;
    }

    /// <summary>FindReferences 的可序列化结果集(JsonUtility 不直接序列化裸 List)。</summary>
    [Serializable]
    public sealed class AssetReferenceResultSet
    {
        public List<AssetReferenceResult> Results = new List<AssetReferenceResult>();
    }

    /// <summary>ExportCsv 选项。ReportPath 为受控目录下的目标 CSV 路径。</summary>
    public sealed class ExportCsvOptions
    {
        public string ReportPath;
    }
}
