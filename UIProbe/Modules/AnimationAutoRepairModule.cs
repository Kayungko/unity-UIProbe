namespace UIProbe
{
    internal sealed class AnimationAutoRepairModule : UIProbeModuleBase
    {
        public override string Id => "animationAutoRepair";
        public override string DisplayName => "动画修复";
        public override Tab Tab => Tab.AnimationAutoRepair;
        public override bool IsVisible(UIProbeConfig config) => config == null || config.modulesVisibility.showAnimationAutoRepair;
        public override void Draw() => Window.DrawAnimationAutoRepairTab_Bridge();

        // 自定义侧栏按钮：含 Animator/Animation 组件时显示 ⚠ 角标。
        public override void DrawSidebarButton(UIProbeWindow window)
            => window.DrawAnimationAutoRepairSidebarButton_Bridge();
    }
}
