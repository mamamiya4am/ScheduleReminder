using System;

namespace ScheduleReminder.Domain.Statistics
{
    /// <summary>
    /// 统计窗口打开时的默认状态。纯常量与小工具方法，不依赖 WPF。
    ///
    /// 口径（对应「统计数据」产品文档）：
    ///   默认区间「近 30 天」＝今天往前推 29 天至今天，含起止两端，共 30 天；
    ///   默认粒度「周」；默认视角「完成状态」。
    ///
    /// 单独成文件而不塞进 StatisticsCaliber：Caliber 放的是统计口径常量，
    /// 这里是窗口的初始状态，两者生命周期不同，混在一起容易被误当成口径。
    /// </summary>
    public static class StatisticsDefaults
    {
        /// <summary>默认粒度：周。</summary>
        public const StatisticsGranularity Granularity = StatisticsGranularity.Week;

        /// <summary>默认视角：完成状态。</summary>
        public const StatisticsView View = StatisticsView.Completion;

        /// <summary>默认区间天数：近 30 天，含今天当天。</summary>
        public const int RangeDays = 30;

        /// <summary>
        /// 默认区间起点：今天往前推 RangeDays − 1 天。
        /// 例：今天 10 月 21 日 → 9 月 22 日，两者含起止共 30 天。
        /// </summary>
        public static DateTime StartDate(DateTime today)
        {
            return today.Date.AddDays(-(RangeDays - 1));
        }
    }
}
