using System;
using System.Collections.Generic;
using System.Globalization;
using ScheduleReminder.Models;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Domain
{
    /// <summary>
    /// 重复规则的命中判定、锚点归一（P0-8 ⑧）与摘要显示语法（P0-3 ⑥）。
    /// 生效窗口为闭区间 [date, until]；until 为空 = 永久；边界不命中一律静默跳过。
    /// </summary>
    public static class RecurrenceService
    {
        private const int DefaultSearchDays = 12000;

        /// <summary>该日期是否命中该日程（窗口 + 规则）。</summary>
        public static bool IsHit(Schedule schedule, DateTime date)
        {
            if (schedule == null)
            {
                return false;
            }
            DateTime start = schedule.DateValue;
            if (date < start)
            {
                return false;
            }
            DateTime? until = DateUtil.ParseDateNullable(schedule.Recurrence != null ? schedule.Recurrence.Until : null);
            if (until.HasValue && date > until.Value)
            {
                return false;
            }
            if (!schedule.IsRecurring)
            {
                return date == start;
            }
            return RuleHit(schedule.Recurrence, date);
        }

        /// <summary>纯规则命中判定（不含窗口）。五类规则可复选，取并集。</summary>
        public static bool RuleHit(RecurrenceRule rule, DateTime date)
        {
            if (rule == null || !rule.Enabled)
            {
                return false;
            }

            if (rule.Yearly != null && rule.Yearly.Month > 0 && rule.Yearly.Day > 0)
            {
                if (rule.Yearly.Month == date.Month && rule.Yearly.Day == date.Day)
                {
                    return true;
                }
            }

            if (rule.Monthly != null && rule.Monthly.Day > 0)
            {
                if (date.Day == rule.Monthly.Day)
                {
                    return true;
                }
            }

            if (rule.MonthlyLastN != null && rule.MonthlyLastN.N >= 1 && rule.MonthlyLastN.N <= 7)
            {
                int lastDay = DateTime.DaysInMonth(date.Year, date.Month);
                if (date.Day == lastDay - (rule.MonthlyLastN.N - 1))
                {
                    return true;
                }
            }

            if (rule.MonthlyLastWeekday != null
                && rule.MonthlyLastWeekday.Weekday >= 0
                && rule.MonthlyLastWeekday.Weekday <= 6)
            {
                int lastDay = DateTime.DaysInMonth(date.Year, date.Month);
                DateTime last = new DateTime(date.Year, date.Month, lastDay);
                while ((int)last.DayOfWeek != rule.MonthlyLastWeekday.Weekday)
                {
                    last = last.AddDays(-1);
                }
                if (date == last)
                {
                    return true;
                }
            }

            if (rule.Weekly != null && rule.Weekly.Weekdays != null)
            {
                int weekday = (int)date.DayOfWeek;
                for (int i = 0; i < rule.Weekly.Weekdays.Count; i++)
                {
                    if (rule.Weekly.Weekdays[i] == weekday)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 锚点归一：返回规则在「≥ from」的第一个实际发生日（首响日）。
        /// 取不到返回 null（调用方保留原值或视为无效）。
        /// </summary>
        public static DateTime? FirstHitOnOrAfter(RecurrenceRule rule, DateTime from, int maxDays = DefaultSearchDays, DateTime? until = null)
        {
            if (rule == null || !rule.Enabled)
            {
                return from;
            }
            DateTime end = from.AddDays(maxDays);
            // 到期日上限：只在 [from, until] 内找首响日；超出 until 的命中一律不算，
            // 避免「下一个该响的日子落在到期日之后」时仍返回它（那会让锚点 > 到期日 → 幽灵日程）。
            if (until.HasValue && until.Value < end)
            {
                end = until.Value;
            }
            for (DateTime date = from; date <= end; date = date.AddDays(1))
            {
                if (RuleHit(rule, date))
                {
                    return date;
                }
            }
            return null;
        }

        /// <summary>
        /// 枚举某日程从 from 起的命中日期（惰性展开，不预生成实例），**按日期升序**。
        /// maxCount：最多返回多少条（提前终止）。用于避免「为了找最近 30 条而把未来几十年全算一遍」。
        /// 正确性：结果按升序，因此第 maxCount+1 条之后不可能进入「全局最早 N 条」，截断是安全的。
        /// </summary>
        public static List<DateTime> EnumerateOccurrences(Schedule schedule, DateTime from, int maxDays, int maxCount = int.MaxValue)
        {
            List<DateTime> result = new List<DateTime>();
            if (schedule == null)
            {
                return result;
            }

            if (!schedule.IsRecurring)
            {
                DateTime only = schedule.DateValue;
                if (only >= from)
                {
                    result.Add(only);
                }
                return result;
            }

            DateTime start = schedule.DateValue > from ? schedule.DateValue : from;
            DateTime? until = DateUtil.ParseDateNullable(schedule.Recurrence != null ? schedule.Recurrence.Until : null);
            DateTime end = start.AddDays(maxDays);
            if (until.HasValue && until.Value < end)
            {
                end = until.Value;
            }

            for (DateTime date = start; date <= end; date = date.AddDays(1))
            {
                if (IsHit(schedule, date))
                {
                    result.Add(date);
                    if (result.Count >= maxCount)
                    {
                        break;
                    }
                }
            }
            return result;
        }

        // ---------------- 摘要显示语法（P0-3 ⑥） ----------------

        private static readonly int[] WeekOrder = { 1, 2, 3, 4, 5, 6, 0 }; // 周一…周日

        private static readonly string[] WeekNames =
        {
            "周日", "周一", "周二", "周三", "周四", "周五", "周六"
        };

        /// <summary>重复列摘要：不重复 / 每年 / 每月 / 每周一、三、五 / 每月倒数第1天 / 每月最后周五 等。</summary>
        public static string Describe(Schedule schedule)
        {
            if (schedule == null || schedule.Recurrence == null || !schedule.Recurrence.Enabled)
            {
                return "不重复";
            }
            RecurrenceRule rule = schedule.Recurrence;
            List<string> parts = new List<string>();

            if (rule.Yearly != null && rule.Yearly.Month > 0 && rule.Yearly.Day > 0)
            {
                parts.Add("每年");
            }
            if (rule.Monthly != null && rule.Monthly.Day > 0)
            {
                parts.Add("每月");
            }
            if (rule.MonthlyLastN != null && rule.MonthlyLastN.N >= 1 && rule.MonthlyLastN.N <= 7)
            {
                parts.Add("每月倒数第" + rule.MonthlyLastN.N.ToString(CultureInfo.InvariantCulture) + "天");
            }
            if (rule.MonthlyLastWeekday != null
                && rule.MonthlyLastWeekday.Weekday >= 0
                && rule.MonthlyLastWeekday.Weekday <= 6)
            {
                parts.Add("每月最后" + WeekNames[rule.MonthlyLastWeekday.Weekday]);
            }
            string weekly = DescribeWeekly(rule.Weekly);
            if (!string.IsNullOrEmpty(weekly))
            {
                parts.Add(weekly);
            }

            if (parts.Count == 0)
            {
                return "不重复";
            }
            return string.Join("、", parts.ToArray());
        }

        /// <summary>
        /// 每周摘要：全选 → 每天；周一~周五 → 工作日；
        /// 每个最大连续段 ≥2 天 →「周X~周Y」（含环绕），单天 →「周X」，段间以「、」连接。
        /// 例：周一 + 周四~周六 →「周一、周四~周六」；周日~周二 + 周四 →「周四、周日~周二」。
        /// </summary>
        public static string DescribeWeekly(WeeklyRule weekly)
        {
            if (weekly == null || weekly.Weekdays == null || weekly.Weekdays.Count == 0)
            {
                return string.Empty;
            }

            bool[] picked = new bool[7];
            for (int i = 0; i < weekly.Weekdays.Count; i++)
            {
                int value = weekly.Weekdays[i];
                if (value >= 0 && value < 7)
                {
                    picked[value] = true;
                }
            }

            int count = 0;
            for (int i = 0; i < 7; i++)
            {
                if (picked[i])
                {
                    count++;
                }
            }
            if (count == 0)
            {
                return string.Empty;
            }
            if (count == 7)
            {
                return "每天";
            }
            if (picked[1] && picked[2] && picked[3] && picked[4] && picked[5] && count == 5)
            {
                return "工作日";
            }
            if (count == 2 && picked[0] && picked[6])
            {
                // 周日 + 周六：环绕连续区间，取「周六~周日」
                return "周六~周日";
            }

            // 单一原语驱动：分段结果总是完整且正确，因此**不需要**「检测失败就逐日列举」的兜底
            //（旧实现的 bug 正藏在那条兜底里：孤立日 + 连续段、多段组合会静默退化成逐日列举）
            List<WeekdayRun> runs = GetWeekdayRuns(picked);
            List<string> parts = new List<string>();
            for (int i = 0; i < runs.Count; i++)
            {
                WeekdayRun run = runs[i];
                if (run.Length >= 2)
                {
                    parts.Add(WeekNames[run.Start] + "~" + WeekNames[run.End]);
                }
                else
                {
                    parts.Add(WeekNames[run.Start]);
                }
            }
            return string.Join("、", parts.ToArray());
        }

        /// <summary>星期环上的一段「最大连续选中区间」（支持环绕，如「周日~周一」）。</summary>
        private sealed class WeekdayRun
        {
            /// <summary>段首：DayOfWeek 序号（0=周日 … 6=周六）。</summary>
            public int Start;

            /// <summary>段尾（环绕时 End 的序号会小于 Start，如周六~周日：Start=6、End=0）。</summary>
            public int End;

            /// <summary>段长（≥1）。</summary>
            public int Length;
        }

        /// <summary>
        /// 在「周日→周六」的自然环上把选中日分组为**最大连续段**（含环绕：周六+周日、周日+周一 都合并为一段），
        /// 段按 WeekOrder（周一→周日）的起点次序返回；全选（7 天）显式返回覆盖整周的单段。
        /// 这是摘要的唯一原语：结果永远完整正确，下游无需任何兜底分支。
        /// </summary>
        private static List<WeekdayRun> GetWeekdayRuns(bool[] picked)
        {
            List<WeekdayRun> runs = new List<WeekdayRun>();

            int count = 0;
            for (int i = 0; i < 7; i++)
            {
                if (picked[i])
                {
                    count++;
                }
            }
            if (count == 0)
            {
                return runs;
            }
            if (count == 7)
            {
                // 自包含：整周算一段（周一 → 周日，环绕表示）
                WeekdayRun full = new WeekdayRun();
                full.Start = 1;
                full.End = 0;
                full.Length = 7;
                runs.Add(full);
                return runs;
            }

            // 按展示次序（周一→周日）寻找每一段的段首：段首要求它在环上的前一位未被选中
            for (int order = 0; order < WeekOrder.Length; order++)
            {
                int start = WeekOrder[order];
                if (!picked[start])
                {
                    continue;
                }
                int previous = (start + 6) % 7;
                if (picked[previous])
                {
                    continue; // 前一位也选中 → 它不是段首
                }

                WeekdayRun run = new WeekdayRun();
                run.Start = start;
                run.End = start;
                run.Length = 1;
                int cursor = (start + 1) % 7;
                while (picked[cursor] && run.Length < 7)
                {
                    run.End = cursor;
                    run.Length++;
                    cursor = (cursor + 1) % 7;
                }
                runs.Add(run);
            }
            return runs;
        }
    }
}
