using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScheduleReminder.Domain.Statistics
{
    /// <summary>
    /// 系列构建：系列键解析、显示名解析、名额裁剪与合并、色位分配。
    ///
    /// 口径（对应「统计数据」产品文档 v1.1）：
    ///   标签按 Schedule.TagId 归类，允许重名，不做任何名字归一化；
    ///   TagId 为空 / null = 未分类，恒存在、必须画出、不可隐藏；
    ///   彩色标签最多九个，第 10 个及以后并入「其他标签」，绝不并入「未分类」；
    ///   色位按各系列发生次数降序分配，并列按显示名字母序，再按 TagId 的 Ordinal 序。
    ///
    /// 纯逻辑，不依赖 WPF，可单独测试。
    ///
    /// 依赖 Schedule.TagId 与 TagTemplate.Id，已由《故障及优化记录.md》调整2 落地。
    /// </summary>
    public static class StatisticsSeriesBuilder
    {
        private const string DoneDisplayName = "已完成";
        private const string UndoneDisplayName = "未完成";

        /// <summary>解析某次发生应归入的系列键。</summary>
        public static string ResolveKey(Schedule schedule, StatisticsView view, bool isDone)
        {
            if (view == StatisticsView.Completion)
            {
                return isDone ? StatisticsCaliber.DoneKey : StatisticsCaliber.UndoneKey;
            }

            // 按 TagId 归类。空 / null 一律归未分类，与 Schedule.Tag 文字是否为 "-" 无关。
            string tagId = schedule.TagId;

            if (string.IsNullOrEmpty(tagId))
            {
                return StatisticsCaliber.UntaggedKey;
            }

            return tagId;
        }

        /// <summary>构建系列列表，完成裁剪、合并、排序与色位分配。</summary>
        public static List<StatisticsSeries> BuildList(
            Dictionary<string, int> totals,
            Dictionary<string, string> snapshots,
            IList<TagTemplate> templates,
            StatisticsView view)
        {
            List<StatisticsSeries> list = new List<StatisticsSeries>();

            if (view == StatisticsView.Completion)
            {
                list.Add(MakeSeries(StatisticsCaliber.DoneKey, GetInt(totals, StatisticsCaliber.DoneKey)));
                list.Add(MakeSeries(StatisticsCaliber.UndoneKey, GetInt(totals, StatisticsCaliber.UndoneKey)));
                AssignDisplayNames(list, view, snapshots, templates);
                SortAndAssignPalette(list);
                return list;
            }

            // 标签视角：先拆出未分类，其余按 TagId 各自独立（允许重名）。
            List<StatisticsSeries> tags = new List<StatisticsSeries>();

            foreach (KeyValuePair<string, int> pair in totals)
            {
                if (string.Equals(pair.Key, StatisticsCaliber.UntaggedKey, StringComparison.Ordinal))
                {
                    continue;
                }

                tags.Add(MakeSeries(pair.Key, pair.Value));
            }

            AssignDisplayNames(tags, view, snapshots, templates);
            tags.Sort(CompareSeries);

            // 取前九个各占一色，其余并入「其他标签」。
            int otherSum = 0;
            int otherCount = 0;

            for (int i = 0; i < tags.Count; i++)
            {
                if (i < StatisticsCaliber.ColoredTagMaxSlots)
                {
                    list.Add(tags[i]);
                }
                else
                {
                    otherSum = otherSum + tags[i].Total;
                    otherCount = otherCount + 1;
                }
            }

            // 未分类：数量只由无标签日程决定；标签视角下 0 数量不进入系列。
            int untaggedTotal = GetInt(totals, StatisticsCaliber.UntaggedKey);
            if (untaggedTotal > 0)
            {
                StatisticsSeries untagged = MakeSeries(
                    StatisticsCaliber.UntaggedKey,
                    untaggedTotal);

                untagged.DisplayName = StatisticsCaliber.UntaggedDisplayName;
                untagged.IsUntagged = true;
                list.Add(untagged);
            }

            // 其他标签：仅当不同 TagId 的标签个数超过九个时产生。
            if (otherCount > 0)
            {
                StatisticsSeries other = MakeSeries(StatisticsCaliber.OtherKey, otherSum);
                other.DisplayName = StatisticsCaliber.OtherDisplayName
                    + "（" + otherCount.ToString(CultureInfo.InvariantCulture) + " 个）";
                other.IsOther = true;
                list.Add(other);
            }

            SortAndAssignPalette(list);
            return list;
        }

        private static StatisticsSeries MakeSeries(string key, int total)
        {
            StatisticsSeries series = new StatisticsSeries();
            series.Key = key;
            series.DisplayName = key;
            series.Total = total;
            return series;
        }

        private static void AssignDisplayNames(
            List<StatisticsSeries> list,
            StatisticsView view,
            Dictionary<string, string> snapshots,
            IList<TagTemplate> templates)
        {
            for (int i = 0; i < list.Count; i++)
            {
                list[i].DisplayName = ResolveDisplayName(list[i].Key, view, snapshots, templates);
            }
        }

        /// <summary>显示名：完成状态视角取固定名；标签视角优先存活模板名，孤儿回退快照文字。</summary>
        private static string ResolveDisplayName(
            string key,
            StatisticsView view,
            Dictionary<string, string> snapshots,
            IList<TagTemplate> templates)
        {
            if (view == StatisticsView.Completion)
            {
                return string.Equals(key, StatisticsCaliber.DoneKey, StringComparison.Ordinal)
                    ? DoneDisplayName
                    : UndoneDisplayName;
            }

            if (string.Equals(key, StatisticsCaliber.UntaggedKey, StringComparison.Ordinal))
            {
                return StatisticsCaliber.UntaggedDisplayName;
            }

            if (templates != null)
            {
                for (int i = 0; i < templates.Count; i++)
                {
                    TagTemplate template = templates[i];

                    if (template == null)
                    {
                        continue;
                    }

                    if (string.Equals(template.Id, key, StringComparison.Ordinal)
                        && !string.IsNullOrEmpty(template.Name))
                    {
                        return template.Name;
                    }
                }
            }

            // 模板已删除：回退到日程上保存的名字快照，保证系列不会丢失标签。
            string snapshot;

            if (snapshots != null
                && snapshots.TryGetValue(key, out snapshot)
                && !string.IsNullOrEmpty(snapshot))
            {
                return snapshot;
            }

            return key;
        }

        /// <summary>排序：发生次数降序 → 显示名 Ordinal 升序 → TagId 的 Ordinal 升序。</summary>
        private static int CompareSeries(StatisticsSeries a, StatisticsSeries b)
        {
            if (a.Total != b.Total)
            {
                return b.Total.CompareTo(a.Total);
            }

            int byName = string.CompareOrdinal(a.DisplayName, b.DisplayName);

            if (byName != 0)
            {
                return byName;
            }

            // 允许重名，名字相同则用 TagId 兜底，保证顺序稳定可复现。
            return string.CompareOrdinal(a.Key, b.Key);
        }

        /// <summary>排序后按顺序分配色位：次数最多的取第一个色位。</summary>
        private static void SortAndAssignPalette(List<StatisticsSeries> list)
        {
            list.Sort(CompareSeries);

            for (int i = 0; i < list.Count; i++)
            {
                list[i].PaletteIndex = i;
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
