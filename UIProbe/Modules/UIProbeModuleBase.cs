namespace UIProbe
{
    /// <summary>
    /// 模块基类。生命周期钩子默认空实现，绝大多数模块只需重写 Draw()。
    /// 持有 UIProbeWindow 反向引用（GUI 宿主 / Repaint）。
    /// </summary>
    internal abstract class UIProbeModuleBase : IUIProbeModule
    {
        public UIProbeWindow Window { get; private set; }

        public void Bind(UIProbeWindow window) => Window = window;

        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract Tab Tab { get; }
        public abstract bool IsVisible(UIProbeConfig config);
        public abstract void Draw();

        public virtual SidebarSection Section => SidebarSection.Top;

        public virtual void DrawSidebarButton(UIProbeWindow window)
            => window.DrawSidebarButtonPublic(Tab, DisplayName);

        public virtual void Apply() { }
        public virtual void Collect() { }
        public virtual void OnEditorUpdate() { }
        public virtual void OnWindowUpdate() { }
        public virtual void OnDestroy() { }
    }
}
