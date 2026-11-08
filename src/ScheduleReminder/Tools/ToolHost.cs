using System;
using ScheduleReminder.Services;

namespace ScheduleReminder.Tools
{
    /// <summary>
    /// 工具宿主：统一打开入口。任何入口（主界面小工具面板、托盘菜单、单元格右键菜单）只调这一个方法。
    /// 按工具标识查注册表，找到就让其打开；单实例由各窗口的静态 Open 方法保证。
    /// </summary>
    public static class ToolHost
    {
        /// <summary>
        /// 按标识打开工具。未注册只记日志、不弹窗；已注册工具打开失败会记日志并提示，
        /// 避免单个小工具的异常把整个主程序带崩。
        /// </summary>
        public static void Open(string id)
        {
            ITool tool = ToolRegistry.Find(id);
            if (tool != null)
            {
                try
                {
                    tool.Open();
                }
                catch (Exception ex)
                {
                    // 单个小工具坏了，不能让整个主程序被带走。
                    LogWriter.Write("小工具打开失败：" + (id ?? "(null)"), ex);
                    DialogService.Alert("小工具", "打开小工具失败：" + ex.Message);
                }
                return;
            }
            LogWriter.Write("未注册的小工具标识：" + (id ?? "(null)"));
        }
    }
}
