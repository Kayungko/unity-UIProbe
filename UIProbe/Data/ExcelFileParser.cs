using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace UIProbe
{
    /// <summary>
    /// 轻量级 .xlsx 读取器（零外部依赖，纯 .NET ZIP + XML 解析）
    /// </summary>
    internal static class ExcelFileParser
    {
        public static RedGoldTableData ReadTable(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("找不到 Excel 文件", path);

            using (var archive = ZipFile.OpenRead(path))
            {
                // 1. 读取共享字符串表（Shared Strings）
                var sharedStrings = ReadSharedStrings(archive);

                // 2. 找到第一个工作表
                XElement sheetData = GetSheetData(archive);
                if (sheetData == null)
                    throw new InvalidDataException("Excel 文件中找不到工作表数据");

                // 3. 解析行和列
                var rows = new List<List<string>>();
                int maxCol = 0;

                var rowElements = sheetData.Elements()
                    .Where(e => e.Name.LocalName == "row")
                    .OrderBy(e => (int)e.Attribute("r"))
                    .ToList();

                foreach (var rowEl in rowElements)
                {
                    var cells = rowEl.Elements()
                        .Where(e => e.Name.LocalName == "c")
                        .ToList();

                    int rowIndex = (int)rowEl.Attribute("r");
                    // 确保 rows 有足够空间
                    while (rows.Count < rowIndex)
                        rows.Add(new List<string>());

                    var row = rows[rowIndex - 1];
                    foreach (var cell in cells)
                    {
                        string cellRef = (string)cell.Attribute("r") ?? "";
                        int colIndex = ParseColumnIndex(cellRef);
                        string cellType = (string)cell.Attribute("t") ?? "";
                        string value = cell.Element(XName.Get("v", sheetData.GetDefaultNamespace().NamespaceName))?.Value ?? "";

                        string cellValue = "";
                        if (cellType == "s" && int.TryParse(value, out int si) && si >= 0 && si < sharedStrings.Count)
                            cellValue = sharedStrings[si];
                        else if (cellType == "str" || cellType == "inlineStr")
                            cellValue = value;
                        else if (!string.IsNullOrEmpty(value))
                            cellValue = value;

                        // 确保行有足够列
                        while (row.Count <= colIndex)
                            row.Add("");
                        row[colIndex] = cellValue;

                        if (colIndex > maxCol) maxCol = colIndex;
                    }
                }

                // 清理尾部空列
                foreach (var row in rows)
                {
                    while (row.Count > maxCol + 1)
                        row.RemoveAt(row.Count - 1);
                }

                return new RedGoldTableData
                {
                    Delimiter = ',',
                    Rows = rows.Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList()
                };
            }
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return new List<string>();

            using (var stream = entry.Open())
            {
                var doc = XDocument.Load(stream);
                var ns = doc.Root.GetDefaultNamespace();
                return doc.Root.Elements(XName.Get("si", ns.NamespaceName))
                    .Select(si =>
                    {
                        var text = si.Element(XName.Get("t", ns.NamespaceName));
                        if (text != null) return text.Value;

                        // Rich text: concatenate all <t> elements
                        var runs = si.Elements(XName.Get("r", ns.NamespaceName));
                        return string.Concat(runs.Select(r =>
                            r.Element(XName.Get("t", ns.NamespaceName))?.Value ?? ""));
                    })
                    .ToList();
            }
        }

        private static XElement GetSheetData(ZipArchive archive)
        {
            // 从 workbook.xml 找第一个 sheet 的 id
            var wbEntry = archive.GetEntry("xl/workbook.xml");
            if (wbEntry == null) return null;

            XNamespace ns;
            string sheetRId = null;
            using (var stream = wbEntry.Open())
            {
                var doc = XDocument.Load(stream);
                var wbNs = doc.Root.GetDefaultNamespace();
                var sheets = doc.Root
                    .Element(XName.Get("sheets", wbNs.NamespaceName));
                if (sheets == null) return null;

                var firstSheet = sheets.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "sheet");
                if (firstSheet == null) return null;

                sheetRId = (string)firstSheet.Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
            }

            if (string.IsNullOrEmpty(sheetRId)) return null;

            // 从 _rels/.rels 或 xl/_rels/workbook.xml.rels 找 sheet 路径
            var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (relsEntry == null) return null;

            string sheetPath = null;
            using (var stream = relsEntry.Open())
            {
                var doc = XDocument.Load(stream);
                var relsNs = doc.Root.GetDefaultNamespace();
                foreach (var rel in doc.Root.Elements(XName.Get("Relationship", relsNs.NamespaceName)))
                {
                    if ((string)rel.Attribute("Id") == sheetRId)
                    {
                        sheetPath = "xl/" + (string)rel.Attribute("Target");
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(sheetPath)) return null;

            // 读取 sheet
            var sheetEntry = archive.GetEntry(sheetPath);
            if (sheetEntry == null) return null;

            using (var stream = sheetEntry.Open())
            {
                var doc = XDocument.Load(stream);
                ns = doc.Root.GetDefaultNamespace();
                return doc.Root.Element(XName.Get("sheetData", ns.NamespaceName));
            }
        }

        private static int ParseColumnIndex(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return 0;
            // 提取字母部分（去掉数字）
            string colLetters = new string(cellRef.TakeWhile(c => !char.IsDigit(c)).ToArray());
            if (string.IsNullOrEmpty(colLetters)) return 0;

            int result = 0;
            foreach (char c in colLetters.ToUpperInvariant())
            {
                result = result * 26 + (c - 'A' + 1);
            }
            return result - 1; // 0-based
        }
    }
}
