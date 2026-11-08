using System;
using System.Globalization;
using ScheduleReminder.Models;

namespace ScheduleReminder.Domain
{
    /// <summary>
    /// 纯领域规则层。
    /// 这里只有「日程什么时候算过期、时间文本怎么解析、边界秒怎么判定」这类与界面无关的规则，
    /// 不引用任何 WPF / 窗口 / 磁盘 / 网络的东西，可以脱离界面单独推演与验证。
    /// </summary>
    public static class ScheduleRules
    {
        // ---------------- 过期 / 生效判定 ----------------

        /// <summary>
        /// 已过期判定：一次性 = date&lt;今日 ∨ (date==今日 ∧ time≤now)；
        /// 重复段 = until≠null ∧ (until&lt;今日 ∨ (until==今日 ∧ time≤now))。
        /// </summary>
        public static bool IsExpired(Schedule schedule, DateTime now)
        {
            if (schedule == null)
            {
                return false;
            }
            DateTime today = now.Date;
            if (schedule.IsRecurring)
            {
                DateTime? until = DateUtil.ParseDateNullable(schedule.Recurrence != null ? schedule.Recurrence.Until : null);
                if (!until.HasValue)
                {
                    return false;
                }
                if (until.Value < today)
                {
                    return true;
                }
                if (until.Value == today && schedule.TimeValue <= now.TimeOfDay)
                {
                    return true;
                }
                return false;
            }
            if (schedule.DateValue < today)
            {
                return true;
            }
            if (schedule.DateValue == today && schedule.TimeValue <= now.TimeOfDay)
            {
                return true;
            }
            return false;
        }

        /// <summary>该日程是否已经生效过一次（从未生效 = date&gt;今日 ∨ (date==今日 ∧ time&gt;now)）。</summary>
        public static bool HasTakenEffect(Schedule schedule, DateTime now)
        {
            if (schedule == null)
            {
                return false;
            }
            DateTime today = now.Date;
            if (schedule.DateValue > today)
            {
                return false;
            }
            if (schedule.DateValue == today && schedule.TimeValue > now.TimeOfDay)
            {
                return false;
            }
            return true;
        }

        // ---------------- 输入校验 ----------------

        /// <summary>时间文本校验：宽松解析。支持 9:00 / 09:00 / 9 / 900 / 0900 等（中文冒号、空格亦兼容）。</summary>
        public static bool TryParseTimeText(string text, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            // 兼容中文冒号与多余空格：9:00 / 9 / 900 / 0900 等都能接受。
            string normalized = text.Trim().Replace('：', ':').Replace(" ", string.Empty);
            // 含冒号：优先用宽松 TimeSpan.TryParse（自动处理 9:00 / 9:5 / 09:00 等）。
            if (normalized.IndexOf(':') >= 0)
            {
                if (TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out value)
                    && value.TotalHours >= 0 && value.TotalHours < 24)
                {
                    return true;
                }
            }
            // 无冒号或上面失败：尝试紧凑格式（9 → 9:00，900 → 9:00，0900 → 09:00）。
            string[] compact = { @"hmm", @"hhmm", @"h", @"hh" };
            if (TimeSpan.TryParseExact(normalized, compact, CultureInfo.InvariantCulture, TimeSpanStyles.None, out value)
                && value.TotalHours >= 0 && value.TotalHours < 24)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 提前提醒文本校验（N 分钟）：非数字 → false；小数四舍五入；&gt;60 取 60；&lt;0 取 0。
        /// </summary>
        public static bool TryParseAdvanceText(string text, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string normalized = text.Trim().Replace("分钟", string.Empty).Replace("分", string.Empty).Trim();
            // 界面用“-”表示 0 分钟；编辑时允许把“-”重新解析为 0
            if (normalized == "-" || normalized == "—")
            {
                minutes = 0;
                return true;
            }
            double value;
            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return false;
            }
            minutes = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            if (minutes < 0)
            {
                minutes = 0;
            }
            if (minutes > 60)
            {
                minutes = 60;
            }
            return true;
        }

        /// <summary>
        /// 边界秒规则：提醒时刻（正点时间 − 提前提醒时间）必须比当前时间至少晚 1 分钟；
        /// 早于今天的日期不校验，保存时会被重新锚定到今天之后。
        /// </summary>
        public static bool CheckBoundary(DateTime date, TimeSpan time, int advanceMinutes)
        {
            if (date < DateTime.Today)
            {
                return true;
            }
            DateTime now = DateTime.Now;
            DateTime fire = date.Date.Add(time).AddMinutes(-advanceMinutes);
            return fire > now.AddMinutes(1);
        }
    }
}
