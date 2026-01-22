using UnityEngine;
using System.IO;
using System;

namespace UIProbe
{
    /// <summary>
    /// 对齐方式
    /// </summary>
    public enum ContentAlignment
    {
        Center,         // 居中
        KeepOriginal,   // 保持原位（左上对齐）
    }

    /// <summary>
    /// 图片规范化工具
    /// 用于将不同尺寸的图片统一到相同尺寸，保持非透明内容不变形
    /// </summary>
    public static class ImageNormalizer
    {
        /// <summary>
        /// 获取图片中非透明内容的边界
        /// </summary>
        public static RectInt GetContentBounds(Texture2D texture)
        {
            if (texture == null) return new RectInt(0, 0, 0, 0);
            
            int minX = texture.width;
            int minY = texture.height;
            int maxX = -1;
            int maxY = -1;
            
            Color[] pixels = texture.GetPixels();
            
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    int index = y * texture.width + x;
                    if (pixels[index].a > 0.01f) // 非透明像素
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            
            // 如果没有非透明像素，返回整个图片区域
            if (maxX < minX || maxY < minY)
            {
                return new RectInt(0, 0, texture.width, texture.height);
            }
            
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            
            return new RectInt(minX, minY, width, height);
        }
        
        /// <summary>
        /// 规范化图片到目标尺寸
        /// </summary>
        public static Texture2D Normalize(
            Texture2D source,
            int targetWidth,
            int targetHeight,
            ContentAlignment alignment)
        {
            if (source == null) return null;
            
            // 创建目标纹理
            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            
            // 用透明色填充
            Color[] clearPixels = new Color[targetWidth * targetHeight];
            for (int i = 0; i < clearPixels.Length; i++)
            {
                clearPixels[i] = new Color(0, 0, 0, 0);
            }
            result.SetPixels(clearPixels);
            
            // 获取源图片内容边界
            RectInt contentBounds = GetContentBounds(source);
            
            // 计算目标位置（根据对齐方式）
            int offsetX = 0;
            int offsetY = 0;
            
            switch (alignment)
            {
                case ContentAlignment.Center:
                    offsetX = (targetWidth - contentBounds.width) / 2;
                    offsetY = (targetHeight - contentBounds.height) / 2;
                    break;
                    
                case ContentAlignment.KeepOriginal:
                    offsetX = contentBounds.x;
                    offsetY = contentBounds.y;
                    break;
            }
            
            // 拷贝内容区域
            Color[] sourcePixels = source.GetPixels(
                contentBounds.x,
                contentBounds.y,
                contentBounds.width,
                contentBounds.height
            );
            
            result.SetPixels(offsetX, offsetY, contentBounds.width, contentBounds.height, sourcePixels);
            result.Apply();
            
            return result;
        }
        
        /// <summary>
        /// 保存纹理为PNG文件
        /// </summary>
        public static bool SaveTexture(Texture2D texture, string path)
        {
            try
            {
                byte[] pngData = texture.EncodeToPNG();
                File.WriteAllBytes(path, pngData);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImageNormalizer] 保存失败: {path}\n{e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 从文件加载纹理（支持项目外路径）
        /// </summary>
        public static Texture2D LoadTexture(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Debug.LogError($"[ImageNormalizer] 文件不存在: {path}");
                    return null;
                }
                
                byte[] fileData = File.ReadAllBytes(path);
                Texture2D texture = new Texture2D(2, 2);
                
                if (texture.LoadImage(fileData))
                {
                    return texture;
                }
                else
                {
                    Debug.LogError($"[ImageNormalizer] 无法加载图片: {path}");
                    UnityEngine.Object.DestroyImmediate(texture);
                    return null;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImageNormalizer] 加载失败: {path}\n{e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 批量处理图片
        /// </summary>
        public static int ProcessBatch(
            string[] imagePaths,
            int targetWidth,
            int targetHeight,
            ContentAlignment alignment,
            bool overwrite,
            string namingSuffix = "_normalized",
            Action<int, int> progressCallback = null)
        {
            int successCount = 0;
            
            for (int i = 0; i < imagePaths.Length; i++)
            {
                string sourcePath = imagePaths[i];
                
                // 加载源图片
                Texture2D sourceTexture = LoadTexture(sourcePath);
                if (sourceTexture == null)
                {
                    progressCallback?.Invoke(i + 1, imagePaths.Length);
                    continue;
                }
                
                // 规范化
                Texture2D normalized = Normalize(sourceTexture, targetWidth, targetHeight, alignment);
                UnityEngine.Object.DestroyImmediate(sourceTexture);
                
                if (normalized == null)
                {
                    progressCallback?.Invoke(i + 1, imagePaths.Length);
                    continue;
                }
                
                // 确定输出路径
                string outputPath = sourcePath;
                if (!overwrite)
                {
                    string directory = Path.GetDirectoryName(sourcePath);
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
                    string extension = Path.GetExtension(sourcePath);
                    outputPath = Path.Combine(directory, fileNameWithoutExt + namingSuffix + extension);
                }
                
                // 保存
                if (SaveTexture(normalized, outputPath))
                {
                    successCount++;
                }
                
                UnityEngine.Object.DestroyImmediate(normalized);
                
                progressCallback?.Invoke(i + 1, imagePaths.Length);
            }
            
            return successCount;
        }
    }
}
