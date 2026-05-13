using UnityEngine;
using System.IO;
using System;

namespace UIProbe
{
    /// <summary>
    /// 内容对齐方式（用于 Expand 模式）
    /// </summary>
    public enum ContentAlignment
    {
        Center,       // 居中
        KeepOriginal, // 保持原位（左上对齐）
    }

    /// <summary>
    /// 缩放模式
    /// </summary>
    public enum ResizeMode
    {
        /// <summary>仅扩展画布，不缩放内容（原有行为）</summary>
        Expand,
        /// <summary>等比缩放至适应目标尺寸，多余区域填充透明</summary>
        ProportionalFit,
        /// <summary>等比缩放至铺满目标尺寸，超出部分从中心裁切</summary>
        ProportionalFill,
        /// <summary>强制拉伸到目标尺寸（不保持比例）</summary>
        Stretch,
    }

    /// <summary>
    /// 图片规范化工具
    /// 支持：仅扩展画布 / 等比适应 / 等比填充 / 强制拉伸
    /// </summary>
    public static class ImageNormalizer
    {
        // ─────────────────────────────────────────────────────────────────
        // 公开 API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>获取图片中非透明内容的最小包围矩形</summary>
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
                    if (pixels[y * texture.width + x].a > 0.01f)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY)
                return new RectInt(0, 0, texture.width, texture.height);

            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>
        /// 规范化图片到目标尺寸
        /// </summary>
        /// <param name="source">源纹理</param>
        /// <param name="targetWidth">目标宽度</param>
        /// <param name="targetHeight">目标高度</param>
        /// <param name="alignment">对齐方式（仅 Expand 模式生效）</param>
        /// <param name="resizeMode">缩放模式（默认 Expand，保持原有行为）</param>
        public static Texture2D Normalize(
            Texture2D source,
            int targetWidth,
            int targetHeight,
            ContentAlignment alignment,
            ResizeMode resizeMode = ResizeMode.Expand)
        {
            if (source == null) return null;

            RectInt bounds = GetContentBounds(source);
            int contentW   = bounds.width;
            int contentH   = bounds.height;

            switch (resizeMode)
            {
                case ResizeMode.Expand:
                    return NormalizeExpand(source, targetWidth, targetHeight, alignment, bounds);

                case ResizeMode.ProportionalFit:
                    return NormalizeProportionalFit(source, targetWidth, targetHeight, bounds);

                case ResizeMode.ProportionalFill:
                    return NormalizeProportionalFill(source, targetWidth, targetHeight, bounds);

                case ResizeMode.Stretch:
                    return NormalizeStretch(source, targetWidth, targetHeight, bounds);

                default:
                    return NormalizeExpand(source, targetWidth, targetHeight, alignment, bounds);
            }
        }

        /// <summary>从文件加载纹理（支持项目外路径）</summary>
        public static Texture2D LoadTexture(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Debug.LogError($"[ImageNormalizer] 文件不存在: {path}");
                    return null;
                }

                byte[] data    = File.ReadAllBytes(path);
                Texture2D tex  = new Texture2D(2, 2);

                if (tex.LoadImage(data))
                    return tex;

