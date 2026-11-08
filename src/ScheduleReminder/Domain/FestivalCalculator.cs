using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScheduleReminder.Domain
{
    /// <summary>
    /// 农历 / 节气 / 节日的纯推算。
    ///
    /// 这里只回答一个问题：「给定日期，它是什么日子？」——
    /// 不读文件、不写日志、不碰界面，因此可以脱离程序单独推演与验证。
    /// 注：holidays.json 的加载留在 Services\CalendarService（那是数据，不是规则）。
    /// </summary>
    public static class FestivalCalculator
    {
        private static readonly System.Globalization.ChineseLunisolarCalendar LunarCalendar =
            new System.Globalization.ChineseLunisolarCalendar();

        /// <summary>农历节日：月 + 日 → 名称（闰月不计）。
        /// 注：固定公历节日自 OPT-024 起改由 data\holidays.json 提供，本类不再内置。</summary>
        private static readonly Dictionary<string, string> LunarFestivals = new Dictionary<string, string>
        {
            { "01-01", "春节" },   // 正月初一
            { "01-15", "元宵" },   // 正月十五
            { "02-02", "龙抬头" }, // 二月初二
            { "05-05", "端午" },   // 五月初五
            { "07-07", "七夕" },   // 七月初七
            { "07-15", "中元" },   // 七月十五
            { "08-15", "中秋" },   // 八月十五
            { "09-09", "重阳" },   // 九月初九
            { "12-08", "腊八" }    // 腊月初八
        };

        private static readonly string[] LunarDayNames =
        {
            "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
            "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
            "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
        };

        private static readonly string[] LunarMonthNames =
        {
            "正月", "二月", "三月", "四月", "五月", "六月",
            "七月", "八月", "九月", "十月", "冬月", "腊月"
        };

        /// <summary>
        /// 方案 C：离线推算「节日当天」——不联网、不依赖官方年度公布、零年度维护。
        /// 覆盖：农历节日（含除夕）与清明节气；固定公历节日由 data\holidays.json（MM-dd）提供（OPT-024）。
        /// 依据：清明节气 + BCL 农历（ChineseLunisolarCalendar）。
        /// </summary>
        public static string ComputeFestival(DateTime date)
        {
            // 1) 清明 = 清明节气当天（受节气表覆盖范围限制，表外年份无）
            if (SolarTermTable.GetSolarTerm(date) == "清明")
            {
                return "清明";
            }

            // 2) 农历节日（闰月不计）
            try
            {
                if (date.Year < 1901 || date.Year > 2100)
                {
                    return string.Empty;
                }
                int lunarYear = LunarCalendar.GetYear(date);
                int lunarMonth = LunarCalendar.GetMonth(date);
                int lunarDay = LunarCalendar.GetDayOfMonth(date);
                if (LunarCalendar.IsLeapMonth(lunarYear, lunarMonth))
                {
                    return string.Empty;
                }
                string lunarKey = lunarMonth.ToString("00", CultureInfo.InvariantCulture)
                                  + "-" + lunarDay.ToString("00", CultureInfo.InvariantCulture);
                string lunarName;
                if (LunarFestivals.TryGetValue(lunarKey, out lunarName))
                {
                    return lunarName;
                }

                // 除夕 = 腊月最后一天。用「明天是否为正月初一」判定：
                // 直接问当天是否腊月三十不可靠（腊月可能是廿九），且可绕开闰月（GetMonth 恒返回 1）。
                DateTime tomorrow = date.AddDays(1);
                if (tomorrow.Year >= 1901 && tomorrow.Year <= 2100)
                {
                    if (LunarCalendar.GetMonth(tomorrow) == 1 && LunarCalendar.GetDayOfMonth(tomorrow) == 1)
                    {
                        return "除夕";
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return string.Empty;
            }
            return string.Empty;
        }

        /// <summary>节气名（内置常量表 1900–2100）；非节气日或表外年份返回空字符串。</summary>
        public static string GetSolarTerm(DateTime date)
        {
            return SolarTermTable.GetSolarTerm(date);
        }

        /// <summary>农历「日」名（如「初六」）；超出 BCL 支持范围（1901–2100）返回空字符串。</summary>
        public static string GetLunarDayName(DateTime date)
        {
            try
            {
                if (date.Year < 1901 || date.Year > 2100)
                {
                    return string.Empty;
                }
                int day = LunarCalendar.GetDayOfMonth(date);
                if (day < 1 || day > 30)
                {
                    return string.Empty;
                }
                return LunarDayNames[day - 1];
            }
            catch (ArgumentOutOfRangeException)
            {
                return string.Empty;
            }
            catch (Exception)
            {
                // Domain 层不写日志（保持纯净），交由调用方按需记录
                return string.Empty;
            }
        }

        /// <summary>完整农历月日（如「九月初八」「闰六月初八」）；超出 BCL 支持范围返回空字符串。</summary>
        public static string GetLunarFullName(DateTime date)
        {
            try
            {
                if (date.Year < 1901 || date.Year > 2100)
                {
                    return string.Empty;
                }
                int lunarYear = LunarCalendar.GetYear(date);
                int lunarMonthRaw = LunarCalendar.GetMonth(date);
                int lunarDay = LunarCalendar.GetDayOfMonth(date);
                if (lunarDay < 1 || lunarDay > 30)
                {
                    return string.Empty;
                }

                int leapMonth = LunarCalendar.GetLeapMonth(lunarYear);
                bool isLeap = LunarCalendar.IsLeapMonth(lunarYear, lunarMonthRaw);
                int lunarMonth = lunarMonthRaw;
                if (leapMonth > 0 && lunarMonthRaw >= leapMonth)
                {
                    lunarMonth--;
                }
                if (lunarMonth < 1 || lunarMonth > 12)
                {
                    return string.Empty;
                }

                return (isLeap ? "闰" : string.Empty)
                       + LunarMonthNames[lunarMonth - 1]
                       + LunarDayNames[lunarDay - 1];
            }
            catch (ArgumentOutOfRangeException)
            {
                return string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
