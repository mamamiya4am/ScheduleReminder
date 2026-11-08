using System;
using System.Collections.Generic;
using ScheduleReminder.Domain;
using ScheduleReminder.Models;

namespace ScheduleReminder.Services
{
    /// <summary>AI 与手动路径共用的日程字段校验。</summary>
    public static class ScheduleValidator
    {
        public static string Validate(Schedule draft, DateTime minStart, bool requireTodayOrLater)
        {
            if (draft == null)
            {
                return "缺必填";
            }
            string title = draft.Title != null ? draft.Title.Trim() : string.Empty;
            if (title.Length == 0)
            {
                return "缺必填";
            }
            if (title.Length > 12)
            {
                return "标题需为 1–12 个字";
            }
            if (draft.Note != null && draft.Note.Length > 500)
            {
                return "备注不能超过 500 个字";
            }

            if (string.IsNullOrEmpty(draft.Date) || string.IsNullOrEmpty(draft.Time))
            {
                return "缺必填";
            }
            DateTime date;
            if (!DateUtil.TryParseDateText(draft.Date, out date))
            {
                return "日期格式应为：YYYY-MM-DD";
            }
            if (date.Year < DateUtil.MinSupportedYear
                || date.Year > DateUtil.MaxSupportedYear)
            {
                return "日期超出支持范围";
            }
            if (requireTodayOrLater && date.Date < DateTime.Today)
            {
                return "日期不能早于今天";
            }

            TimeSpan time;
            if (!ScheduleService.TryParseTimeText(draft.Time, out time))
            {
                return "时间格式应为 HH:MM（24 小时制）";
            }
            if (draft.AdvanceMinutes < 0 || draft.AdvanceMinutes > 60)
            {
                return "提前提醒需为 0–60 分钟";
            }
            if (!IsValidColor(draft.BgColor) || !IsValidColor(draft.FontColor))
            {
                return "颜色格式错误";
            }
            if (draft.Recurrence != null && draft.Recurrence.Enabled
                && (!HasAnyRecurrenceRule(draft.Recurrence)
                    || !IsRecurrenceValid(draft.Recurrence)))
            {
                return "重复规则不完整";
            }
            if (draft.Recurrence != null && draft.Recurrence.Enabled
                && !string.IsNullOrEmpty(draft.Recurrence.Until))
            {
                DateTime until;
                if (!DateUtil.TryParseDateText(draft.Recurrence.Until, out until))
                {
                    return "截止日期格式应为：YYYY-MM-DD";
                }
                if (until.Date < minStart.Date)
                {
                    return "截止日期不能早于起始日期";
                }
            }
            return null;
        }

        public static bool IsValidColor(string color)
        {
            if (string.IsNullOrEmpty(color))
            {
                return true;
            }
            string value = color[0] == '#' ? color.Substring(1) : color;
            if (value.Length != 6)
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsValidSound(string preset)
        {
            if (string.IsNullOrEmpty(preset) || preset == SoundLibrary.NoneOption)
            {
                return true;
            }
            List<string> names = SoundLibrary.ListNames();
            return names.Contains(preset);
        }

        private static bool IsRecurrenceValid(RecurrenceRule rule)
        {
            if (rule.Weekly != null && rule.Weekly.Weekdays != null)
            {
                for (int i = 0; i < rule.Weekly.Weekdays.Count; i++)
                {
                    if (rule.Weekly.Weekdays[i] < 0 || rule.Weekly.Weekdays[i] > 6)
                    {
                        return false;
                    }
                }
            }
            if (rule.Monthly != null && rule.Monthly.Day != 0
                && (rule.Monthly.Day < 1 || rule.Monthly.Day > 31))
            {
                return false;
            }
            if (rule.MonthlyLastN != null && rule.MonthlyLastN.N != 0
                && (rule.MonthlyLastN.N < 1 || rule.MonthlyLastN.N > 7))
            {
                return false;
            }
            if (rule.MonthlyLastWeekday != null && rule.MonthlyLastWeekday.Weekday != 0
                && (rule.MonthlyLastWeekday.Weekday < 0 || rule.MonthlyLastWeekday.Weekday > 6))
            {
                return false;
            }
            if (rule.Yearly != null && rule.Yearly.Month != 0
                && (rule.Yearly.Month < 1 || rule.Yearly.Month > 12
                    || rule.Yearly.Day < 1 || rule.Yearly.Day > 31))
            {
                return false;
            }
            return true;
        }

        private static bool HasAnyRecurrenceRule(RecurrenceRule rule)
        {
            if (rule.Weekly != null && rule.Weekly.Weekdays != null
                && rule.Weekly.Weekdays.Count > 0)
            {
                return true;
            }
            if (rule.Monthly != null && rule.Monthly.Day > 0)
            {
                return true;
            }
            if (rule.MonthlyLastN != null && rule.MonthlyLastN.N > 0)
            {
                return true;
            }
            if (rule.MonthlyLastWeekday != null && rule.MonthlyLastWeekday.Weekday >= 0)
            {
                return true;
            }
            return rule.Yearly != null && rule.Yearly.Month > 0;
        }
    }

    public static class TagValidator
    {
        public static string ValidateName(string name)
        {
            string trimmed = name != null ? name.Trim() : string.Empty;
            if (trimmed.Length == 0 || trimmed.Length > 6)
            {
                return "标签名需为 1–6 个字";
            }
            return null;
        }
    }
}
