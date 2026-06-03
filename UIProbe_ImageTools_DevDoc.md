# UIProbe 图片工具模块开发文档

> 适用版本：UIProbe v3.7+
> 涵盖模块：图片规范化（Image Normalizer）· 批量命名（Batch Rename）· 大红大金资源修改导入（Red/Gold Resource Importer）
> 最后更新：2026-06

---

## 目录

1. [模块概述](#1-模块概述)
2. [文件结构](#2-文件结构)
3. [图片规范化模块](#3-图片规范化模块)
   - 3.1 数据类与枚举
   - 3.2 UI 状态字段
   - 3.3 核心算法层
   - 3.4 目标尺寸计算逻辑
   - 3.5 UI 交互流程
   - 3.6 配置持久化
4. [批量命名模块](#4-批量命名模块)
   - 4.1 数据类
   - 4.2 UI 状态字段
   - 4.3 命名规则引擎
   - 4.4 冲突检测机制
   - 4.5 执行与日志
   - 4.6 配置持久化
5. [大红大金资源修改导入模块](#5-大红大金资源修改导入模块)
6. [公共基础设施](#6-公共基础设施)
7. [扩展指南](#7-扩展指南)
8. [已知限制与注意事项](#8-已知限制与注意事项)

---

## 1. 模块概述

三个模块共同挂载在 **图片工具（Image Tools）** 标签页下，通过子标签栏切换：

```
图片工具
├── 📐 图片规范化   ← UIProbeWindow_ImageNormalizer.cs
├── ✏️  批量命名    ← UIProbeWindow_ImageRenamer.cs
└── 大红大金资源修改导入 ← UIProbeWindow_RedGoldImporter.cs
```

三个模块均以 `partial class UIProbeWindow` 形式实现，数据层独立于 UI 层，配置通过 `UIProbeConfig` 统一序列化到 `config.json`。

---

## 2. 文件结构

```
UIProbe/
├── UIProbeWindow_ImageNormalizer.cs    图片规范化 UI + 业务逻辑
├── UIProbeWindow_ImageRenamer.cs       批量命名 UI + 业务逻辑
├── UIProbeWindow_RedGoldImporter.cs    大红大金资源修改导入 UI + 业务逻辑（~1400 行）
│
└── Data/
    ├── ImageNormalizer.cs              图片处理算法（缩放、裁切、画布操作）
    ├── ImageRenameLogManager.cs        批量命名操作日志管理
    ├── DelimitedFileParser.cs          通用 CSV/TSV 分隔文件读写器
    ├── RedGoldDataTypes.cs             大红大金数据类（枚举、导入行、撤销条目等）
    ├── RedGoldPathHelper.cs            路径转换工具（绝对路径 ↔ Assets 相对路径）
    ├── RedGoldNamingState.cs           文件名自动编号分配引擎
    ├── RedGoldNameConverter.cs         拼音/语义命名转换器
    ├── RedGoldUndoManager.cs           栈式多级撤销管理器（最多 10 层）
    ├── RedGoldImageMatcher.cs          图片映射构建与源文件匹配
    ├── RedGoldPresetManager.cs          预设系统（保存/加载/删除配置）
    ├── ExcelFileParser.cs              零依赖 .xlsx 读取器
    └── UIProbeConfig.cs                配置数据类（含图片工具模块的持久化字段）
```

**命名空间级数据类**（定义在对应 `.cs` 文件中，独立于 `partial class`）：

| 类名 | 所在文件 | 用途 |
|------|----------|------|
| `NormalizerImageItem` | ImageNormalizer UI | 单张图片的扫描结果（路径、尺寸、勾选状态） |
| `NormalizerSizeMode` (enum) | ImageNormalizer UI | 目标尺寸计算方式 |
| `NormalizerSizeGroup` | ImageNormalizer UI | 按分辨率聚合的分组 |
| `RenamePreviewItem` | ImageRenamer UI | 单条重命名预览（原名、新名、冲突状态） |
| `ImageRenameLogItem` | ImageRenameLogManager | CSV 日志条目 |
| `ModificationStatus` (enum) | RedGoldDataTypes | 预览行变更状态（新增/已修改/无变化/未知） |
| `RedGoldUndoEntry` | RedGoldDataTypes | 覆盖生成前的撤销记录，保存备份路径与表格旧值 |
| `RedGoldImportRow` | RedGoldDataTypes | 单行导入预览数据（名称、品质、格数、源图、输出路径、状态等） |
| `UnmatchedSourceInfo` | RedGoldDataTypes | 源目录中未被表格匹配到的图片信息 |
| `RedGoldTableData` | RedGoldDataTypes | 表格解析结果（分隔符、行数据、列索引），含 `GetCell`/`SetCell` 静态方法 |
| `RedGoldNamingState` | RedGoldNamingState | 文件名自动编号分配引擎，延续目录命名序号并避免重名 |
| `RedGoldUndoSnapshot` | RedGoldUndoManager | 单次生成操作的撤销快照（条目列表、表格路径、备份目录） |
| `QualityConfigEntry` | RedGoldDataTypes | 品质配置条目（关键字、路径、命名模板、拼音开关） |
| `RedGoldPreset` | RedGoldPresetManager | 预设数据（品质列表、格数规则、尺寸参数） |

---

## 3. 图片规范化模块

### 3.1 数据类与枚举

#### `NormalizerImageItem`

```csharp
internal class NormalizerImageItem
{
    public string Path;       // 完整绝对路径
    public string FileName;   // 文件名（含扩展名）
    public bool   IsSelected; // 是否被勾选（控制是否参与处理）
    public int    Width;      // 原始宽度（扫描时读取并缓存）
    public int    Height;     // 原始高度
    public string SizeLabel;  // 显示用字符串，如 "(512×512)"
}
```

> **性能说明**：宽高在 `ScanImagesForNormalizer()` 中一次性读取并缓存，不在 `OnGUI` 中重复 IO，规避原版每帧 `LoadTexture` 的性能问题。

#### `NormalizerSizeMode`

```csharp
internal enum NormalizerSizeMode
{
    Custom,      // 固定尺寸：全局统一 W × H
    Percentage,  // 百分比：各图按自身原始尺寸等比缩放
    LockWidth,   // 锁定宽度：各图高度自动等比计算
    LockHeight,  // 锁定高度：各图宽度自动等比计算
}
```

#### `NormalizerSizeGroup`

```csharp
internal class NormalizerSizeGroup
{
    public int    Width;      // 该组的原始宽度
    public int    Height;     // 该组的原始高度
    public string SizeLabel;  // 显示标题，如 "512 × 512"
    public List<NormalizerImageItem> Items;
    public bool   IsFoldout;  // 折叠展开状态（仅 UI 用，不持久化）
}
```

分组由 `BuildNormalizerGroups()` 按 `Width_Height` 键聚合，排序规则为**像素总数降序**（大图优先），相同像素数时按数量降序。

---

### 3.2 UI 状态字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `normalizerSourceFolder` | string | 源文件夹路径 |
| `normalizerIncludeSubfolders` | bool | 是否递归子文件夹 |
| `normalizerSizeMode` | NormalizerSizeMode | 当前缩放方式 |
| `normalizerTargetWidth/Height` | int | Custom 模式目标尺寸 |
| `normalizerForceSquare` | bool | Custom 模式正方形锁定 |
| `normalizerScalePercent` | float | Percentage 模式缩放比（1~400） |
| `normalizerLockWidth/Height` | int | LockWidth / LockHeight 目标值 |
| `normalizerNoUpscale` | bool | 仅缩小，不放大 |
| `normalizerMaxDimension` | int | 最大边长限制（0=不限） |
| `normalizerResizeMode` | ResizeMode | 内容缩放模式（Expand/Fit/Fill/Stretch） |
| `normalizerAlignment` | ContentAlignment | 对齐方式（仅 Expand 模式生效） |
| `normalizerOverwrite` | bool | 覆盖原文件或生成新文件 |
| `normalizerNamingSuffix` | string | 新文件后缀（覆盖关闭时使用） |
| `normalizerImageItems` | List\<NormalizerImageItem\> | 扫描结果平铺列表 |
| `normalizerImageGroups` | List\<NormalizerSizeGroup\> | 按分辨率分组后的结构 |

---

### 3.3 核心算法层（`ImageNormalizer.cs`）

#### `ResizeMode` 枚举

```csharp
public enum ResizeMode
{
    Expand,           // 扩展画布，内容不缩放
    ProportionalFit,  // 等比缩放至适应目标，多余区域透明
    ProportionalFill, // 等比缩放至铺满，超出部分从中心裁切
    Stretch,          // 强制拉伸到目标尺寸
}
```

#### 主要公开方法

```csharp
// 规范化单张图片
Texture2D Normalize(Texture2D source, int targetWidth, int targetHeight,
                    ContentAlignment alignment, ResizeMode resizeMode = ResizeMode.Expand)

// 批量处理（Custom 模式使用，统一目标尺寸）
int ProcessBatch(string[] imagePaths, int targetWidth, int targetHeight,
                 ContentAlignment alignment, bool overwrite, string namingSuffix,
                 ResizeMode resizeMode, Action<int, int> progressCallback)

// 获取非透明内容最小包围矩形
RectInt GetContentBounds(Texture2D texture)
```

#### 内部算法方法

| 方法 | 说明 |
|------|------|
| `NormalizeExpand` | 按 alignment 平移内容，不缩放 |
| `NormalizeProportionalFit` | 等比缩小/放大使内容完整显示，居中放置 |
| `NormalizeProportionalFill` | 等比缩放铺满，从中心裁切超出部分 |
| `NormalizeStretch` | 内容直接拉伸到目标 |
| `ScaleTexture` | 双线性插值缩放（以像素中心映射，边缘无色彩偏移） |
| `ExtractRegion` | 裁切纹理的指定矩形区域 |
| `CreateBlankTexture` | 创建全透明画布（内部已调用 Apply） |

#### 浮点安全保护

- `ProportionalFit`：`scaledW/H` 上限钳位到 `targetWidth/Height`，防止 `SetPixels` 越界
- `ProportionalFill`：`scaledW/H` 下限保证 `>= targetWidth/Height`，防止裁切区域不足
- 所有模式：`Normalize` 返回 null 时由调用方降级处理

---

### 3.4 目标尺寸计算逻辑（`ComputeTarget`）

```csharp
private (int w, int h) ComputeTarget(int srcW, int srcH)
```

计算流程（按顺序应用）：

```
1. 按 NormalizerSizeMode 计算初步输出尺寸
   ├── Custom     → 使用 normalizerTargetWidth/Height
   ├── Percentage → outW = Round(srcW * percent / 100)
   ├── LockWidth  → outW = normalizerLockWidth；outH 按比例
   └── LockHeight → outH = normalizerLockHeight；outW 按比例

2. 仅缩小保护（normalizerNoUpscale）
   └── 若 outW > srcW 或 outH > srcH → 还原为 srcW, srcH

3. 最大边长保护（normalizerMaxDimension > 0）
   └── 若 max(outW, outH) > maxDim → 等比缩至上限

4. 返回 (outW, outH)
```

**注意**：当 `srcW <= 0`（未知尺寸）时，回退到 Custom 模式的全局目标值。

---

### 3.5 UI 交互流程

```
用户操作                        方法调用
────────────────────────────────────────────────────────
选择源文件夹 → 点击扫描          ScanImagesForNormalizer()
                                  ├── 读取所有 PNG/JPG 文件
                                  ├── LoadTexture → 缓存 Width/Height（含进度条）
                                  ├── 构建 normalizerImageItems
                                  └── BuildNormalizerGroups()
                                       └── 按 "W_H" 聚合，按像素数降序排列

调整缩放方式 / 参数               OnGUI 实时调用 ComputeTarget()
                                  └── 分组标题与条目箭头实时更新

勾选/取消图片                     item.IsSelected 直接修改
点击文件名                        PingNormalizerAsset(item.Path)
                                  ├── 项目内 → AssetDatabase.PingObject + Selection
                                  └── 项目外 → EditorUtility.RevealInFinder

点击"开始处理"                    StartNormalizerProcessing()
                                  ├── 过滤 IsSelected 条目
                                  ├── 弹出确认对话框（含尺寸描述）
                                  └── 逐图调用 ComputeTarget + ImageNormalizer.Normalize
                                       └── 保存到 outputPath（覆盖 or 新文件名）
```

---

### 3.6 配置持久化

配置类：`ImageNormalizerConfig`（位于 `UIProbeConfig.cs`）

```csharp
[Serializable]
public class ImageNormalizerConfig
{
    public string lastSourceFolder;
    public bool   includeSubfolders;
    public int    targetWidth, targetHeight;
    public bool   forceSquare;
    public string alignment;        // ContentAlignment.ToString()
    public bool   overwrite;
    public string namingSuffix;
    public string resizeMode;       // ResizeMode.ToString()
    // 新增字段（v3.2+）
    public string sizeMode;         // NormalizerSizeMode.ToString()
    public float  scalePercent;
    public int    lockWidth, lockHeight;
    public bool   noUpscale;
    public int    maxDimension;
}
```

读写时机：`ApplyImageNormalizerConfig()`（`OnEnable`）和 `CollectImageNormalizerConfig()`（`OnDisable`），枚举字段使用 `System.Enum.Parse` 反序列化，解析失败时回退为默认值。

---

## 4. 批量命名模块

### 4.1 数据类

#### `RenamePreviewItem`

```csharp
internal class RenamePreviewItem
{
    public string OriginalPath;         // 完整绝对路径
    public string OriginalFileName;     // 原始文件名（含扩展名）
    public string OriginalNameNoExt;    // 原始文件名（不含扩展名）
    public string Extension;            // 扩展名，如 ".png"
    public string RuleGeneratedName;    // 规则引擎生成的名称（不含扩展名）
    public string FinalNameNoExt;       // 最终名称（用户可手动覆盖）
    public bool   IsManualOverride;     // true = 用户已手动编辑，规则变化时不覆盖
    public bool   HasConflict;
    public string ConflictReason;       // "列表内重复" 或 "目标已存在"

    public string FinalFileName => FinalNameNoExt + Extension;  // 完整新文件名
}
```

#### `ImageRenameLogItem`

```csharp
public class ImageRenameLogItem
{
    public string OriginalName;   // 原始文件名
    public string NewName;        // 新文件名
    public string OriginalPath;   // 原始完整路径
    public string TargetFolder;   // 目标文件夹
    public string Mode;           // 操作模式描述
}
```

---

### 4.2 UI 状态字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `renamerSourceFolder` | string | 源图片文件夹 |
| `renamerTargetFolder` | string | 目标文件夹（非覆盖模式使用） |
| `renamerIncludeSubfolders` | bool | 是否递归子文件夹 |
| `renamerOverwriteInPlace` | bool | true=原路径覆盖删原文件；false=复制到目标路径 |
| `renamerPrefix` | string | 命名前缀 |
| `renamerKeepOriginalName` | bool | 是否保留原文件名 |
| `renamerEnableSequence` | bool | 是否启用序号 |
| `renamerSeqStart` | int | 序号起始值 |
| `renamerSeqStep` | int | 序号步长 |
| `renamerSeqDigits` | int | 序号位数（补零） |
| `renamerSuffix` | string | 命名后缀 |
| `renamerUpdateMeta` | bool | 是否同步更新 `.meta` 文件 |
| `renamePreviewList` | List\<RenamePreviewItem\> | 预览条目列表 |
| `renameConflictCount` | int | 当前冲突数量（缓存，避免每帧重算） |

---

### 4.3 命名规则引擎（`BuildRenamerName`）

```csharp
private string BuildRenamerName(string originalNameNoExt, int seqValue)
```

输出 = `string.Join("_", parts)` 其中 `parts` 按如下顺序组装：

```
[前缀（非空时加入）]
[原文件名（renamerKeepOriginalName=true 时加入）]
[序号（renamerEnableSequence=true 时加入，格式为 "D{digits}"）]
[后缀（非空时加入）]
```

若 `parts` 为空（四项均未启用），则回退为 `originalNameNoExt`。

**调用场景**：

| 场景 | 传入 seqValue |
|------|--------------|
| `ScanRenamerImages` 初始构建 | `renamerSeqStart + i * renamerSeqStep` |
| `RegenerateRenamePreview` 重新生成 | 从 `renamerSeqStart` 开始，每项 `+= renamerSeqStep` |
| `DrawRenamerRules` 示例预览 | `renamerSeqStart`（固定展示第一个序号） |

---

### 4.4 冲突检测机制（`ValidateRenameConflicts`）

检测按顺序执行，命中任一规则即标记冲突：

```
① 列表内重复
   统计所有条目的 FinalFileName 出现次数
   若 count > 1 → ConflictReason = "列表内重复"

② 与目标目录已有文件冲突
   effectiveTarget = overwriteInPlace ? sourceFolder : targetFolder
   若目标路径存在同名文件 AND 非文件自身（isSelf 判断）
   → ConflictReason = "目标已存在"
```

**isSelf 判断**：仅在 `renamerOverwriteInPlace=true` 时生效，比较绝对路径（`OrdinalIgnoreCase`），用于跳过"原名即目标名"的情况。

冲突数量缓存到 `renameConflictCount`，执行按钮 disabled 条件为 `renameConflictCount > 0`。

**触发时机**：
- 扫描完成后
- 规则参数变化时（`EditorGUI.EndChangeCheck()` 检测）
- 用户手动编辑某行新文件名后

---

### 4.5 执行与日志（`ExecuteRename`）

#### 执行流程

```
前置校验
  └── 非覆盖模式必须填写目标路径

弹出确认对话框（含文件数、操作模式、Meta同步选项）

逐文件处理：
  ├── 原路径覆盖模式（renamerOverwriteInPlace=true）
  │    ├── destPath != originalPath → File.Copy(src → dest, overwrite:true)
  │    ├── updateMeta → 同步复制 .meta
  │    ├── File.Delete(originalPath)
  │    └── updateMeta → 删除原 .meta
  │
  └── 复制到目标路径模式（renamerOverwriteInPlace=false）
       ├── File.Copy(src → dest, overwrite:true)
       └── updateMeta → 复制 .meta

isInsideProject → AssetDatabase.Refresh()

生成 CSV 日志（ImageRenameLogManager.GenerateLog）
清空 renamePreviewList
```

#### 日志管理（`ImageRenameLogManager`）

- 存储路径：`UIProbe/ImageRenameLogs/yyyy-MM-dd/ImageRename_HHmmss.csv`
- CSV 列：`原文件名, 新文件名, 原路径, 目标文件夹, 操作模式, 执行时间`
- 每次执行独立一个文件（时间戳命名），避免追加覆盖

---

### 4.6 配置持久化

配置类：`BatchRenameConfig`（位于 `UIProbeConfig.cs`）

```csharp
[Serializable]
public class BatchRenameConfig
{
    public string lastSourceFolder, lastTargetFolder;
    public bool   includeSubfolders, overwriteInPlace;
    public string prefix, suffix;
    public bool   keepOriginalName, enableSequence;
    public int    seqStart, seqStep, seqDigits;
    public bool   updateMeta;
}
```

读写时机：由 `ApplyBatchRenameConfig()` / `CollectBatchRenameConfig()` 负责，被 `ApplyImageNormalizerConfig` / `CollectImageNormalizerConfig` 在图片规范化配置读写时顺带调用，无需在 `UIProbeWindow.cs` 的 `OnEnable/OnDisable` 中单独注册。

---

## 5. 大红大金资源修改导入模块

### 5.1 架构概览

v3.5.0 重构后，原 1794 行的巨型文件拆分为 **1 个 UI 层文件 + 7 个 Data 层文件**：

```
UI 层（UIProbeWindow_RedGoldImporter.cs, ~1091 行）
├── 状态字段（路径、折叠状态、滚动位置等）
├── Draw* 方法（EditorGUI 绘制）
├── 编排方法（RedGoldLoadPreview / RedGoldGenerateAndWriteTable）
├── 读取 UI 状态的小方法（GetQualityFolder / ComputeOutputSize 等）
└── Config 持久化（Apply/Collect）

Data 层（UIProbe/Data/）
├── RedGoldDataTypes.cs          数据类 + GetCell/SetCell
├── RedGoldPathHelper.cs         路径转换（绝对 ↔ Assets 相对）
├── DelimitedFileParser.cs       通用 CSV/TSV 读写
├── RedGoldNamingState.cs        文件名自动编号引擎
├── RedGoldNameConverter.cs      拼音/语义命名转换
├── RedGoldUndoManager.cs        栈式多级撤销（最多 10 层）
└── RedGoldImageMatcher.cs       图片映射构建 + 源文件匹配
```

**依赖关系**：UI 层调用 Data 层，Data 层之间仅 `RedGoldImageMatcher` → `RedGoldPathHelper`、`RedGoldUndoManager` → `DelimitedFileParser` + `RedGoldTableData` 存在依赖。

### 5.2 数据类（`RedGoldDataTypes.cs`）

| 类名 | 用途 |
|------|------|
| `ModificationStatus` | 预览行变更状态，区分新增、已修改、无变化和未知 |
| `RedGoldUndoEntry` | 覆盖生成前的撤销记录，保存备份文件路径与表格旧值 |
| `RedGoldImportRow` | 单行导入预览数据，包含表格行号、名称、品质、格数、源图、输出目录、计划路径、变更状态和可编辑输出文件名 |
| `UnmatchedSourceInfo` | 源目录中未被表格匹配到的图片信息 |
| `RedGoldTableData` | 表格解析结果，保存分隔符、全部行数据以及关键列索引；提供 `GetCell`/`SetCell` 静态方法 |

### 5.3 CSV/TSV 解析器（`DelimitedFileParser.cs`）

通用分隔文件读写器，支持 RFC-4180 引号规则：

```csharp
// 读取（自动识别逗号/Tab 分隔符，UTF-8 优先、系统默认编码兜底）
RedGoldTableData ReadTable(string path)

// 写回（UTF-8 无 BOM）
void WriteTable(string path, RedGoldTableData table)
```

内部方法：`ReadAllText`、`ChooseDelimiter`、`ParseDelimited`、`EscapeCell`。

### 5.4 路径工具（`RedGoldPathHelper.cs`）

```csharp
static string ToTablePath(string absolutePath)        // 绝对路径 → "Assets/..." 相对路径
static string ToAbsolutePath(string path)             // Assets/相对路径 → 绝对路径
static string GetDefaultOutputTablePath(string tablePath)  // 生成 "{name}_导入结果{ext}" 路径
static string GetDefaultOutputTableName(string tablePath)  // 同上，仅文件名
static string GetTableExtension(string tablePath)          // 获取扩展名，默认 csv
static string GetExistingDirectory(string path)            // 查找最近的已存在祖先目录
```

### 5.5 命名系统

#### `RedGoldNamingState` — 文件名自动编号引擎

检测目录中已有的命名模式（如 `icon_001.png`、`icon_002.png`），自动递增编号避免冲突：

```csharp
RedGoldNamingState.Create(folder)     // 扫描目录，检测命名模式
state.Allocate(folder, fallbackName)  // 分配下一个文件名
state.ReserveFileName(fileName)       // 预留文件名（防冲突）
state.ReservePreferred(folder, name)  // 尝试使用首选名称，冲突时加后缀
```

#### `RedGoldNameConverter` — 拼音/语义命名转换

红品质资源专用命名：中文名 → `T_Icon_Red_{拼音}.png`

- 内置 ~60 字拼音映射表 + 8 条语义覆盖（如"铁球先生" → `TieQiuXianSheng`）
- 未映射的 CJK 字符输出 "X"，ASCII 字符保持原样

```csharp
static string BuildRedOutputFileName(string displayName)  // "火锅神像" → "T_Icon_Red_HuoGuoShenXiang.png"
static string GetOutputFileNameFromIconPath(string iconPath)  // 从路径提取文件名，确保 .png 扩展名
```

### 5.6 图片匹配器（`RedGoldImageMatcher.cs`）

```csharp
// 构建文件名→路径映射，同名文件自动保留最新版本
static Dictionary<string,string> BuildImageMap(string folder, bool includeSubfolders, out List<string> duplicateWarnings)

// 三级查找：图标文件名 → 名称列 → 旧表格路径（兜底）
static string FindSourceImage(Dictionary<string,string> imageMap, string name, string iconPath)
```

### 5.7 撤销管理器（`RedGoldUndoManager.cs`）— 栈式多级

v3.5.0 从单级撤销升级为栈式多级（最多 10 层）：

```csharp
// 压入快照（超限自动清理最旧备份目录）
void PushSnapshot(List<RedGoldUndoEntry> entries, string tablePath, string description)

// 弹出栈顶并恢复文件和表格数据
UndoResult TryUndo(RedGoldTableData tableData, bool overrideGrid)

// UI 展示属性
bool HasUndo           // 是否有可撤销操作
string CurrentDescription  // 栈顶操作描述
int EntryCount         // 栈顶操作涉及的文件数
int StackDepth         // 当前栈深度
```

`UndoResult` 包含 `Success`、`RestoredFileCount`、`RestoredTableCount`、`Error` 字段。

**存储路径**：`{UIProbeStorage.GetMainFolderPath()}/RedGoldUndo/{yyyyMMdd_HHmmss}/`

### 5.8 UI 状态字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `redGoldTablePath` | string | CSV/TSV 表格路径 |
| `redGoldImageSourceFolder` | string | 待匹配图片源目录 |
| `redGoldIncludeSubfolders` | bool | 是否递归查找源图 |
| `redGoldNameColumn` / `redGoldQualityColumn` | string | 名称列与品质列列名 |
| `redGoldGridLongColumn` / `redGoldGridWideColumn` / `redGoldGridCountColumn` | string | 格数相关列名 |
| `redGoldIconPathColumn` | string | 图标路径回写列名 |
| `redGoldOverrideGrid` | bool | 是否统一覆盖格数 |
| `redGoldCellPixelSize` | int | 非正方形比例的单格像素基准 |
| `redGoldMaxOutputEdge` | int | 1:1 到 6:6 正方形资源的统一边长 |
| `redGoldRedOutputFolder` / `redGoldPurpleOutputFolder` / `redGoldGoldOutputFolder` | string | 红、紫、金品质输出目录 |
| `redGoldOverwriteTable` / `redGoldOutputTablePath` | bool / string | 表格覆盖或另存配置 |
| `redGoldUndoManager` | RedGoldUndoManager | 栈式多级撤销管理器实例 |
| `redGoldFoldReplaceable` / `redGoldFoldMissing` / `redGoldFoldUnmatched` | bool | 三个预览分组的折叠状态 |

### 5.9 处理流程

```
选择表格和图片目录
  ↓
RedGoldLoadPreview()
  ├── DelimitedFileParser.ReadTable()   读取 CSV/TSV
  ├── RedGoldResolveColumns()           匹配表头列名
  ├── RedGoldImageMatcher.BuildImageMap()  建立源图文件名索引
  ├── RedGoldImageMatcher.FindSourceImage()  按图标文件名/名称匹配源图
  ├── RedGoldValidatePreviewRow()       标记缺图、格数无效等问题
  ├── RedGoldNamingState / RedGoldNameConverter  分配输出文件名
  └── 收集未匹配源图并展示在独立预览分组
  ↓
RedGoldGenerateAndWriteTable()
  ├── 生成前备份已有输出文件，记录表格旧值
  ├── ImageNormalizer.Normalize()       等比适配到目标画布
  ├── 按品质输出到红/紫/金目录
  ├── RedGoldWriteBackRow()             回写图标路径和可选格数
  ├── DelimitedFileParser.WriteTable()  覆盖原表或另存结果表
  └── redGoldUndoManager.PushSnapshot()  压入撤销栈
  ↓
↩ 撤销（UI 按钮，显示栈深度）
  └── redGoldUndoManager.TryUndo()      恢复文件 + 表格数据 + AssetDatabase.Refresh
```

### 5.10 配置持久化

配置类：`RedGoldResourceImporterConfig`（位于 `UIProbeConfig.cs`）

```csharp
[Serializable]
public class RedGoldResourceImporterConfig
{
    public string tablePath, imageSourceFolder;
    public bool includeSubfolders;
    public string nameColumn, qualityColumn, gridLongColumn, gridWideColumn, gridCountColumn, iconPathColumn;
    public bool overrideGrid;
    public int overrideGridLong, overrideGridWide, cellPixelSize, maxOutputEdge;
    public string redOutputFolder, purpleOutputFolder, goldOutputFolder;
    public bool overwriteTable;
    public string outputTablePath;
}
```

读写时机：`ApplyRedGoldResourceImporterConfig()` / `CollectRedGoldResourceImporterConfig()` 由图片工具总配置读写流程统一调用，与图片规范化和批量命名保持一致。

---

## 6. 公共基础设施

### `UIProbeStorage` 存储路径

```
UIProbe/（主目录，默认在 AppData）
├── ImageRenameLogs/        批量命名操作日志
│   └── yyyy-MM-dd/
│       └── ImageRename_HHmmss.csv
├── RedGoldUndo/            大红大金撤销备份
│   └── yyyyMMdd_HHmmss/    每次生成操作的旧文件备份
└── Settings/
    └── config.json         所有配置的统一持久化文件
```

获取路径方法：
```csharp
UIProbeStorage.GetImageRenameLogsPath()  // 日志目录
UIProbeStorage.GetMainFolderPath()       // 主目录
UIProbeStorage.GetSettingsPath()         // 配置目录
```

### `UIProbeConfig` / `UIProbeConfigManager`

```csharp
// 加载（OnEnable 调用）
UIProbeConfig config = UIProbeConfigManager.Load();

// 保存（OnDisable 或明确保存时调用）
UIProbeConfigManager.Save(config);
```

使用 `JsonUtility.ToJson/FromJson` 序列化，所有子配置类均标注 `[Serializable]`。

---

## 7. 扩展指南

### 7.1 新增图片处理模式（ResizeMode）

1. 在 `ImageNormalizer.cs` 的 `ResizeMode` 枚举中添加新值
2. 在 `Normalize()` 的 `switch` 中添加对应 `case`，实现处理方法
3. 在 `UIProbeWindow_ImageNormalizer.cs` 的 `resizeModeHint` if-else 链中添加说明文字
4. 无需修改配置类，`resizeMode` 已用字符串序列化（`ToString`/`Enum.Parse`）

### 7.2 新增目标尺寸计算方式（NormalizerSizeMode）

1. 在 `NormalizerSizeMode` 枚举中添加新值
2. 在 `ComputeTarget()` 的 `switch` 中添加计算分支
3. 在 `DrawImageNormalizerContent()` 的 `switch` 中添加对应 UI 控件
4. 在 `StartNormalizerProcessing()` 的确认对话框 `targetSizeDesc` 中补充描述
5. 在 `UIProbeConfig.cs` 的 `ImageNormalizerConfig` 中无需改动（sizeMode 已用字符串持久化）

### 7.3 新增命名规则字段

1. 在 `BuildRenamerName()` 的 `parts.Add(...)` 序列中添加新部分
2. 在 `UIProbeWindow_ImageRenamer.cs` 的 `DrawRenamerRules()` 中添加对应 UI 控件
3. 在 `BatchRenameConfig` 中添加持久化字段
4. 在 `ApplyBatchRenameConfig()` / `CollectBatchRenameConfig()` 中补充读写

---

## 8. 已知限制与注意事项

### 图片规范化

| 限制 | 说明 |
|------|------|
| 支持格式 | 扫描仅识别 `.png` / `.jpg`；算法层通过 `LoadImage` 支持更多格式但未在 UI 层暴露 |
| 大文件处理 | 扫描时逐文件 `LoadTexture` 读取尺寸，文件数量极多时会有明显延迟（已添加进度条） |
| 透明通道 | 输出统一为 RGBA32 PNG，原始 JPG 处理后透明区域会以白色替代（`EncodeToPNG` 行为） |
| 项目外文件 | 处理后不会触发 AssetDatabase.Refresh，`.meta` 同步亦无意义，由操作者自行管理 |
| 多图批量 + 锁宽/锁高 | 不同宽高比的图输出高度/宽度各不相同，处理结果为非统一尺寸集合 |

### 批量命名

| 限制 | 说明 |
|------|------|
| 序列号连续性 | 仅保证列表内序号连续，若中途跳过某些文件（未勾选或过滤），序号不会补偿断档 |
| 顺序依赖 | 原路径覆盖模式下，若两文件存在名称交换（A→B，B→A），两者均会被检测为冲突，需用户手动解决 |
| Meta 同步 | 仅做文件级 Copy/Delete，不调用 `AssetDatabase.RenameAsset`；Unity 重启或刷新后 GUID 关联可能丢失，建议 Unity 项目内使用时通过 AssetDatabase API 重命名 |
| 支持格式 | 扫描识别 `.png/.jpg/.jpeg/.tga/.psd`，其他格式（如 `.webp`）不会出现在列表 |

---

*本文档由 UIProbe 开发团队维护，如有问题请通过 GitHub Issues 反馈。*
