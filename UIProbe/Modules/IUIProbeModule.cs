namespace UIProbe
{
    /// <summary>侧栏分区：Top 为常规功能区，Bottom 固定在底部（设置/关于）。</summary>
    internal enum SidebarSection
    {
        Top,
        Bottom
    }

    /// <summary>
    /// UIProbe 功能模块抽象。每个 Tab 对应一个模块，主窗口仅通过此接口调度。
    /// Step 1 阶段为薄适配器，转发回 UIProbeWindow 中现有的 partial 方法。
    /// </summary>
    internal interface IUIProbeModule
    {
        /// <summary>稳定键，未来替代 Tab 枚举。</summary>
        string Id { get; }

        /// <summary>侧栏显示标签。</summary>
        string DisplayName { get; }

        /// <summary>过渡期与现有 currentTab 桥接。</summary>
        Tab Tab { get; }

        bool IsVisible(UIProbeConfig config);

        void Draw();

        void DrawSidebarButton(UIProbeWindow window);

        SidebarSection Section { get; }

        void Apply();

        void Collect();

        void OnEditorUpdate();

        void OnWindowUpdate();

        void OnDestroy();
    }
}
