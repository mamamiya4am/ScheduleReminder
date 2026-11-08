using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ScheduleReminder.Models;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 农历 / 节气 / 节假日服务。
    /// 农历：BCL System.Globalization.ChineseLunisolarCalendar 真实计算（零依赖）。
    /// 节气：内置常量表（1900-2100，与主窗可翻阅年份、BCL 农历覆盖范围一致），取不到留空。
    /// 节假日：读 data\holidays.json，键为 **MM-dd（年复，一次写入每年生效）**（OPT-024）；
    /// 文件缺失或为空即不显示公历节日（不再重建默认文件，发布包须自带数据）。
    /// 浮动日（农历节日 = 农历推算，清明 = 节气推算）始终由算法推算。
    /// 显示优先级：节假日 &gt; 节气 &gt; 农历；农历只显示「日」。
    /// </summary>
    public class CalendarService
    {

        /// <summary>节假日表：键 MM-dd（年复），值节日名（OPT-024）。</summary>
        private readonly Dictionary<string, string> _holidays = new Dictionary<string, string>();

        /// <summary>
        /// 载入节假日数据（键 MM-dd）。
        /// 文件缺失 / 键非法 → 静默留空；文件被占用或损坏 → 弹提示并按空数据处理。
        /// 不再重建默认文件——公历节日由数据文件提供，程序不再内置公历节日兜底（OPT-024）。
        /// </summary>
        public void Load()
        {
            _holidays.Clear();

            try
            {
                // 必须用 TryLoadStringDictionary（启用 UseSimpleDictionaryFormat）：
                // 默认的 DataContractJsonSerializer 读不懂 {"MM-dd":"节日"} 这种手写字典格式。
                Dictionary<string, string> stored;
                Exception readError;
                if (!JsonStore.TryLoadStringDictionary(
                        AppPaths.HolidaysPath, out stored, out readError))
                {
                    if (JsonStore.IsTransientReadError(readError))
                    {
                        LogWriter.WriteError(
                            new DataError(DataErrorCode.LiteReadOccupied,
                                "数据被独占", "holidays.json 暂时无法读取"),
                            readError,
                            new ErrorContext
                            {
                                Operation = "Load",
                                FilePath = AppPaths.HolidaysPath,
                                Result = false
                            });
                        DialogService.Alert(
                            "节假日数据被独占",
                            "holidays.json 暂时无法读取，已按空数据处理。\r\n\r\n"
                            + "错误代码：" + DataErrorCode.LiteReadOccupied);
                    }
                    else
                    {
                        LogWriter.WriteError(
                            new DataError(DataErrorCode.LiteBackupUnavailable,
                                "节假日数据检测到损坏", "holidays.json 损坏且无备份"),
                            readError,
                            new ErrorContext
                            {
                                Operation = "Load",
                                FilePath = AppPaths.HolidaysPath,
                                Result = false
                            });
                        DialogService.Alert(
                            "节假日数据检测到损坏",
                            "holidays.json 数据检测到损坏，备份不可用，已恢复默认数据。\r\n\r\n"
                            + "错误代码：" + DataErrorCode.LiteBackupUnavailable);
                    }
                    stored = new Dictionary<string, string>();
                }

                if (stored != null)
                {
                    foreach (KeyValuePair<string, string> pair in stored)
                    {
                        string raw = pair.Key != null ? pair.Key.Trim() : null;
                        if (DateUtil.IsValidMonthDay(raw))
                        {
                            _holidays[raw] = pair.Value;
                        }
                        // 非法键静默跳过：绝不退化成「今天」，否则一条写错的数据会让今天凭空显示成节日
                    }
                }

                if (_holidays.Count == 0)
                {
                    LogWriter.Write("提示：data\\holidays.json 为空或缺失，公历节日须由该文件提供（发布包应自带）；"
                                    + "农历节日与清明由算法推算。");
                }
            }
            catch (Exception ex)
            {
                LogWriter.Write("载入节假日数据失败", ex);
            }
        }

        /// <summary>
        /// 节假日名（当天）。优先级：data\holidays.json（MM-dd）&gt; 算法推算（农历 / 节气 / 除夕）。
        /// 只标注节日当天，放假区间与调休一律不展示。
        /// </summary>
        public string GetHoliday(DateTime date)
        {
            string key = DateUtil.FormatMonthDay(date);
            string name;
            if (_holidays.TryGetValue(key, out name) && !string.IsNullOrEmpty(name))
            {
                return name;
            }
            // 兜底：离线推算（JSON 仍可覆盖或补充）
            return ComputeFestival(date);
        }

        /// <summary>离线推算节日当天。规则详见 Domain\FestivalCalculator.ComputeFestival。</summary>
        private static string ComputeFestival(DateTime date)
        {
            return FestivalCalculator.ComputeFestival(date);
        }

        /// <summary>节气名（内置常量表 1900–2100）；表外年份返回空字符串。</summary>
        public string GetSolarTerm(DateTime date)
        {
            return FestivalCalculator.GetSolarTerm(date);
        }

        /// <summary>农历「日」名（如「初六」）；超出 BCL 支持范围返回空字符串。</summary>
        public string GetLunarDayName(DateTime date)
        {
            return FestivalCalculator.GetLunarDayName(date);
        }
        /// <summary>
        /// 单元格右上角小字：节假日 &gt; 节气 &gt; 农历（仅日）。三者均与日期号同色，不做颜色区分。
        /// </summary>
        public string GetSubLabel(DateTime date)
        {
            string holiday = GetHoliday(date);
            if (!string.IsNullOrEmpty(holiday))
            {
                return holiday;
            }
            string term = GetSolarTerm(date);
            if (!string.IsNullOrEmpty(term))
            {
                return term;
            }
            return GetLunarDayName(date);
        }

        /// <summary>完整农历（如「九月初八」）；超出 BCL 支持范围返回空字符串。</summary>
        public string GetLunarFullName(DateTime date)
        {
            return FestivalCalculator.GetLunarFullName(date);
        }

        /// <summary>右侧面板副标题：节假日 &gt; 节气 &gt; 完整农历（不加上缀）。</summary>
        public string GetPanelSubLabel(DateTime date)
        {
            string holiday = GetHoliday(date);
            if (!string.IsNullOrEmpty(holiday))
            {
                return holiday;
            }
            string term = GetSolarTerm(date);
            if (!string.IsNullOrEmpty(term))
            {
                return term;
            }
            return GetLunarFullName(date);
        }

        /// <summary>编辑窗 / 预览窗标题栏后缀（「· 完整农历 / 节气 / 节日」），取不到则空。</summary>
        public string GetTitleSuffix(DateTime date)
        {
            string label = GetPanelSubLabel(date);
            if (string.IsNullOrEmpty(label))
            {
                return string.Empty;
            }
            return "· " + label;
        }

        /// <summary>中文月份名，如「9 月」。</summary>
        public static string MonthText(int month)
        {
            return month.ToString(CultureInfo.InvariantCulture) + " 月";
        }

        /// <summary>中文年份名，如「2026 年」。</summary>
        public static string YearText(int year)
        {
            return year.ToString(CultureInfo.InvariantCulture) + " 年";
        }
    }
}
