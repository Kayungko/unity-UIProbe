---
trigger: always_on
---

1.不需要手动创建.meta文件 此类型文件由unity引擎自动生成。
2.任何回答都使用中文。
3.打包/发布 Release 时，使用本地打包工具，无需手写导出/压缩/校验流程：
  - 脚本位置：`BuildTools/UIProbeReleaseExporter.cs`（已在 .gitignore 中，仅本地使用，不随扩展上线）。
  - 触发方式：Unity 菜单 `UI Probe > 工具 > 导出 Release 包 (unitypackage + zip + sha256)`，
    或通过 MCP `execute_menu_item` 执行同一菜单路径。
  - 产物：一键在 `ReleasePackages/` 生成 `unity-UIProbe-v{VERSION}` 的 unitypackage + zip + 两个 .sha256，
    版本号自动取自 `UIProbeUpdateChecker.VERSION`，unitypackage 仅含 `UIProbe/` 子目录。