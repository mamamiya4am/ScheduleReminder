using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScheduleReminder.Domain.Statistics
{
    /// <summary>
    /// 分桶：把一段日期区间切成横轴上的桶。
    /// 日 = 一天一桶；周 = 周一至周日一桶；月 = 自然月一桶。
    /// 首尾不完整的桶只保留落在区间内的部分，不补齐到整周 / 整月。
    ///
    /// 纯逻辑，不依赖 WPF，可单独测试。
    /// </summary>
    public static class StatisticsBucketing
    {
        /// <summary>按粒度切桶，返回按时间升序排列的桶集合。</summary>
        public static List<StatisticsBucket> BuildBuckets(
            DateTime start,
            DateTime end,
            StatisticsGranularity granularity)
        {
            List<StatisticsBucket> buckets = new List<StatisticsBucket>();

            if (end < start)
            {
                return buckets;
            }

            DateTime cursor = start.Date;
            DateTime last = end.Date;

            while (cursor <= last)
            {
                DateTime bucketStart;
                DateTime bucketEnd;

                if (granularity == StatisticsGranularity.Week)
                {
                    bucketStart = StartOfWeek(cursor);
                    bucketEnd = bucketStart.AddDays(6);
                }
                else if (granularity == StatisticsGranularity.Month)
                {
                    bucketStart = new DateTime(cursor.Year, cursor.Month, 1);
                    bucketEnd = bucketStart.AddMonths(1).AddDays(-1);
                }
                else
                {
                    bucketStart = cursor;
                    bucketEnd = cursor;
                }

                // 首尾不完整的桶裁剪到所选区间，不补齐。
                if (bucketStart < start)
                {
                    bucketStart = start;
                }

                if (bucketEnd > last)
                {
                    bucketEnd = last;
                }

                StatisticsBucket bucket = new StatisticsBucket();
                bucket.Start = bucketStart;
                bucket.End = bucketEnd;
                bucket.CountedDays = (bucketEnd - bucketStart).Days + 1;
                bucket.Label = BuildLabel(bucketStart, bucketEnd, granularity);
                buckets.Add(bucket);

                cursor = bucketEnd.AddDays(1);
            }

            return buckets;
        }

        /// <summary>建「日期 → 桶下标」映射，供遍历时 O(1) 定位，避免循环内重复查找。</summary>
        public static Dictionary<DateTime, int> BuildIndexMap(List<StatisticsBucket> buckets)
        {
            Dictionary<DateTime, int> map = new Dictionary<DateTime, int>();

            for (int i = 0; i < buckets.Count; i++)
            {
                StatisticsBucket bucket = buckets[i];

                for (DateTime day = bucket.Start; day <= bucket.End; day = day.AddDays(1))
                {
                    map[day] = i;
                }
            }

            return map;
        }

        /// <summary>取该日期所在周的周一。</summary>
        private static DateTime StartOfWeek(DateTime date)
        {
            int diff = (7 + ((int)date.DayOfWeek - (int)StatisticsCaliber.WeekStart)) % 7;
            return date.Date.AddDays(-diff);
        }

        /// <summary>横轴标签：日 M/d，周 M/d–M/d，月 yyyy-MM。</summary>
        private static string BuildLabel(
            DateTime bucketStart,
            DateTime bucketEnd,
            StatisticsGranularity granularity)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;

            if (granularity == StatisticsGranularity.Week)
            {
                return bucketStart.Month.ToString(inv) + "/"
                    + bucketStart.Day.ToString(inv) + "–"
                    + bucketEnd.Month.ToString(inv) + "/"
                    + bucketEnd.Day.ToString(inv);
            }

            if (granularity == StatisticsGranularity.Month)
            {
                return bucketStart.Year.ToString(inv) + "-"
                    + bucketStart.Month.ToString("00", inv);
            }

            return bucketStart.Month.ToString(inv) + "/"
                + bucketStart.Day.ToString(inv);
        }
    }
}