                Debug.LogError($"[ImageNormalizer] 无法解码图片: {path}");
                UnityEngine.Object.DestroyImmediate(tex);
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImageNormalizer] 加载失败: {path}\n{e.Message}");
                return null;
            }
        }

        /// <summary>保存纹理为 PNG 文件</summary>
        public static bool SaveTexture(Texture2D texture, string path)
        {
            try
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImageNormalizer] 保存失败: {path}\n{e.Message}");
                return false;
            }
        }

        /// <summary>批量处理图片</summary>
        public static int ProcessBatch(
            string[] imagePaths,
            int targetWidth,
            int targetHeight,
            ContentAlignment alignment,
            bool overwrite,
            string namingSuffix = "_normalized",
            ResizeMode resizeMode = ResizeMode.Expand,
            Action<int, int> progressCallback = null)
        {
            int successCount = 0;

            for (int i = 0; i < imagePaths.Length; i++)
            {
                string sourcePath = imagePaths[i];

                Texture2D src = LoadTexture(sourcePath);
                if (src == null)
                {
                    progressCallback?.Invoke(i + 1, imagePaths.Length);
                    continue;
                }

                Texture2D result = Normalize(src, targetWidth, targetHeight, alignment, resizeMode);
                UnityEngine.Object.DestroyImmediate(src);

                if (result == null)
                {
                    progressCallback?.Invoke(i + 1, imagePaths.Length);
                    continue;
                }

                // 确定输出路径
                string outputPath = overwrite
                    ? sourcePath
                    : Path.Combine(
                        Path.GetDirectoryName(sourcePath),
                        Path.GetFileNameWithoutExtension(sourcePath) + namingSuffix + Path.GetExtension(sourcePath));

                if (SaveTexture(result, outputPath))
                    successCount++;

                UnityEngine.Object.DestroyImmediate(result);
                progressCallback?.Invoke(i + 1, imagePaths.Length);
            }

            return successCount;
        }

        // ─────────────────────────────────────────────────────────────────
        // 各模式私有实现
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Expand：仅扩展画布，不缩放内容（原有行为）</summary>
        private static Texture2D NormalizeExpand(
            Texture2D source,
            int targetWidth,
            int targetHeight,
            ContentAlignment alignment,
            RectInt bounds)
        {
            Texture2D result = CreateBlankTexture(targetWidth, targetHeight);

            int offsetX, offsetY;
            switch (alignment)
            {
                case ContentAlignment.Center:
                    offsetX = (targetWidth  - bounds.width)  / 2;
                    offsetY = (targetHeight - bounds.height) / 2;
                    break;
                default: // KeepOriginal
                    offsetX = bounds.x;
                    offsetY = bounds.y;
                    break;
            }

            // 确保不越界
            offsetX = Mathf.Clamp(offsetX, 0, Mathf.Max(0, targetWidth  - bounds.width));
            offsetY = Mathf.Clamp(offsetY, 0, Mathf.Max(0, targetHeight - bounds.height));

            int copyW = Mathf.Min(bounds.width,  targetWidth  - offsetX);
            int copyH = Mathf.Min(bounds.height, targetHeight - offsetY);

            if (copyW > 0 && copyH > 0)
            {
                Color[] pixels = source.GetPixels(bounds.x, bounds.y, copyW, copyH);
                result.SetPixels(offsetX, offsetY, copyW, copyH, pixels);
            }

            result.Apply();
            return result;
        }

        /// <summary>
        /// ProportionalFit：等比缩放，使内容完整显示在目标画布内，多余区域透明
        /// </summary>
        private static Texture2D NormalizeProportionalFit(
            Texture2D source,
            int targetWidth,
            int targetHeight,
            RectInt bounds)
        {
            if (bounds.width <= 0 || bounds.height <= 0)
                return CreateBlankTexture(targetWidth, targetHeight);

            // 计算等比缩放因子（取较小值以保证内容完整显示）
            float scale  = Mathf.Min((float)targetWidth / bounds.width, (float)targetHeight / bounds.height);
            int scaledW  = Mathf.Max(1, Mathf.RoundToInt(bounds.width  * scale));
            int scaledH  = Mathf.Max(1, Mathf.RoundToInt(bounds.height * scale));

            // ★ 浮点误差保护：确保缩放尺寸不超出目标，防止 offsetX/Y 为负导致 SetPixels 越界崩溃
            scaledW = Mathf.Clamp(scaledW, 1, targetWidth);
            scaledH = Mathf.Clamp(scaledH, 1, targetHeight);

            // 提取内容区域并缩放
            Texture2D content = ExtractRegion(source, bounds);
            Texture2D scaled  = ScaleTexture(content, scaledW, scaledH);
            UnityEngine.Object.DestroyImmediate(content);

            if (scaled == null)
                return CreateBlankTexture(targetWidth, targetHeight);

            // 居中放置到目标画布（使用 Max(0,...) 防止偏移量为负）
            Texture2D result = CreateBlankTexture(targetWidth, targetHeight);
            int offsetX = Mathf.Max(0, (targetWidth  - scaledW) / 2);
            int offsetY = Mathf.Max(0, (targetHeight - scaledH) / 2);
            result.SetPixels(offsetX, offsetY, scaledW, scaledH, scaled.GetPixels());
            result.Apply();

            UnityEngine.Object.DestroyImmediate(scaled);
            return result;
        }

        /// <summary>
        /// ProportionalFill：等比缩放至铺满目标尺寸，超出部分从中心裁切
        /// </summary>
        private static Texture2D NormalizeProportionalFill(
            Texture2D source,
            int targetWidth,
            int targetHeight,
            RectInt bounds)
        {
            if (bounds.width <= 0 || bounds.height <= 0)
                return CreateBlankTexture(targetWidth, targetHeight);

            // 计算等比缩放因子（取较大值以保证铺满）
            float scale  = Mathf.Max((float)targetWidth / bounds.width, (float)targetHeight / bounds.height);
            int scaledW  = Mathf.Max(1, Mathf.RoundToInt(bounds.width  * scale));
            int scaledH  = Mathf.Max(1, Mathf.RoundToInt(bounds.height * scale));

            // ★ 浮点误差保护：确保缩放尺寸不低于目标，防止裁切后出现透明条纹
            scaledW = Mathf.Max(scaledW, targetWidth);
            scaledH = Mathf.Max(scaledH, targetHeight);

            Texture2D content = ExtractRegion(source, bounds);
            Texture2D scaled  = ScaleTexture(content, scaledW, scaledH);
            UnityEngine.Object.DestroyImmediate(content);

            if (scaled == null)
                return CreateBlankTexture(targetWidth, targetHeight);

            // 从中心裁切到目标尺寸
            int cropX  = Mathf.Max(0, (scaledW - targetWidth)  / 2);
            int cropY  = Mathf.Max(0, (scaledH - targetHeight) / 2);
            int copyW  = Mathf.Min(targetWidth,  scaledW - cropX);
            int copyH  = Mathf.Min(targetHeight, scaledH - cropY);

            Texture2D result = CreateBlankTexture(targetWidth, targetHeight);
            if (copyW > 0 && copyH > 0)
            {
                Color[] pixels = scaled.GetPixels(cropX, cropY, copyW, copyH);
                result.SetPixels(0, 0, copyW, copyH, pixels);
            }
            result.Apply();

            UnityEngine.Object.DestroyImmediate(scaled);
            return result;
        }

        /// <summary>Stretch：强制将内容区域拉伸到目标尺寸（不保持比例）</summary>
        private static Texture2D NormalizeStretch(
            Texture2D source,
            int targetWidth,
            int targetHeight,
            RectInt bounds)
        {
            if (bounds.width <= 0 || bounds.height <= 0)
                return CreateBlankTexture(targetWidth, targetHeight);

            Texture2D content = ExtractRegion(source, bounds);
            Texture2D scaled  = ScaleTexture(content, targetWidth, targetHeight);
            UnityEngine.Object.DestroyImmediate(content);
            // ★ ScaleTexture 异常时返回空白纹理，防止 NullReferenceException
            return scaled != null ? scaled : CreateBlankTexture(targetWidth, targetHeight);
        }

        // ─────────────────────────────────────────────────────────────────
        // 内部工具方法
        // ─────────────────────────────────────────────────────────────────

        /// <summary>创建全透明的空白纹理（已提交 Apply，可安全直接返回）</summary>
        private static Texture2D CreateBlankTexture(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] clear = new Color[width * height]; // Color 默认 (0,0,0,0)
            tex.SetPixels(clear);
            tex.Apply(); // 必须在此处 Apply，早返回路径依赖本方法保证数据已提交
            return tex;
        }

        /// <summary>从纹理裁切指定区域</summary>
        private static Texture2D ExtractRegion(Texture2D source, RectInt region)
        {
            var result = new Texture2D(region.width, region.height, TextureFormat.RGBA32, false);
            Color[] pixels = source.GetPixels(region.x, region.y, region.width, region.height);
            result.SetPixels(pixels);
            result.Apply();
            return result;
        }

        /// <summary>
        /// 双线性插值缩放纹理
        /// 比 Point Sampling 更平滑，适合 UI 图片
        /// </summary>
        private static Texture2D ScaleTexture(Texture2D source, int newWidth, int newHeight)
        {
            if (source == null || newWidth <= 0 || newHeight <= 0) return null;

            // 尺寸相同时直接复制
            if (newWidth == source.width && newHeight == source.height)
            {
                var copy = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
                copy.SetPixels(source.GetPixels());
                copy.Apply();
                return copy;
            }

            Color[] src = source.GetPixels();
            Color[] dst = new Color[newWidth * newHeight];

            int srcW = source.width;
            int srcH = source.height;

            // 以像素中心映射，避免边缘颜色偏移
            float xRatio = (float)srcW / newWidth;
            float yRatio = (float)srcH / newHeight;

            for (int y = 0; y < newHeight; y++)
            {
                float srcY = (y + 0.5f) * yRatio - 0.5f;
                int   y0   = Mathf.Clamp(Mathf.FloorToInt(srcY), 0, srcH - 1);
                int   y1   = Mathf.Clamp(y0 + 1,                 0, srcH - 1);
                float ty   = Mathf.Clamp01(srcY - y0);

                for (int x = 0; x < newWidth; x++)
                {
                    float srcX = (x + 0.5f) * xRatio - 0.5f;
                    int   x0   = Mathf.Clamp(Mathf.FloorToInt(srcX), 0, srcW - 1);
                    int   x1   = Mathf.Clamp(x0 + 1,                 0, srcW - 1);
                    float tx   = Mathf.Clamp01(srcX - x0);

                    Color c00 = src[y0 * srcW + x0];
                    Color c10 = src[y0 * srcW + x1];
                    Color c01 = src[y1 * srcW + x0];
                    Color c11 = src[y1 * srcW + x1];

                    dst[y * newWidth + x] = Color.Lerp(
                        Color.Lerp(c00, c10, tx),
                        Color.Lerp(c01, c11, tx),
                        ty);
                }
            }

            var result = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
            result.SetPixels(dst);
            result.Apply();
            return result;
        }
    }
}
