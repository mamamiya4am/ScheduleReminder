using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScheduleReminder.Tools.WorldClock
{
    /// <summary>内置城市：名称即唯一标识（三十九城无重名），国家名为显示用中文，时区标识用 IANA 写法。</summary>
    public sealed class CityDefinition
    {
        /// <summary>城市名，同时作为持久化标识（落盘只存这一项）。</summary>
        public string Name { get; private set; }

        /// <summary>国家名（中文），仅用于显示，不落盘。</summary>
        public string Country { get; private set; }

        /// <summary>互联网号码分配局（IANA）时区标识，如 Asia/Shanghai。</summary>
        public string IanaId { get; private set; }

        public static CityDefinition Create(string name, string country, string ianaId)
        {
            return new CityDefinition { Name = name, Country = country, IanaId = ianaId };
        }
    }

    /// <summary>
    /// 城市库与时区解析。.NET Framework 4.8 的 TimeZoneInfo.FindSystemTimeZoneById 只认 Windows 时区标识，
    /// 因此内置 IANA → Windows 标识映射表（项目零 NuGet 依赖，不引外部时区转换库）。
    /// 城市名即持久化标识；程序端只存名称，时区偏移与国家名每次由本库与系统时区规则实时计算。
    /// </summary>
    public static class WorldClockCatalog
    {
        private static readonly List<CityDefinition> CitiesBacking = Build();
        private static readonly Dictionary<string, string> WindowsIdByIana = BuildMapping();
        private static readonly Dictionary<string, TimeZoneInfo> Cache = new Dictionary<string, TimeZoneInfo>();
        private static readonly Dictionary<string, TimeZoneInfo> SystemZones = BuildSystemZones();

        /// <summary>内置 39 城，顺序即下拉顺序。</summary>
        public static IList<CityDefinition> Cities
        {
            get { return CitiesBacking; }
        }

        /// <summary>首次打开预置的四座城市。</summary>
        public static IList<string> DefaultCityNames
        {
            get { return new List<string> { "北京", "东京", "伦敦", "纽约" }; }
        }

        /// <summary>按城市名查找；找不到返回 null（调用方负责过滤掉库里已不存在的旧数据）。</summary>
        public static CityDefinition FindByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            for (int i = 0; i < CitiesBacking.Count; i++)
            {
                if (CitiesBacking[i].Name == name)
                {
                    return CitiesBacking[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 取城市对应的时区：先经映射表换成 Windows 标识；映射缺失或系统不认时返回 null，
        /// 由窗口明确显示“时区不可用”，绝不伪装成本地时间，也绝不抛异常。
        /// </summary>
        public static TimeZoneInfo Resolve(CityDefinition city)
        {
            if (city == null)
            {
                return null;
            }

            TimeZoneInfo cached;
            if (Cache.TryGetValue(city.IanaId, out cached))
            {
                return cached;
            }

            // .NET Framework + Windows 只认 Windows 时区 ID。
            // 必须先映射成 "China Standard Time" 再查；直接拿 "Asia/Shanghai" 去查
            // 会抛 TimeZoneNotFoundException（调试器会停在 first-chance 异常处）。
            TimeZoneInfo zone = null;
            string windowsId;
            if (WindowsIdByIana.TryGetValue(city.IanaId, out windowsId))
            {
                zone = TryFind(windowsId);
            }

            // 非 Windows 平台或映射缺失时，才把它当作系统原生 ID 再查一次。
            if (zone == null)
            {
                zone = TryFind(city.IanaId);
            }

            // 解析失败不能伪装成本地时间：返回 null，由 UI 明确显示“时区不可用”。
            // 失败结果不写 Cache，系统时区恢复后下一分钟可自动恢复。
            if (zone == null)
            {
                return null;
            }

            Cache[city.IanaId] = zone;
            return zone;
        }

        /// <summary>当地时刻文本，显示到分（24 小时制，如 22:45）。</summary>
        public static string FormatTime(TimeZoneInfo zone, DateTime utcNow)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
            return local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>当地日期文本，格式「10月20日 · 周二」。</summary>
        public static string FormatDate(TimeZoneInfo zone, DateTime utcNow)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
            return string.Format(CultureInfo.InvariantCulture, "{0}月{1}日 · 周{2}",
                local.Month, local.Day, WeekdayText(local.DayOfWeek));
        }

        /// <summary>
        /// 时差文本：目标城市 UTC 偏移减本地 UTC 偏移，最多一位小数并四舍五入、整数不带小数点，
        /// 零值显「+0 小时」，不写「与本地相同」。
        /// </summary>
        public static string FormatDifference(TimeZoneInfo zone, DateTime utcNow)
        {
            double hours = (zone.GetUtcOffset(utcNow) - TimeZoneInfo.Local.GetUtcOffset(utcNow)).TotalHours;
            if (Math.Abs(hours) < 0.01)
            {
                return "+0 小时";
            }
            double rounded = Math.Round(Math.Abs(hours) * 10.0) / 10.0;
            string sign = hours > 0 ? "+" : "−";
            return sign + rounded.ToString("0.#", CultureInfo.InvariantCulture) + " 小时";
        }

        private static string WeekdayText(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Sunday:
                    return "日";
                case DayOfWeek.Monday:
                    return "一";
                case DayOfWeek.Tuesday:
                    return "二";
                case DayOfWeek.Wednesday:
                    return "三";
                case DayOfWeek.Thursday:
                    return "四";
                case DayOfWeek.Friday:
                    return "五";
                default:
                    return "六";
            }
        }

        /// <summary>
        /// 系统时区表只构建一次。用字典查找而不是 FindSystemTimeZoneById：
        /// 后者在找不到 ID 时会抛 first-chance 异常，调试器可能把它当成崩溃。
        /// </summary>
        private static Dictionary<string, TimeZoneInfo> BuildSystemZones()
        {
            Dictionary<string, TimeZoneInfo> map =
                new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);
            try
            {
                IList<TimeZoneInfo> zones = TimeZoneInfo.GetSystemTimeZones();
                for (int i = 0; i < zones.Count; i++)
                {
                    TimeZoneInfo zone = zones[i];
                    if (zone != null && !string.IsNullOrEmpty(zone.Id) && !map.ContainsKey(zone.Id))
                    {
                        map.Add(zone.Id, zone);
                    }
                }
            }
            catch (Exception)
            {
                // 系统时区表异常时留空，Resolve 返回 null，由窗口显示“时区不可用”。
            }
            return map;
        }

        private static TimeZoneInfo TryFind(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            TimeZoneInfo zone;
            return SystemZones.TryGetValue(id, out zone) ? zone : null;
        }

        private static Dictionary<string, string> BuildMapping()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();

            map["Asia/Shanghai"] = "China Standard Time";
            map["Asia/Hong_Kong"] = "China Standard Time";
            map["Asia/Taipei"] = "Taipei Standard Time";
            map["Asia/Tokyo"] = "Tokyo Standard Time";
            map["Asia/Seoul"] = "Korea Standard Time";
            map["Asia/Singapore"] = "Singapore Standard Time";
            map["Asia/Bangkok"] = "SE Asia Standard Time";
            map["Asia/Jakarta"] = "SE Asia Standard Time";
            map["Asia/Kolkata"] = "India Standard Time";
            map["Asia/Dubai"] = "Arabian Standard Time";
            map["Europe/Moscow"] = "Russian Standard Time";
            map["Europe/Istanbul"] = "Turkey Standard Time";
            map["Europe/Athens"] = "GTB Standard Time";
            map["Europe/Berlin"] = "W. Europe Standard Time";
            map["Europe/Paris"] = "W. Europe Standard Time";
            map["Europe/Rome"] = "W. Europe Standard Time";
            map["Europe/Amsterdam"] = "W. Europe Standard Time";
            map["Europe/Zurich"] = "W. Europe Standard Time";
            map["Europe/Stockholm"] = "W. Europe Standard Time";
            map["Europe/London"] = "GMT Standard Time";
            map["Europe/Dublin"] = "GMT Standard Time";
            map["Europe/Lisbon"] = "GMT Standard Time";
            map["Africa/Cairo"] = "Egypt Standard Time";
            map["Africa/Nairobi"] = "E. Africa Standard Time";
            map["Africa/Johannesburg"] = "South Africa Standard Time";
            map["America/New_York"] = "Eastern Standard Time";
            map["America/Toronto"] = "Eastern Standard Time";
            map["America/Chicago"] = "Central Standard Time";
            map["America/Denver"] = "Mountain Standard Time";
            map["America/Los_Angeles"] = "Pacific Standard Time";
            map["America/Vancouver"] = "Pacific Standard Time";
            map["America/Mexico_City"] = "Central Standard Time (Mexico)";
            map["America/Sao_Paulo"] = "E. South America Standard Time";
            map["America/Argentina/Buenos_Aires"] = "Argentina Standard Time";
            map["Australia/Sydney"] = "AUS Eastern Standard Time";
            map["Australia/Melbourne"] = "AUS Eastern Standard Time";
            map["Australia/Perth"] = "W. Australia Standard Time";
            map["Pacific/Auckland"] = "New Zealand Standard Time";
            map["Pacific/Honolulu"] = "Hawaiian Standard Time";

            return map;
        }

        private static List<CityDefinition> Build()
        {
            List<CityDefinition> list = new List<CityDefinition>();

            list.Add(CityDefinition.Create("北京", "中国", "Asia/Shanghai"));
            list.Add(CityDefinition.Create("香港", "中国", "Asia/Hong_Kong"));
            list.Add(CityDefinition.Create("台北", "中国", "Asia/Taipei"));
            list.Add(CityDefinition.Create("东京", "日本", "Asia/Tokyo"));
            list.Add(CityDefinition.Create("首尔", "韩国", "Asia/Seoul"));
            list.Add(CityDefinition.Create("新加坡", "新加坡", "Asia/Singapore"));
            list.Add(CityDefinition.Create("曼谷", "泰国", "Asia/Bangkok"));
            list.Add(CityDefinition.Create("雅加达", "印度尼西亚", "Asia/Jakarta"));
            list.Add(CityDefinition.Create("新德里", "印度", "Asia/Kolkata"));
            list.Add(CityDefinition.Create("迪拜", "阿联酋", "Asia/Dubai"));
            list.Add(CityDefinition.Create("莫斯科", "俄罗斯", "Europe/Moscow"));
            list.Add(CityDefinition.Create("伊斯坦布尔", "土耳其", "Europe/Istanbul"));
            list.Add(CityDefinition.Create("雅典", "希腊", "Europe/Athens"));
            list.Add(CityDefinition.Create("柏林", "德国", "Europe/Berlin"));
            list.Add(CityDefinition.Create("巴黎", "法国", "Europe/Paris"));
            list.Add(CityDefinition.Create("罗马", "意大利", "Europe/Rome"));
            list.Add(CityDefinition.Create("阿姆斯特丹", "荷兰", "Europe/Amsterdam"));
            list.Add(CityDefinition.Create("苏黎世", "瑞士", "Europe/Zurich"));
            list.Add(CityDefinition.Create("斯德哥尔摩", "瑞典", "Europe/Stockholm"));
            list.Add(CityDefinition.Create("伦敦", "英国", "Europe/London"));
            list.Add(CityDefinition.Create("都柏林", "爱尔兰", "Europe/Dublin"));
            list.Add(CityDefinition.Create("里斯本", "葡萄牙", "Europe/Lisbon"));
            list.Add(CityDefinition.Create("开罗", "埃及", "Africa/Cairo"));
            list.Add(CityDefinition.Create("内罗毕", "肯尼亚", "Africa/Nairobi"));
            list.Add(CityDefinition.Create("约翰内斯堡", "南非", "Africa/Johannesburg"));
            list.Add(CityDefinition.Create("纽约", "美国", "America/New_York"));
            list.Add(CityDefinition.Create("多伦多", "加拿大", "America/Toronto"));
            list.Add(CityDefinition.Create("芝加哥", "美国", "America/Chicago"));
            list.Add(CityDefinition.Create("丹佛", "美国", "America/Denver"));
            list.Add(CityDefinition.Create("洛杉矶", "美国", "America/Los_Angeles"));
            list.Add(CityDefinition.Create("温哥华", "加拿大", "America/Vancouver"));
            list.Add(CityDefinition.Create("墨西哥城", "墨西哥", "America/Mexico_City"));
            list.Add(CityDefinition.Create("圣保罗", "巴西", "America/Sao_Paulo"));
            list.Add(CityDefinition.Create("布宜诺斯艾利斯", "阿根廷", "America/Argentina/Buenos_Aires"));
            list.Add(CityDefinition.Create("悉尼", "澳大利亚", "Australia/Sydney"));
            list.Add(CityDefinition.Create("墨尔本", "澳大利亚", "Australia/Melbourne"));
            list.Add(CityDefinition.Create("珀斯", "澳大利亚", "Australia/Perth"));
            list.Add(CityDefinition.Create("奥克兰", "新西兰", "Pacific/Auckland"));
            list.Add(CityDefinition.Create("檀香山", "美国", "Pacific/Honolulu"));

            return list;
        }
    }
}
