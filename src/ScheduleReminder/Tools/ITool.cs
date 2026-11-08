namespace ScheduleReminder.Tools
{
    /// <summary>
    /// 小工具统一接口（方案《小工具增加方案.md》『小工具框架设计』一节）。
    /// 每个小工具给出工具标识、显示名称、图标资源键与打开方法：标识用于单实例管理与入口查找，
    /// 显示名称用于面板与菜单文字，图标键指向 Views\ToolBoxFlyout.xaml 里的 Geometry。
    /// 窗口自身的单实例由各窗口的静态 Open 方法承担。
    /// </summary>
    public interface ITool
    {
        /// <summary>工具标识：入口调用与注册表查找的键，与方案中约定的字符串一致。</summary>
        string Id { get; }

        /// <summary>显示名称：小工具面板、菜单与按钮上的中文名称。</summary>
        string DisplayName { get; }

        /// <summary>
        /// 图标资源键：对应 Views\ToolBoxFlyout.xaml 里的 Geometry 键名。
        /// 小工具面板按此键取几何图形渲染图标；取不到时该格只显示名称、不影响点击。
        /// </summary>
        string IconKey { get; }

        /// <summary>打开窗口；若已打开则由窗口自身负责激活并前置。</summary>
        void Open();
    }
}
