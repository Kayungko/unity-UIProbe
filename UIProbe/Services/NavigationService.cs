namespace UIProbe
{
    /// <summary>
    /// 导航服务：持有当前激活 Tab，封装跨模块跳转。
    /// 壳层以 currentTab shim 委托到本服务；迁出的模块经构造注入后调用 GoTo 切换。
    /// </summary>
    internal sealed class NavigationService
    {
        public Tab Current { get; set; } = Tab.Picker;

        public void GoTo(Tab tab) => Current = tab;
    }
}
