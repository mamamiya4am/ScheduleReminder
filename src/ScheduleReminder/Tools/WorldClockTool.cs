using ScheduleReminder.Views;

namespace ScheduleReminder.Tools
{
    /// <summary>世界时钟工具：标识 worldclock，窗口自身保证单实例。</summary>
    public sealed class WorldClockTool : ITool
    {
        public string Id
        {
            get { return "worldclock"; }
        }

        public string DisplayName
        {
            get { return "世界时钟"; }
        }

        public string IconKey
        {
            get { return "IconToolWorldClock"; }
        }

        public void Open()
        {
            WorldClockWindow.Open();
        }
    }
}
