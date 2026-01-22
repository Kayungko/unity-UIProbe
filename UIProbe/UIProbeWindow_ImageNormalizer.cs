using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UIProbe
{
    partial class UIProbeWindow
    {
        // 图片规范化标签页状态
        private string normalizerSourceFolder = "";
        private bool normalizerIncludeSubfolders = true;
        private int normalizerTargetWidth = 512;
        private int normalizerTargetHeight = 512;
        private bool normalizerForceSquare = true;
        private ContentAlignment normalizerAlignment = ContentAlignment.Center;
        private bool normalizerOverwrite = true;
        private string normalizerNamingSuffix = "_normalized";
        private Vector2 normalizerScrollPos;
        
        private List<string> normalizerImageList = new List<string>();
        private bool normalizerProcessing = false;
        private float normalizerProgress = 0f;
        
        /// <summary>
        /// 绘制图片规范化标签页
        /// </summary>
        private void DrawImageNormalizerTab()
        {
            EditorGUILayout.LabelField("图片规范化工具 (Image Normalizer)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.HelpBox("将不同尺寸的图片统一到相同尺寸，保持非透明内容不变形。", MessageType.Info);
            EditorGUILayout.Space(5);
            
            // 源文件夹选择
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("源文件设置", EditorStyles.boldLabel);
            
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("源文件夹:", GUILayout.Width(80));
            EditorGUI.BeginDisabledGroup(normalizerProcessing);
            normalizerSourceFolder = EditorGUILayout.TextField(normalizerSourceFolder);
            if (GUILayout.Button("📁 浏览", GUILayout.Width(60)))
            {
                string selected = EditorUtility.OpenFolderPanel("选择图片文件夹", normalizerSourceFolder, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    normalizerSourceFolder = selected;
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
            
            normalizerIncludeSubfolders = EditorGUILayout.Toggle("包含子文件夹", normalizerIncludeSubfolders);
            GUILayout.EndVertical();
            
            EditorGUILayout.Space(5);
            
            // 目标尺寸设置
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("目标尺寸设置", EditorStyles.boldLabel);
            
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("目标尺寸:", GUILayout.Width(80));
            EditorGUI.BeginDisabledGroup(normalizerProcessing);
            normalizerTargetWidth = EditorGUILayout.IntField(normalizerTargetWidth, GUILayout.Width(60));
            EditorGUILayout.LabelField("x", GUILayout.Width(15));
            
            EditorGUI.BeginDisabledGroup(normalizerForceSquare);
            normalizerTargetHeight = EditorGUILayout.IntField(normalizerTargetHeight, GUILayout.Width(60));
            EditorGUI.EndDisabledGroup();
            
            normalizerForceSquare = EditorGUILayout.Toggle("正方形", normalizerForceSquare, GUILayout.Width(80));
            
            if (normalizerForceSquare)
            {
                normalizerTargetHeight = normalizerTargetWidth;
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
            
            // 对齐方式
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("对齐方式:", GUILayout.Width(80));
            EditorGUI.BeginDisabledGroup(normalizerProcessing);
            normalizerAlignment = (ContentAlignment)EditorGUILayout.EnumPopup(normalizerAlignment);
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
            
            EditorGUILayout.Space(5);
            
            // 处理模式设置
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("处理模式", EditorStyles.boldLabel);
            
            EditorGUI.BeginDisabledGroup(normalizerProcessing);
            normalizerOverwrite = EditorGUILayout.Toggle("覆盖原文件", normalizerOverwrite);
            
            EditorGUI.BeginDisabledGroup(normalizerOverwrite);
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("文件名后缀:", GUILayout.Width(80));
            normalizerNamingSuffix = EditorGUILayout.TextField(normalizerNamingSuffix);
            GUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
            EditorGUI.EndDisabledGroup();
            
            GUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // 扫描和处理按钮
            GUILayout.BeginHorizontal();
            
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(normalizerSourceFolder) || normalizerProcessing);
            if (GUILayout.Button("🔍 扫描图片", GUILayout.Height(30)))
            {
                ScanImagesForNormalizer();
            }
            EditorGUI.EndDisabledGroup();
            
            EditorGUI.BeginDisabledGroup(normalizerImageList.Count == 0 || normalizerProcessing);
            if (GUILayout.Button("开始处理", GUILayout.Height(30)))
            {
                StartNormalizerProcessing();
            }
            EditorGUI.EndDisabledGroup();
            
            GUILayout.EndHorizontal();
            
            // 显示图片列表
            if (normalizerImageList.Count > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"找到 {normalizerImageList.Count} 张图片:", EditorStyles.boldLabel);
                
                normalizerScrollPos = EditorGUILayout.BeginScrollView(normalizerScrollPos, GUILayout.Height(200));
                
                foreach (var imagePath in normalizerImageList)
                {
                    GUILayout.BeginHorizontal(EditorStyles.helpBox);
                    
                    string fileName = Path.GetFileName(imagePath);
                    EditorGUILayout.LabelField($"📄 {fileName}", GUILayout.Width(200));
                    
                    // 显示当前尺寸
                    Texture2D tex = ImageNormalizer.LoadTexture(imagePath);
                    if (tex != null)
                    {
                        EditorGUILayout.LabelField($"({tex.width}x{tex.height})", EditorStyles.miniLabel, GUILayout.Width(80));
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                    
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                
                EditorGUILayout.EndScrollView();
            }
            
            // 显示处理进度
            if (normalizerProcessing)
            {
                EditorGUILayout.Space(5);
                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(GUILayout.Height(20)),
                    normalizerProgress,
                    $"处理中... {(int)(normalizerProgress * 100)}%"
                );
            }
        }
        
        /// <summary>
        /// 扫描图片文件
        /// </summary>
        private void ScanImagesForNormalizer()
        {
            normalizerImageList.Clear();
            
            if (string.IsNullOrEmpty(normalizerSourceFolder) || !Directory.Exists(normalizerSourceFolder))
            {
                EditorUtility.DisplayDialog("错误", "请选择有效的文件夹", "确定");
                return;
            }
            
            SearchOption searchOption = normalizerIncludeSubfolders ? 
                SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            
            try
            {
                var pngFiles = Directory.GetFiles(normalizerSourceFolder, "*.png", searchOption);
                var jpgFiles = Directory.GetFiles(normalizerSourceFolder, "*.jpg", searchOption);
                
                normalizerImageList.AddRange(pngFiles);
                normalizerImageList.AddRange(jpgFiles);
                
                if (normalizerImageList.Count == 0)
                {
                    EditorUtility.DisplayDialog("提示", "未找到PNG或JPG图片文件", "确定");
                }
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("错误", $"扫描失败:\n{e.Message}", "确定");
            }
        }
        
        /// <summary>
        /// 开始批量处理
        /// </summary>
        private void StartNormalizerProcessing()
        {
            if (normalizerImageList.Count == 0) return;
            
            bool confirmed = EditorUtility.DisplayDialog(
                "确认处理",
                $"即将处理 {normalizerImageList.Count} 张图片\n" +
                $"目标尺寸: {normalizerTargetWidth}x{normalizerTargetHeight}\n" +
                $"对齐方式: {normalizerAlignment}\n" +
                $"模式: {(normalizerOverwrite ? "覆盖原文件" : "生成新文件")}\n\n" +
                "确认开始处理?",
                "开始",
                "取消"
            );
            
            if (!confirmed) return;
            
            normalizerProcessing = true;
            normalizerProgress = 0f;
            
            int successCount = ImageNormalizer.ProcessBatch(
                normalizerImageList.ToArray(),
                normalizerTargetWidth,
                normalizerTargetHeight,
                normalizerAlignment,
                normalizerOverwrite,
                normalizerNamingSuffix,
                (current, total) =>
                {
                    normalizerProgress = (float)current / total;
                    Repaint();
                }
            );
            
            normalizerProcessing = false;
            normalizerProgress = 0f;
            
            EditorUtility.DisplayDialog(
                "完成",
                $"处理完成！\n成功: {successCount}/{normalizerImageList.Count}",
                "确定"
            );
            
            // 如果处理的是项目内文件，刷新 AssetDatabase
            if (normalizerSourceFolder.StartsWith(Application.dataPath))
            {
                UnityEditor.AssetDatabase.Refresh();
            }
        }
    }
}
