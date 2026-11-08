using ScheduleReminder.Views;

namespace ScheduleReminder.Tools
{
    /// <summary>计算器工具：标识 calculator，窗口自身保证单实例。</summary>
    public sealed class CalculatorTool : ITool
    {
        public string Id
        {
            get { return "calculator"; }
        }

        public string DisplayName
        {
            get { return "计算器"; }
        }

        public string IconKey
        {
            get { return "IconToolCalculator"; }
        }

        public void Open()
        {
            CalculatorWindow.Open();
        }
    }
}
