using System;
using System.Collections.Generic;

namespace ScheduleReminder.Domain.Statistics
{
    /// <summary>统计视角（划分）。</summary>
    public enum StatisticsView
    {
        /// <summary>完成状态：已完成 / 未完成。</summary>
        Completion = 0,

        /// <summary>标签：按标签 ID 归类。</summary>
        Tag = 1
    }

    /// <summary>粒度（分桶方式）。</summary>
    public enum StatisticsGranularity
    {
        /// <summary>按天分桶。</summary>
        Day = 0,

        /// <summary>按周分桶（周一至周日）。</summary>
        Week = 1,

        /// <summary>按自然月分桶（月初至月底）。</summary>
        Month = 2
    }

    /// <summary>一个分桶，对应横轴上的一个点或一列。</summary>
    public class StatisticsBucket
    {
        /// <summary>桶起始日（含）。周粒度为该周周一，月粒度为该月 1 号，首尾不完整时裁剪到所选区间。</summary>
        public DateTime Start { get; set; }

        /// <summary>桶结束日（含）。首尾不完整时裁剪到所选区间。</summary>
        public DateTime End { get; set; }

        /// <summary>实际纳入统计的天数，首尾不完整的桶会小于桶的自然长度。</summary>
        public int CountedDays { get; set; }

        /// <summary>横轴标签：日 M/d，周 M/d–M/d，月 yyyy-MM。</summary>
        public string Label { get; set; }

        /// <summary>各系列在该桶内的数值，键为系列标识。</summary>
        public Dictionary<string, int> Values { get; set; }

        public StatisticsBucket()
        {
            Values = new Dictionary<string, int>(StringComparer.Ordinal);
            Label = string.Empty;
        }
    }

    /// <summary>一条统计系列，对应图例中的一项。</summary>
    public class StatisticsSeries
    {
        /// <summary>系列标识：标签视角为 TagId，未分类为 UntaggedKey，完成状态视角为 DoneKey / UndoneKey。</summary>
        public string Key { get; set; }

        /// <summary>显示名。</summary>
        public string DisplayName { get; set; }

        /// <summary>色位序号（自 0 起），按发生次数降序分配。</summary>
        public int PaletteIndex { get; set; }

        /// <summary>总发生次数。</summary>
        public int Total { get; set; }

        /// <summary>是否为合并产生的「其他标签」。</summary>
        public bool IsOther { get; set; }

        /// <summary>是否为「未分类」。</summary>
        public bool IsUntagged { get; set; }

        public StatisticsSeries()
        {
            Key = string.Empty;
            DisplayName = string.Empty;
        }
    }

    /// <summary>统计结果，整体只读。</summary>
    public class StatisticsResult
    {
        /// <summary>划分视角。</summary>
        public StatisticsView View { get; set; }

        /// <summary>粒度。</summary>
        public StatisticsGranularity Granularity { get; set; }

        /// <summary>区间起点（含）。</summary>
        public DateTime Start { get; set; }

        /// <summary>区间终点（含），恒不晚于今天。</summary>
        public DateTime End { get; set; }

        /// <summary>「共 N 天」，等于 End − Start + 1，含起止两端。</summary>
        public int Days { get; set; }

        /// <summary>总数：区间内全部发生次数。</summary>
        public int Total { get; set; }

        /// <summary>已完成次数。</summary>
        public int Done { get; set; }

        /// <summary>未完成次数，等于总数减已完成。</summary>
        public int Undone { get; set; }

        /// <summary>当前粒度平均值 = 总数 ÷ 当前粒度桶数。</summary>
        public double Average { get; set; }

        /// <summary>完成率 = 已完成 ÷ 总数。</summary>
        public double Rate { get; set; }

        /// <summary>峰值：单个桶内总数的最大值。</summary>
        public int Peak { get; set; }

        /// <summary>峰值所在桶；并列时取最早的那个。</summary>
        public StatisticsBucket PeakBucket { get; set; }

        /// <summary>按发生次数降序排好的系列；超额标签已并入「其他标签」。</summary>
        public List<StatisticsSeries> Series { get; set; }

        /// <summary>横轴桶集合，按时间升序。</summary>
        public List<StatisticsBucket> Buckets { get; set; }

        /// <summary>是否已无可统计数据（用于决定空态）。</summary>
        public bool IsEmpty
        {
            get { return Series.Count == 0 || Total == 0; }
        }

        public StatisticsResult()
        {
            Series = new List<StatisticsSeries>();
            Buckets = new List<StatisticsBucket>();
        }
    }
}
