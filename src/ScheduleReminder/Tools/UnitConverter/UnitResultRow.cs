namespace ScheduleReminder.Tools.UnitConverter
{
    /// <summary>结果列表中的一行：单位名 + 换算结果文本。</summary>
    public sealed class UnitResultRow
    {
        /// <summary>单位名，如「厘米」。</summary>
        public string UnitName { get; set; }

        /// <summary>换算结果文本，已按格式化规则处理。</summary>
        public string ValueText { get; set; }
    }
}
