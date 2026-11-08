using System;
using System.Globalization;

namespace ScheduleReminder.Domain.Statistics
{
    /// <summary>
    /// 统计口径常量。所有与统计相关的"规则数值"集中在此，
    /// 便于窗口、引擎、验收脚本共用同一套定义，避免三处各写一份。
    ///
    /// 本文件为纯逻辑，不依赖 WPF，可单独测试。
    /// </summary>
    public static class StatisticsCaliber
    {
        /// <summary>图例总项数上限，包含「总计」这一项。</summary>
        public const int LegendMaxItems = 12;

        /// <summary>
        /// 彩色标签最多占用的色位数。
        /// 推导：LegendMaxItems(12) − 总计(1) − 未分类(1) − 其他标签(1) = 9。
        /// </summary>
        public const int ColoredTagMaxSlots = 9;

        /// <summary>所有概览数字保留的小数位数。</summary>
        public const int DecimalPlaces = 2;

        /// <summary>一周的起始日，与程序周视图保持一致。</summary>
        public const DayOfWeek WeekStart = DayOfWeek.Monday;

        /// <summary>「未分类」系列的显示名。</summary>
        public const string UntaggedDisplayName = "未分类";

        /// <summary>「其他标签」系列的显示名。</summary>
        public const string OtherDisplayName = "其他标签";

        /// <summary>「总计」系列的显示名，统一叫「总计」，不再写「当天」。</summary>
        public const string TotalDisplayName = "总计";

        /// <summary>完成状态视角下「已完成」系列的固定键。</summary>
        public const string DoneKey = "__done__";

        /// <summary>完成状态视角下「未完成」系列的固定键。</summary>
        public const string UndoneKey = "__undone__";

        /// <summary>「未分类」系列的固定键，用于字典避免 null 键。</summary>
        public const string UntaggedKey = "__untagged__";

        /// <summary>「其他标签」系列的固定键。</summary>
        public const string OtherKey = "__other__";

        /// <summary>
        /// 判断某次发生是否计入统计：只统计「发生日期 ≤ 今天」的出现，未来的不计入。
        /// </summary>
        /// <remarks>
        /// 按发生日期判定，不能用 ScheduleOccurrence.IsPast 代替。
        /// IsPast 的语义是 alarmTime &lt; now，受当天具体时刻与提前量影响，
        /// 与本口径不同，混用会让今天早上已过点的日程被误判为不应计入。
        /// </remarks>
        public static bool IsCounted(DateTime occurrenceDate, DateTime today)
        {
            return occurrenceDate.Date <= today.Date;
        }

        /// <summary>
        /// 数值格式化：整数不补小数；仅真正产生小数的数字保留两位（四舍五入）；
        /// 值为 0 时返回「0」，不返回「—」或空白。
        /// </summary>
        /// <param name="value">待格式化数值。</param>
        /// <returns>格式化后的字符串。</returns>
        public static string FormatNumber(double value)
        {
            if (Math.Abs(value) < 0.005)
            {
                return "0";
            }
            // 整数不补小数；仅真正产生小数的数字保留两位（四舍五入）。
            if (value == Math.Floor(value) && !double.IsInfinity(value))
            {
                return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            }
            return value.ToString("F" + DecimalPlaces, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 完成率格式化：带百分号的两位小数；总数为 0 时返回「0」。
        /// </summary>
        /// <param name="done">已完成次数。</param>
        /// <param name="total">总次数。</param>
        /// <returns>格式化后的完成率字符串。</returns>
        public static string FormatRate(double done, double total)
        {
            if (total <= 0)
            {
                return "0";
            }
            return FormatNumber(done / total * 100.0) + "%";
        }
    }
}
