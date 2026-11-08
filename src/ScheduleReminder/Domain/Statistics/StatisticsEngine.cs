using System;
using System.Collections.Generic;
using ScheduleReminder.Models;

namespace ScheduleReminder.Domain.Statistics
{
    /// <summary>
    /// 统计引擎，纯逻辑，不依赖 WPF，可后台线程执行，可通过 isCancelled 中止。
    ///
    /// 口径（对应「统计数据」产品文档 v1.1）：
    ///   区间终点恒为今天，未来日程一律不计入（只统计发生日期 ≤ 今天）；
    ///   标签按 Schedule.TagId 归类，不按名字，允许重名；
    ///   未分类 = TagId 空，必须画出；彩色标签最多九个，其余并入「其他标签」；
    ///   总计线颜色固定、默认关闭、不参与色板分配，由窗口层单独处理。
    ///
    /// 性能铁律：按日程遍历，不按天遍历；严禁逐日调用 ScheduleService.GetOccurrences(day)。
    /// 进循环前预解析 DateValue 与 Recurrence.Until，循环内只调 RecurrenceService.RuleHit。
    ///
    /// Schedule.TagId 与 TagTemplate.Id 已由《故障及优化记录.md》调整2 落地。
    /// </summary>
    public static class StatisticsEngine
    {
        /// <summary>计算统计结果（不可取消）。</summary>
        public static StatisticsResult Compute(
            IList<Schedule> all,
            IList<TagTemplate> templates,
            DateTime start,
            DateTime end,
            StatisticsGranularity granularity,
            StatisticsView view)
        {
            return Compute(all, templates, start, end, granularity, view, null);
        }

        /// <summary>计算统计结果；isCancelled 返回 true 时立即中止并返回 null。</summary>
        public static StatisticsResult Compute(
            IList<Schedule> all,
            IList<TagTemplate> templates,
            DateTime start,
            DateTime end,
            StatisticsGranularity granularity,
            StatisticsView view,
            Func<bool> isCancelled)
        {
            // 一、区间裁剪：终点恒不晚于今天；起止颠倒后退化为空区间（今天—今天）。
            DateTime today = DateTime.Now.Date;
            DateTime s = start.Date;
            DateTime e = end.Date;

            // 调用方若传了未来起点（窗口本应禁止），不能把区间夹成今天后再统计今天。
            bool requestedFutureRange = s > today;

            if (s > e)
            {
                DateTime t = s;
                s = e;
                e = t;
            }

            if (e > today)
            {
                e = today;
            }

            if (s > e)
            {
                s = e;
            }

            StatisticsResult result = new StatisticsResult();
            result.View = view;
            result.Granularity = granularity;
            result.Start = s;
            result.End = e;
            result.Days = (e - s).Days + 1;
            result.Buckets = StatisticsBucketing.BuildBuckets(s, e, granularity);

            Dictionary<DateTime, int> bucketIndexByDate =
                StatisticsBucketing.BuildIndexMap(result.Buckets);

            Dictionary<string, int> totals = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, string> snapshots = new Dictionary<string, string>(StringComparer.Ordinal);

            int done = 0;
            int total = 0;

            // 二、按日程遍历，每条只走「自己的有效区间 ∩ 所选区间」。
            // 未来起点请求直接返回空结果，避免“未来区间被夹成今天后误统计今天”。
            if (!requestedFutureRange && all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (isCancelled != null && isCancelled())
                    {
                        return null;
                    }

                    Schedule schedule = all[i];

                    if (schedule == null)
                    {
                        continue;
                    }

                    // 预解析一次：循环内不再触碰任何日期字符串。
                    DateTime begin = schedule.DateValue.Date;

                    // 与 ScheduleService.GetOccurrences 对齐：越界/损坏日期不进入统计。
                    if (begin < DateUtil.MinSupportedDate || begin > DateUtil.MaxSupportedDate)
                    {
                        continue;
                    }

                    RecurrenceRule rule = schedule.Recurrence;
                    bool recurring = rule != null && rule.Enabled;

                    DateTime? until = null;

                    if (recurring)
                    {
                        DateTime parsedUntil;

                        if (DateUtil.TryParseDateText(rule.Until, out parsedUntil))
                        {
                            until = parsedUntil.Date;
                        }
                    }

                    // 一次性日程：只在起始日发生一次，不进逐日循环。
                    // 若让它进循环，区间内每一天都会被误计一次，数字全错且退化成「日程数 × 天数」。
                    if (!recurring)
                    {
                        if (begin < s || begin > e)
                        {
                            continue;
                        }

                        Accumulate(result, schedule, view, begin, today,
                            bucketIndexByDate, totals, snapshots, ref total, ref done);
                        continue;
                    }

                    DateTime from = begin > s ? begin : s;
                    DateTime to = (until.HasValue && until.Value < e) ? until.Value : e;

                    if (from > to)
                    {
                        continue;
                    }

                    for (DateTime day = from; day <= to; day = day.AddDays(1))
                    {
                        if (!StatisticsCaliber.IsCounted(day, today))
                        {
                            continue;
                        }

                        if (!RecurrenceService.RuleHit(rule, day))
                        {
                            continue;
                        }

                        Accumulate(result, schedule, view, day, today,
                            bucketIndexByDate, totals, snapshots, ref total, ref done);
                    }
                }
            }

