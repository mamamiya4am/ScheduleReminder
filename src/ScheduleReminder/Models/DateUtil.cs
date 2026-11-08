using System;
using System.Globalization;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Models
{
    /// <summary>
    /// 日期 / 时间 与字符串之间的转换工具。
    /// 落盘格式：日期 "yyyy-MM-dd"（零填充，保证字典序可排序），时间 "HH:mm"（24 小时制、零填充）。
    /// </summary>
    public static class DateUtil
    {
        public const string DateFormat = "yyyy-MM-dd";
        public const string TimeFormat = @"hh\:mm";

        /// <summary>
        /// 节假日的「年复键」格式 MM-dd（OPT-024）：政策性 / 自定义节日一次写入，每年生效。
        /// </summary>
        public const string MonthDayFormat = "MM-dd";

        /// <summary>
        /// 全程序唯一的「支持年份范围」单一事实源：与月历可翻阅范围、节气表覆盖范围、日程保存范围一致。
        /// 下限 1900 与 BCL 农历（1901 起）取齐；上限 2100 与 BCL 农历上限取齐。
        /// 任何涉及日期上/下限的判断都必须引用这里，不得在各窗口里另写死数字。
        /// </summary>
        public const int MinSupportedYear = 1900;
        public const int MaxSupportedYear = 2100;

        public static DateTime MinSupportedDate
        {
            get { return new DateTime(MinSupportedYear, 1, 1); }
        }

        public static DateTime MaxSupportedDate
        {
            get { return new DateTime(MaxSupportedYear, 12, 31); }
        }

        public static DateTime Today
        {
            get { return DateTime.Today; }
        }

        /// <summary>宽松解析：失败回落今天（历史行为，供容错读取路径使用）。</summary>
        public static DateTime ParseDate(string text)
        {
            DateTime value;
            return TryParseDateText(text, out value) ? value.Date : Today;
        }

        /// <summary>宽松解析：空 / 非法返回 null。</summary>
        public static DateTime? ParseDateNullable(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            DateTime value;
            return TryParseDateText(text, out value) ? value.Date : (DateTime?)null;
        }

        /// <summary>
        /// 强健的日期文本解析（与「时间」框的宽松策略对齐），按优先级依次尝试：
        /// ① 标准格式 yyyy-MM-dd（2026-10-07）
        /// ② 8 位紧凑数字 yyyyMMdd（20261007）
        /// ③ 常见分隔符 + 1~2 位月/日（2026/10/7、2026.10.7、2026-10-7）
        /// ④ 全角数字 / 全角分隔符与中文「年月日(号)」（２０２６年１０月７日、2026年10月7日）
        /// ⑤ 兜底：固定区域 + 当前区域的可读日期文本
        /// 全部失败 → false（由调用方决定回退值，不再隐藏失败）。
        /// </summary>
        public static bool TryParseDateText(string text, out DateTime value)
        {
            value = Today;
            if (text == null)
            {
                return false;
            }
            string s = NormalizeDateText(text);
            if (s.Length == 0)
            {
                return false;
            }
            // ① 标准格式
            if (DateTime.TryParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                return true;
            }
            // ② 8 位紧凑 yyyyMMdd
            if (s.Length == 8 && IsAllDigits(s)
                && DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                return true;
            }
            // ③ 分隔符切分：统一为 yyyy-M-d 再精确解析（"M" / "d" 兼容 1~2 位）
            string[] parts = s.Split(new char[] { '-', '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3)
            {
                string joined = parts[0] + "-" + parts[1] + "-" + parts[2];
                if (DateTime.TryParseExact(joined, "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
                {
                    return true;
                }
            }
            // ⑤ 兜底：固定区域优先，再试当前区域（两者语义不同，先后都试）
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                return true;
            }
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out value))
            {
                return true;
            }
            value = Today;
            return false;
        }

        private static bool IsAllDigits(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 归一化输入：全角数字 → 半角；全角符号 / 中文「年月日(号)」→ ASCII 分隔符；
        /// 制表符 / 全角空格 → 半角空格。供 TryParseDateText 统一预处理。
        /// </summary>
        private static string NormalizeDateText(string text)
        {
            char[] buffer = new char[text.Length];
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '０' && c <= '９') // 全角数字 U+FF10 ~ U+FF19
                {
                    buffer[count++] = (char)('0' + (c - '０'));
                    continue;
                }
                switch (c)
                {
                    case '－': // 全角减号
                    case '—':  // 破折号
                    case '–':  // 短破折号
                        buffer[count++] = '-';
                        continue;
                    case '／': // 全角斜杠
                        buffer[count++] = '/';
                        continue;
                    case '．': // 全角句点
                        buffer[count++] = '.';
                        continue;
                    case '年':
                    case '月':
                        buffer[count++] = '-';
                        continue;
                    case '日':
                    case '号':
                        continue;
                    case '　': // 全角空格
                    case '\t':
                        buffer[count++] = ' ';
                        continue;
                    default:
                        buffer[count++] = c;
                        continue;
                }
            }
            return new string(buffer, 0, count).Trim();
        }

        public static TimeSpan ParseTime(string text)
        {
            TimeSpan value;
            if (TimeSpan.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return new TimeSpan(9, 0, 0);
        }

        public static string FormatDate(DateTime value)
        {
            return value.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        public static string FormatTime(TimeSpan value)
        {
            return value.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }

        public static string FormatMonthDay(DateTime value)
        {
            return value.ToString(MonthDayFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 校验 "MM-dd"：形如两位-两位，月份 01–12，日 1..该月天数。
        /// 天数上限取闰年 2000（DaysInMonth(2000,2)=29）以放行 "02-29"；平年没有 2-29 自然不命中。
        /// </summary>
        public static bool IsValidMonthDay(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length != 5 || text[2] != '-')
            {
                return false;
            }
            for (int i = 0; i < text.Length; i++)
            {
                if (i == 2)
                {
                    continue;
                }
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }
            int month = int.Parse(text.Substring(0, 2), CultureInfo.InvariantCulture);
            int day = int.Parse(text.Substring(3, 2), CultureInfo.InvariantCulture);
            if (month < 1 || month > 12)
            {
                return false;
            }
            return day >= 1 && day <= DateTime.DaysInMonth(2000, month);
        }

        /// <summary>返回形如「周一」…「周日」的中文星期名。</summary>
        public static string WeekdayName(DateTime date)
        {
            switch (date.DayOfWeek)
            {
                case DayOfWeek.Monday: return "周一";
                case DayOfWeek.Tuesday: return "周二";
                case DayOfWeek.Wednesday: return "周三";
                case DayOfWeek.Thursday: return "周四";
                case DayOfWeek.Friday: return "周五";
                case DayOfWeek.Saturday: return "周六";
                default: return "周日";
            }
        }

        /// <summary>返回形如「2026 年 9 月 14 日（周一）」的日期标题。</summary>
        public static string FormatDateTitle(DateTime date)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} 年 {1} 月 {2} 日（{3}）",
                date.Year, date.Month, date.Day, WeekdayName(date));
        }
    }
}
