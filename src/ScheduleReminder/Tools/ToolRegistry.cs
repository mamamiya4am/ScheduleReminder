using System.Collections.Generic;

namespace ScheduleReminder.Tools
{
    /// <summary>
    /// 工具注册表：保存全部已注册工具，按注册顺序返回。
    /// 注册点在 App.xaml.cs 的 OnStartup 中 ThemeService.ApplySettings() 之后、MainWindow 创建之前。
    /// </summary>
    public static class ToolRegistry
    {
        private static readonly List<ITool> Tools = new List<ITool>();

        /// <summary>注册一个工具；工具为空、标识为空或标识重复时静默忽略。</summary>
        public static void Register(ITool tool)
        {
            if (tool == null || string.IsNullOrEmpty(tool.Id))
            {
                return;
            }
            for (int i = 0; i < Tools.Count; i++)
            {
                if (Tools[i].Id == tool.Id)
                {
                    return;
                }
            }
            Tools.Add(tool);
        }

        /// <summary>按注册顺序返回全部工具（只读视图，外部不可改列表）。</summary>
        public static IList<ITool> GetTools()
        {
            return Tools.AsReadOnly();
        }

        /// <summary>按工具标识查找；找不到返回 null（调用方 ToolHost 负责忽略）。</summary>
        public static ITool Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            for (int i = 0; i < Tools.Count; i++)
            {
                if (Tools[i].Id == id)
                {
                    return Tools[i];
                }
            }
            return null;
        }
    }
}
