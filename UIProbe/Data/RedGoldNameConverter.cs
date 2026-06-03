using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UIProbe
{
    /// <summary>
    /// 红品质资源命名转换器：中文名 → 拼音/语义名 → T_Icon_Red_{Name}.png
    /// </summary>
    internal static class RedGoldNameConverter
    {
        private static readonly Dictionary<char, string> PinyinMap = new Dictionary<char, string>
        {
            { '阿', "A" }, { '白', "Bai" }, { '宝', "Bao" }, { '爆', "Bao" }, { '兵', "Bing" },
            { '裁', "Cai" }, { '茶', "Cha" }, { '辰', "Chen" }, { '处', "Chu" }, { '傩', "Nuo" },
            { '地', "Di" }, { '赌', "Du" }, { '发', "Fa" }, { '骨', "Gu" }, { '锅', "Guo" },
            { '红', "Hong" }, { '虹', "Hong" }, { '火', "Huo" }, { '画', "Hua" }, { '匠', "Jiang" },
            { '祭', "Ji" }, { '甲', "Jia" }, { '金', "Jin" }, { '晶', "Jing" }, { '巨', "Ju" },
            { '骼', "Ge" }, { '怪', "Guai" }, { '猎', "Lie" }, { '炉', "Lu" }, { '满', "Man" },
            { '蒙', "Meng" }, { '霓', "Ni" }, { '球', "Qiu" }, { '神', "Shen" }, { '生', "Sheng" },
            { '森', "Sen" }, { '坛', "Tan" }, { '堂', "Tang" }, { '特', "Te" }, { '天', "Tian" },
            { '铁', "Tie" }, { '外', "Wai" }, { '文', "Wen" }, { '西', "Xi" }, { '戏', "Xi" },
            { '先', "Xian" }, { '像', "Xiang" }, { '星', "Xing" }, { '型', "Xing" }, { '鸭', "Ya" },
            { '源', "Yuan" }, { '之', "Zhi" }, { '桌', "Zhuo" }, { '死', "Si" },
            { 'Ⅰ', "1" }, { 'Ⅱ', "2" }, { 'Ⅲ', "3" }, { 'Ⅳ', "4" }
        };

        private static readonly Dictionary<string, string> SemanticNameMap = new Dictionary<string, string>
        {
            { "星辰画匠文森特", "XingChenHuaJiang" },
            { "铁球先生", "TieQiuXianSheng" },
            { "白死神西蒙", "BaiSiShen" },
            { "傩戏外骨骼", "NuoXiWaiGuGe" },
            { "霓虹茶桌", "NiHongChaZhuo" },
            { "赌神祭坛", "DuShenJiTan" },
            { "火锅神像", "HuoGuoShenXiang" },
            { "满堂红天锅", "ManTangHongTianGuo" }
        };

        /// <summary>
        /// 从图标路径提取输出文件名，确保扩展名为 .png
        /// </summary>
        public static string GetOutputFileNameFromIconPath(string iconPath)
        {
            if (string.IsNullOrEmpty(iconPath)) return "";

            string fileName = Path.GetFileName(iconPath.Replace('\\', '/'));
            if (string.IsNullOrEmpty(fileName)) return "";

            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
                fileName += ".png";
            else if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
                fileName = Path.GetFileNameWithoutExtension(fileName) + ".png";

            return fileName;
        }

        /// <summary>
        /// 构建红品质输出文件名：T_Icon_Red_{拼音}.png
        /// </summary>
        public static string BuildRedOutputFileName(string displayName)
        {
            string pinyin = GetSemanticPinyin(displayName);
            return string.IsNullOrEmpty(pinyin) ? "" : $"T_Icon_Red_{pinyin}.png";
        }

        private static string GetSemanticPinyin(string displayName)
        {
            string key = NormalizeSemanticName(displayName);
            if (!string.IsNullOrEmpty(key) && SemanticNameMap.TryGetValue(key, out string semanticName))
                return semanticName;

            return ToShortPinyin(displayName);
        }

        private static string NormalizeSemanticName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "";

            var builder = new StringBuilder();
            foreach (char c in displayName)
            {
                if (IsCjk(c) || char.IsLetterOrDigit(c))
                    builder.Append(c);
            }

            return builder.ToString();
        }

        private static string ToShortPinyin(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "";

            var builder = new StringBuilder();
            bool capitalizeNextAscii = true;
            foreach (char c in displayName)
            {
                if (PinyinMap.TryGetValue(c, out string pinyin))
                {
                    builder.Append(pinyin);
                    capitalizeNextAscii = true;
                }
                else if (c >= 'a' && c <= 'z')
                {
                    builder.Append(capitalizeNextAscii ? char.ToUpperInvariant(c) : c);
                    capitalizeNextAscii = false;
                }
                else if (c >= 'A' && c <= 'Z')
                {
                    builder.Append(c);
                    capitalizeNextAscii = false;
                }
                else if (c >= '0' && c <= '9')
                {
                    builder.Append(c);
                    capitalizeNextAscii = false;
                }
                else if (IsCjk(c))
                {
                    builder.Append("X");
                    capitalizeNextAscii = true;
                }
                else
                {
                    capitalizeNextAscii = true;
                }
            }

            return builder.ToString();
        }

        private static bool IsCjk(char c)
        {
            return c >= 0x4E00 && c <= 0x9FFF;
        }
    }
}
