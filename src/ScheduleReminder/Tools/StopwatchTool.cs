using ScheduleReminder.Views;

namespace ScheduleReminder.Tools
{
    /// <summary>秒表工具：标识 stopwatch，窗口自身保证单实例。</summary>
    public sealed class StopwatchTool : ITool
    {
        public string Id
        {
            get { return "stopwatch"; }
        }

        public string DisplayName
        {
            get { return "秒表"; }
        }

        public string IconKey
        {
            get { return "IconToolStopwatch"; }
        }

        public void Open()
        {
            StopwatchWindow.Open();
        }
    }
}
