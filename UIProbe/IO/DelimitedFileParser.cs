using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace UIProbe
{
    /// <summary>
    /// 通用 CSV/TSV 分隔文件读写器，支持 RFC-4180 引号规则
    /// </summary>
    internal static class DelimitedFileParser
    {
        /// <summary>
        /// 读取 CSV/TSV 文件，自动识别分隔符
        /// </summary>
        public static RedGoldTableData ReadTable(string path)
        {
            string text = ReadAllText(path);
            char delimiter = ChooseDelimiter(path, text);
            return new RedGoldTableData
            {
                Delimiter = delimiter,
                Rows = ParseDelimited(text, delimiter)
            };
        }

        /// <summary>
        /// 将 RedGoldTableData 写回 CSV/TSV 文件（UTF-8 无 BOM）
        /// </summary>
        public static void WriteTable(string path, RedGoldTableData table)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            foreach (List<string> row in table.Rows)
            {
                for (int i = 0; i < row.Count; i++)
                {
                    if (i > 0) sb.Append(table.Delimiter);
                    sb.Append(EscapeCell(row[i], table.Delimiter));
                }
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string ReadAllText(string path)
        {
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > int.MaxValue)
                    throw new IOException("表格文件过大，无法读取。");

                bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0) break;
                    offset += read;
                }
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Default.GetString(bytes);
            }
        }

        private static char ChooseDelimiter(string path, string text)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tsv") return '\t';

            string firstLine = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).FirstOrDefault() ?? "";
            int commaCount = firstLine.Count(c => c == ',');
            int tabCount = firstLine.Count(c => c == '\t');
            return tabCount > commaCount ? '\t' : ',';
        }

        private static List<List<string>> ParseDelimited(string text, char delimiter)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                }
                else if ((c == '\r' || c == '\n') && !inQuotes)
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                    rows.Add(row);
                    row = new List<string>();
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                }
                else
                {
                    field.Append(c);
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows.Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        }

        private static string EscapeCell(string cell, char delimiter)
        {
            cell = cell ?? "";
            if (cell.IndexOfAny(new[] { delimiter, '"', '\r', '\n' }) < 0)
                return cell;

            return "\"" + cell.Replace("\"", "\"\"") + "\"";
        }
    }
}
