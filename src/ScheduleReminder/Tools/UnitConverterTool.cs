using ScheduleReminder.Views;

namespace ScheduleReminder.Tools
{
    /// <summary>单位换算工具：标识 unitconverter，窗口自身保证单实例。</summary>
    public sealed class UnitConverterTool : ITool
    {
        public string Id
        {
            get { return "unitconverter"; }
        }

        public string DisplayName
        {
            get { return "单位换算"; }
        }

        public string IconKey
        {
            get { return "IconToolUnitConverter"; }
        }

        public void Open()
        {
            UnitConverterWindow.Open();
        }
    }
}