            // 三、汇总。
            result.Total = total;
            result.Done = done;
            result.Undone = total - done;

            // 平均值按当前粒度的桶数计算：
            // 日粒度 = 每日平均，周粒度 = 每周平均，月粒度 = 每月平均。
            result.Average = result.Buckets.Count > 0
                ? (double)total / result.Buckets.Count
                : 0.0;
            result.Rate = total > 0 ? (double)done / total : 0.0;
            result.Series = StatisticsSeriesBuilder.BuildList(totals, snapshots, templates, view);

            // 被裁掉的标签在桶里仍以原 TagId 存着值，先合并到「其他标签」，
            // 否则那条线读到的永远是 0。
            MergeOtherIntoBuckets(result);

            NormalizeBuckets(result);
            ComputePeak(result);
            return result;
        }

        /// <summary>把一次发生计入统计。桶定位失败（越界）时只累计、不落桶。</summary>
        private static void Accumulate(
            StatisticsResult result,
            Schedule schedule,
            StatisticsView view,
            DateTime day,
            DateTime today,
            Dictionary<DateTime, int> bucketIndexByDate,
            Dictionary<string, int> totals,
            Dictionary<string, string> snapshots,
            ref int total,
            ref int done)
        {
            // 只统计发生日期 ≤ 今天。不用 IsPast，那是 alarmTime < now。
            if (!StatisticsCaliber.IsCounted(day, today))
            {
                return;
            }

            bool isDone = schedule.IsDone(day);
            string key = StatisticsSeriesBuilder.ResolveKey(schedule, view, isDone);

            total = total + 1;

            if (isDone)
            {
                done = done + 1;
            }

            totals[key] = GetInt(totals, key) + 1;
            snapshots[key] = schedule.Tag;

            int bi;

            if (bucketIndexByDate.TryGetValue(day, out bi))
            {
                Dictionary<string, int> values = result.Buckets[bi].Values;
                values[key] = GetInt(values, key) + 1;
            }
        }

        /// <summary>
        /// 把被裁掉的标签在桶里的值合并到「其他标签」键上。
        /// 累积阶段用的是原始 TagId 作键，而 Series 里只保留了前九个，
        /// 若不合并，「其他标签」系列在每个桶里读到的都是 0，线永远是平的。
        /// </summary>
        private static void MergeOtherIntoBuckets(StatisticsResult result)
        {
            if (result.View == StatisticsView.Completion)
            {
                return;
            }

            StatisticsSeries other = null;

            for (int i = 0; i < result.Series.Count; i++)
            {
                if (result.Series[i].IsOther)
                {
                    other = result.Series[i];
                    break;
                }
            }

            if (other == null)
            {
                return;
            }

            // 保留下来的键：除「其他标签」外 Series 里的全部键。
            HashSet<string> kept = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < result.Series.Count; i++)
            {
                if (!result.Series[i].IsOther)
                {
                    kept.Add(result.Series[i].Key);
                }
            }

            for (int i = 0; i < result.Buckets.Count; i++)
            {
                Dictionary<string, int> values = result.Buckets[i].Values;
                int mergeSum = 0;
                List<string> dropped = null;

                foreach (KeyValuePair<string, int> pair in values)
                {
                    if (kept.Contains(pair.Key))
                    {
                        continue;
                    }

                    mergeSum = mergeSum + pair.Value;

                    if (dropped == null)
                    {
                        dropped = new List<string>();
                    }

                    dropped.Add(pair.Key);
                }

                if (dropped == null)
                {
                    continue;
                }

                for (int k = 0; k < dropped.Count; k++)
                {
                    values.Remove(dropped[k]);
                }

                values[other.Key] = GetInt(values, other.Key) + mergeSum;
            }
        }

        /// <summary>补齐每个桶内缺失的系列键为 0，画图时不必再判断键是否存在。</summary>
        public static void NormalizeBuckets(StatisticsResult result)
        {
            for (int i = 0; i < result.Buckets.Count; i++)
            {
                Dictionary<string, int> values = result.Buckets[i].Values;

                for (int j = 0; j < result.Series.Count; j++)
                {
                    string key = result.Series[j].Key;

                    if (!values.ContainsKey(key))
                    {
                        values[key] = 0;
                    }
                }
            }
        }

        /// <summary>峰值：单桶内全部系列数值之和的最大值；并列时取最早的桶。</summary>
        public static void ComputePeak(StatisticsResult result)
        {
            result.Peak = 0;
            result.PeakBucket = null;

            for (int i = 0; i < result.Buckets.Count; i++)
            {
                StatisticsBucket bucket = result.Buckets[i];
                int sum = 0;

                // 按桶内全部键求和，不按 Series 求和。
                // 只按 Series 求和会漏掉未进 Series 的键，导致峰值偏小。
                foreach (KeyValuePair<string, int> pair in bucket.Values)
                {
                    sum = sum + pair.Value;
                }

                // 严格大于才更新，因此并列时保留最早的那个桶。
                if (result.PeakBucket == null || sum > result.Peak)
                {
                    result.Peak = sum;
                    result.PeakBucket = bucket;
                }
            }
        }

        private static int GetInt(Dictionary<string, int> map, string key)
        {
            int value;

            if (map != null && map.TryGetValue(key, out value))
            {
                return value;
            }

            return 0;
        }
    }
}
