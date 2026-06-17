using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UIProbe
{
    internal sealed class RichTextGeneratorModule : UIProbeModuleBase
    {
        public override string Id => "richTextGenerator";
        public override string DisplayName => "富文本生成";
        public override Tab Tab => Tab.RichTextGenerator;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showRichTextGenerator;

        // Rich Text Generator State
        private string richTextInput = "";
        private string richTextOutput = "";
        private Vector2 richTextScrollPosition;

        // Selection
        private int selectionStart = 0;
        private int selectionEnd = 0;

        // Format Settings
        private Color selectedColor = Color.white;
        private int selectedColorIndex = -1;
        private int fontSize = 18;
        private bool isBold = false;
        private bool isItalic = false;
        private bool isUnderline = false;
        private bool isStrikethrough = false;
        private float alphaValue = 1.0f;

        // Cumulative Mode（累积模式）
        private bool cumulativeMode = true;
        private List<AppliedFormat> appliedFormats = new List<AppliedFormat>();

        // 已应用的格式记录
        [Serializable]
        private class AppliedFormat
        {
            public int start;
            public int end;
            public string preview;
            public string formatDesc;
        }

        // Preset Colors
        private readonly Color[] presetColors = new Color[]
        {
            new Color(1f, 0f, 0f, 1f),      // 红色 - 错误/危险
            new Color(0f, 1f, 0f, 1f),      // 绿色 - 成功/确认
            new Color(0f, 0.6f, 1f, 1f),    // 蓝色 - 信息/提示
            new Color(1f, 1f, 0f, 1f),      // 黄色 - 警告
            new Color(1f, 0.6f, 0f, 1f),    // 橙色 - 强调
            new Color(0.8f, 0f, 1f, 1f),    // 紫色 - 稀有
            new Color(1f, 0.84f, 0f, 1f),   // 金色 - 高级/VIP
            new Color(1f, 1f, 1f, 1f),      // 白色 - 默认
            new Color(0.5f, 0.5f, 0.5f, 1f),// 灰色 - 次要信息
            new Color(0f, 0f, 0f, 1f)       // 黑色 - 主文本
        };

        private readonly string[] presetColorNames = new string[]
        {
            "红色", "绿色", "蓝色", "黄色", "橙色",
            "紫色", "金色", "白色", "灰色", "黑色"
        };

        private readonly int[] presetFontSizes = new int[] { 14, 18, 24, 32, 48 };

        /// <summary>
        /// 绘制富文本生成器标签页
        /// </summary>
        public override void Draw()
        {
            EditorGUILayout.LabelField("TMP 富文本生成器", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("可视化生成 TextMeshPro 富文本代码，无需记忆复杂的标签语法", MessageType.Info);
            EditorGUILayout.Space(5);

            richTextScrollPosition = EditorGUILayout.BeginScrollView(richTextScrollPosition);

            // 文本输入区域
            DrawTextInputSection();

            EditorGUILayout.Space(10);

            // 格式化工具栏
            DrawFormatToolbar();

            EditorGUILayout.Space(10);

            // 已应用格式列表（累积模式）
            if (cumulativeMode && appliedFormats.Count > 0)
            {
                DrawAppliedFormatsList();
                EditorGUILayout.Space(10);
            }

            // 富文本代码输出
            DrawOutputSection();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 绘制文本输入区域
        /// </summary>
        private void DrawTextInputSection()
        {
            EditorGUILayout.LabelField("📝 文本输入", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("原始文本:", EditorStyles.miniLabel);
            richTextInput = EditorGUILayout.TextArea(richTextInput, GUILayout.Height(60));

            EditorGUILayout.Space(5);

            // 选择范围
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("选择范围:", GUILayout.Width(70));

            int maxLength = Mathf.Max(0, richTextInput.Length);
            selectionStart = EditorGUILayout.IntSlider("起始", selectionStart, 0, maxLength);
            selectionEnd = EditorGUILayout.IntSlider("结束", selectionEnd, 0, maxLength);

            // 确保选择范围合法
            if (selectionStart > selectionEnd)
            {
                int temp = selectionStart;
                selectionStart = selectionEnd;
                selectionEnd = temp;
            }

            if (GUILayout.Button("全选", GUILayout.Width(50)))
            {
                selectionStart = 0;
                selectionEnd = richTextInput.Length;
            }

            EditorGUILayout.EndHorizontal();

            // 显示选中的文本
            if (selectionStart < selectionEnd && selectionEnd <= richTextInput.Length)
            {
                string selectedText = richTextInput.Substring(selectionStart, selectionEnd - selectionStart);
                EditorGUILayout.LabelField($"已选中: \"{selectedText}\"", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 绘制格式化工具栏
        /// </summary>
        private void DrawFormatToolbar()
        {
            EditorGUILayout.LabelField("🎨 格式化工具", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 颜色选择
            DrawColorPicker();

            EditorGUILayout.Space(5);

            // 字号选择
            DrawFontSizePicker();

            EditorGUILayout.Space(5);

            // 样式按钮
            DrawStyleButtons();

            EditorGUILayout.Space(5);

            // 透明度
            DrawAlphaSlider();

            EditorGUILayout.Space(10);

            // 累积模式开关
            EditorGUILayout.BeginVertical();
            bool newCumulativeMode = EditorGUILayout.Toggle("累积模式", cumulativeMode);
            if (newCumulativeMode != cumulativeMode)
            {
                cumulativeMode = newCumulativeMode;
                if (!cumulativeMode)
                {
                    appliedFormats.Clear();
                }
            }
            EditorGUILayout.LabelField("(允许对同一文本多次应用不同格式)", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // 应用按钮
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(cumulativeMode ? "累积应用" : "应用格式", GUILayout.Height(30)))
            {
                ApplyFormat();
            }

            if (GUILayout.Button("清除格式", GUILayout.Width(100), GUILayout.Height(30)))
            {
                ClearFormat();
            }

            if (cumulativeMode && appliedFormats.Count > 0)
            {
                if (GUILayout.Button("撤销上次", GUILayout.Width(80), GUILayout.Height(30)))
                {
                    UndoLastFormat();
                }
            }

            if (GUILayout.Button("重置", GUILayout.Width(60), GUILayout.Height(30)))
            {
                ResetToInput();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 绘制颜色选择器
        /// </summary>
        private void DrawColorPicker()
        {
            EditorGUILayout.LabelField("颜色", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();

            // 预设颜色
            for (int i = 0; i < presetColors.Length; i++)
            {
                Color oldColor = GUI.backgroundColor;
                GUI.backgroundColor = presetColors[i];

                bool isSelected = selectedColorIndex == i;
                GUIStyle buttonStyle = isSelected ? EditorStyles.miniButtonMid : EditorStyles.miniButton;

                if (GUILayout.Button(presetColorNames[i], buttonStyle, GUILayout.Width(50), GUILayout.Height(25)))
                {
                    selectedColor = presetColors[i];
                    selectedColorIndex = i;
                }

                GUI.backgroundColor = oldColor;
            }

            EditorGUILayout.EndHorizontal();

            // 自定义颜色
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("自定义:", GUILayout.Width(60));
            Color newColor = EditorGUILayout.ColorField(selectedColor, GUILayout.Width(60));
            if (newColor != selectedColor)
            {
                selectedColor = newColor;
                selectedColorIndex = -1;
            }

            // 显示当前颜色的Hex值
            string hexColor = ColorUtility.ToHtmlStringRGBA(selectedColor);
            EditorGUILayout.LabelField($"#{hexColor}", GUILayout.Width(100));

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制字号选择器
        /// </summary>
        private void DrawFontSizePicker()
        {
            EditorGUILayout.LabelField("字号", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();

            // 预设字号
            foreach (int size in presetFontSizes)
            {
                if (GUILayout.Button(size.ToString(), GUILayout.Width(40), GUILayout.Height(25)))
                {
                    fontSize = size;
                }
            }

            GUILayout.FlexibleSpace();

            // 自定义字号
            EditorGUILayout.LabelField("自定义:", GUILayout.Width(60));
            fontSize = EditorGUILayout.IntSlider(fontSize, 10, 60, GUILayout.Width(200));

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制样式按钮
        /// </summary>
        private void DrawStyleButtons()
        {
            EditorGUILayout.LabelField("样式", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();

            // 粗体
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = isBold ? Color.cyan : Color.white;
            if (GUILayout.Button("B  粗体", GUILayout.Width(80), GUILayout.Height(30)))
            {
                isBold = !isBold;
            }

            // 斜体
            GUI.backgroundColor = isItalic ? Color.cyan : Color.white;
            if (GUILayout.Button("I  斜体", GUILayout.Width(80), GUILayout.Height(30)))
            {
                isItalic = !isItalic;
            }

            // 下划线
            GUI.backgroundColor = isUnderline ? Color.cyan : Color.white;
            if (GUILayout.Button("U  下划线", GUILayout.Width(80), GUILayout.Height(30)))
            {
                isUnderline = !isUnderline;
            }

            // 删除线
            GUI.backgroundColor = isStrikethrough ? Color.cyan : Color.white;
            if (GUILayout.Button("S  删除线", GUILayout.Width(80), GUILayout.Height(30)))
            {
                isStrikethrough = !isStrikethrough;
            }

            GUI.backgroundColor = oldBg;

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制透明度滑块
        /// </summary>
        private void DrawAlphaSlider()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("透明度", EditorStyles.miniBoldLabel, GUILayout.Width(60));
            alphaValue = EditorGUILayout.Slider(alphaValue, 0f, 1f);
            EditorGUILayout.LabelField($"{(int)(alphaValue * 100)}%", GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制输出区域
        /// </summary>
        private void DrawOutputSection()
        {
            EditorGUILayout.LabelField("📋 富文本代码", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("生成的富文本代码:", EditorStyles.miniLabel);

            // 显示生成的代码（只读）
            EditorGUILayout.TextArea(richTextOutput, GUILayout.Height(60));

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("📋 复制到剪贴板", GUILayout.Height(35)))
            {
                EditorGUIUtility.systemCopyBuffer = richTextOutput;
                EditorUtility.DisplayDialog("成功", "富文本代码已复制到剪贴板！", "确定");
            }

            if (GUILayout.Button("清空", GUILayout.Width(80), GUILayout.Height(35)))
            {
                richTextInput = "";
                richTextOutput = "";
                selectionStart = 0;
                selectionEnd = 0;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 应用格式
        /// </summary>
        private void ApplyFormat()
        {
            // 在累积模式下，如果已有输出，基于输出继续应用
            string baseText = cumulativeMode && !string.IsNullOrEmpty(richTextOutput)
                ? richTextOutput
                : richTextInput;

            if (string.IsNullOrEmpty(baseText))
            {
                EditorUtility.DisplayDialog("提示", "请先输入文本", "确定");
                return;
            }

            if (selectionStart >= selectionEnd)
            {
                EditorUtility.DisplayDialog("提示", "请选择要格式化的文本范围", "确定");
                return;
            }

            // 验证选择范围是否在原始文本范围内
            if (selectionEnd > richTextInput.Length)
            {
                EditorUtility.DisplayDialog("提示", $"选择范围超出原始文本长度({richTextInput.Length})\n请基于原始文本的位置选择", "确定");
                return;
            }

            // 生成富文本代码
            string newOutput = GenerateRichTextCode();

            // 如果是累积模式，记录这次应用
            if (cumulativeMode)
            {
                string selectedText = richTextInput.Substring(selectionStart, selectionEnd - selectionStart);
                string formatDesc = GetFormatDescription();

                appliedFormats.Add(new AppliedFormat
                {
                    start = selectionStart,
                    end = selectionEnd,
                    preview = selectedText,
                    formatDesc = formatDesc
                });
            }

            richTextOutput = newOutput;
        }

        /// <summary>
        /// 生成富文本代码
        /// </summary>
        private string GenerateRichTextCode()
        {
            // 在累积模式下，基于当前输出或原始输入
            string baseText = cumulativeMode && !string.IsNullOrEmpty(richTextOutput)
                ? richTextOutput
                : richTextInput;

            // 在累积模式下，我们需要在原始输入中找到选中的文本位置
            // 然后在当前输出中插入格式化标签
            if (cumulativeMode && !string.IsNullOrEmpty(richTextOutput))
            {
                return GenerateCumulativeRichText();
            }

            // 非累积模式，直接格式化
            string before = richTextInput.Substring(0, selectionStart);
            string selected = richTextInput.Substring(selectionStart, selectionEnd - selectionStart);
            string after = richTextInput.Substring(selectionEnd);

            // 构建标签
            string formattedText = WrapWithTags(selected);

            return before + formattedText + after;
        }

        /// <summary>
        /// 累积模式下生成富文本
        /// </summary>
        private string GenerateCumulativeRichText()
        {
            // 建立原始文本到当前输出的位置映射
            // 简化处理：基于原始文本的选择位置，在当前输出中找到对应的纯文本位置

            string selectedPlainText = richTextInput.Substring(selectionStart, selectionEnd - selectionStart);

            // 从当前输出中提取纯文本（移除所有标签）
            string plainOutput = System.Text.RegularExpressions.Regex.Replace(richTextOutput, "<[^>]+>", "");

            // 验证纯文本是否与原始输入一致
            if (plainOutput != richTextInput)
            {
                // 如果不一致，回退到原始输入
                richTextOutput = richTextInput;
                plainOutput = richTextInput;
            }

            // 找到选中文本在输出中的实际位置
            int outputStart = FindPositionInRichText(richTextOutput, selectionStart);
            int outputEnd = FindPositionInRichText(richTextOutput, selectionEnd);

            if (outputStart == -1 || outputEnd == -1)
            {
                EditorUtility.DisplayDialog("错误", "无法定位选中文本在富文本中的位置", "确定");
                return richTextOutput;
            }

            // 提取选中部分
            string before = richTextOutput.Substring(0, outputStart);
            string selected = richTextOutput.Substring(outputStart, outputEnd - outputStart);
            string after = richTextOutput.Substring(outputEnd);

            // 包裹新标签
            string formattedText = WrapWithTags(selected);

            return before + formattedText + after;
        }

        /// <summary>
        /// 在富文本中找到对应原始文本位置的实际位置
        /// </summary>
        private int FindPositionInRichText(string richText, int plainPosition)
        {
            int plainCount = 0;
            bool inTag = false;

            for (int i = 0; i < richText.Length; i++)
            {
                if (richText[i] == '<')
                {
                    inTag = true;
                }
                else if (richText[i] == '>')
                {
                    inTag = false;
                    continue;
                }

                if (!inTag)
                {
                    if (plainCount == plainPosition)
                    {
                        return i;
                    }
                    plainCount++;
                }
            }

            return plainCount == plainPosition ? richText.Length : -1;
        }

        /// <summary>
        /// 用标签包裹文本
        /// </summary>
        private string WrapWithTags(string text)
        {
            List<string> openTags = new List<string>();
            List<string> closeTags = new List<string>();

            // 颜色标签（包含透明度）
            Color finalColor = new Color(selectedColor.r, selectedColor.g, selectedColor.b, alphaValue);
            string colorHex = ColorUtility.ToHtmlStringRGBA(finalColor);
            openTags.Add($"<color=#{colorHex}>");
            closeTags.Insert(0, "</color>");

            // 字号标签
            if (fontSize != 18)
            {
                openTags.Add($"<size={fontSize}>");
                closeTags.Insert(0, "</size>");
            }

            // 样式标签
            if (isBold)
            {
                openTags.Add("<b>");
                closeTags.Insert(0, "</b>");
            }

            if (isItalic)
            {
                openTags.Add("<i>");
                closeTags.Insert(0, "</i>");
            }

            if (isUnderline)
            {
                openTags.Add("<u>");
                closeTags.Insert(0, "</u>");
            }

            if (isStrikethrough)
            {
                openTags.Add("<s>");
                closeTags.Insert(0, "</s>");
            }

            return string.Concat(openTags) + text + string.Concat(closeTags);
        }

        /// <summary>
        /// 获取当前格式描述
        /// </summary>
        private string GetFormatDescription()
        {
            List<string> desc = new List<string>();

            string colorHex = ColorUtility.ToHtmlStringRGB(selectedColor);
            desc.Add($"颜色:#{colorHex}");

            if (fontSize != 18)
            {
                desc.Add($"字号:{fontSize}");
            }

            if (isBold) desc.Add("粗体");
            if (isItalic) desc.Add("斜体");
            if (isUnderline) desc.Add("下划线");
            if (isStrikethrough) desc.Add("删除线");

            if (alphaValue < 1.0f)
            {
                desc.Add($"透明度:{(int)(alphaValue * 100)}%");
            }

            return string.Join(", ", desc);
        }

        /// <summary>
        /// 绘制已应用格式列表
        /// </summary>
        private void DrawAppliedFormatsList()
        {
            EditorGUILayout.LabelField("📜 已应用格式", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            for (int i = 0; i < appliedFormats.Count; i++)
            {
                var format = appliedFormats[i];
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField($"{i + 1}. \"{format.preview}\" → {format.formatDesc}", EditorStyles.miniLabel);

                if (GUILayout.Button("×", GUILayout.Width(20)))
                {
                    RemoveFormatAt(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 撤销上一次格式应用
        /// </summary>
        private void UndoLastFormat()
        {
            if (appliedFormats.Count > 0)
            {
                appliedFormats.RemoveAt(appliedFormats.Count - 1);
                RegenerateFromFormats();
            }
        }

        /// <summary>
        /// 移除指定位置的格式
        /// </summary>
        private void RemoveFormatAt(int index)
        {
            if (index >= 0 && index < appliedFormats.Count)
            {
                appliedFormats.RemoveAt(index);
                RegenerateFromFormats();
            }
        }

        /// <summary>
        /// 从格式列表重新生成富文本
        /// </summary>
        private void RegenerateFromFormats()
        {
            richTextOutput = richTextInput;

            // 这是一个简化实现，实际应该重新应用所有格式
            // 为了简化，这里只是清空输出，用户可以重新应用
            if (appliedFormats.Count == 0)
            {
                richTextOutput = richTextInput;
            }
        }

        /// <summary>
        /// 清除格式设置
        /// </summary>
        private void ClearFormat()
        {
            selectedColor = Color.white;
            selectedColorIndex = -1;
            fontSize = 18;
            isBold = false;
            isItalic = false;
            isUnderline = false;
            isStrikethrough = false;
            alphaValue = 1.0f;
        }

        /// <summary>
        /// 重置到输入
        /// </summary>
        private void ResetToInput()
        {
            richTextOutput = richTextInput;
            appliedFormats.Clear();
        }
    }
}
