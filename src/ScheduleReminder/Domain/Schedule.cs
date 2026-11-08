using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using ScheduleReminder.Models;   // DateUtil 在 Models 命名空间，需显式引用

namespace ScheduleReminder.Domain
{
    /// <summary>
    /// 每条日程的音效配置。
    /// preset = 音效文件名（data\sound\ 下的文件）；null / 缺省 = 静音（只弹窗、不响）。
    /// 不再使用 "default" / "custom" / "-" 哨兵。
    /// </summary>
    [DataContract]
    public class ScheduleSound
    {
        [DataMember(Name = "preset", EmitDefaultValue = false)]
        public string Preset { get; set; }
    }

    /// <summary>每年重复：month + day。</summary>
    [DataContract]
    public class YearlyRule
    {
        [DataMember(Name = "month", EmitDefaultValue = false)]
        public int Month { get; set; }

        [DataMember(Name = "day", EmitDefaultValue = false)]
        public int Day { get; set; }
    }

    /// <summary>每月同一天重复。</summary>
    [DataContract]
    public class MonthlyRule
    {
        [DataMember(Name = "day", EmitDefaultValue = false)]
        public int Day { get; set; }
    }

    /// <summary>每月倒数第 N 天重复（N = 1..7）。</summary>
    [DataContract]
    public class MonthlyLastNRule
    {
        [DataMember(Name = "n", EmitDefaultValue = false)]
        public int N { get; set; }
    }

    /// <summary>每月最后一个周 X 重复（0 = 周日, 1 = 周一 ... 6 = 周六）。</summary>
    [DataContract]
    public class MonthlyLastWeekdayRule
    {
        [DataMember(Name = "weekday", EmitDefaultValue = false)]
        public int Weekday { get; set; }
    }

    /// <summary>每周重复（0 = 周日, 1 = 周一 ... 6 = 周六）。</summary>
    [DataContract]
    public class WeeklyRule
    {
        [DataMember(Name = "weekdays", EmitDefaultValue = false)]
        public List<int> Weekdays { get; set; }
    }

    /// <summary>
    /// 重复规则。生效窗口为闭区间 [顶层 date, until]；until 为空 = 永久。
    /// 一次性日程的唯一判定标准：enabled == false（此时其余子字段一律为默认值 / 空值）。
    /// </summary>
    [DataContract]
    public class RecurrenceRule
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "until", EmitDefaultValue = false)]
        public string Until { get; set; }

        [DataMember(Name = "yearly", EmitDefaultValue = false)]
        public YearlyRule Yearly { get; set; }

        [DataMember(Name = "monthly", EmitDefaultValue = false)]
        public MonthlyRule Monthly { get; set; }

        [DataMember(Name = "monthlyLastN", EmitDefaultValue = false)]
        public MonthlyLastNRule MonthlyLastN { get; set; }

        [DataMember(Name = "monthlyLastWeekday", EmitDefaultValue = false)]
        public MonthlyLastWeekdayRule MonthlyLastWeekday { get; set; }

        [DataMember(Name = "weekly", EmitDefaultValue = false)]
        public WeeklyRule Weekly { get; set; }

        public RecurrenceRule()
        {
            Enabled = false;
            Until = null;
            Yearly = null;
            Monthly = null;
            MonthlyLastN = null;
            MonthlyLastWeekday = null;
            Weekly = new WeeklyRule();
            Weekly.Weekdays = new List<int>();
        }

        /// <summary>深拷贝（分段时新建未来段用）。</summary>
        public RecurrenceRule Clone()
        {
            RecurrenceRule copy = new RecurrenceRule();
            copy.Enabled = Enabled;
            copy.Until = Until;
            if (Yearly != null)
            {
                copy.Yearly = new YearlyRule();
                copy.Yearly.Month = Yearly.Month;
                copy.Yearly.Day = Yearly.Day;
            }
            if (Monthly != null)
            {
                copy.Monthly = new MonthlyRule();
                copy.Monthly.Day = Monthly.Day;
            }
            if (MonthlyLastN != null)
            {
                copy.MonthlyLastN = new MonthlyLastNRule();
                copy.MonthlyLastN.N = MonthlyLastN.N;
            }
            if (MonthlyLastWeekday != null)
            {
                copy.MonthlyLastWeekday = new MonthlyLastWeekdayRule();
                copy.MonthlyLastWeekday.Weekday = MonthlyLastWeekday.Weekday;
            }
            copy.Weekly = new WeeklyRule();
            copy.Weekly.Weekdays = new List<int>();
            if (Weekly != null && Weekly.Weekdays != null)
            {
                copy.Weekly.Weekdays.AddRange(Weekly.Weekdays);
            }
            return copy;
        }

        /// <summary>五类规则是否一条都没选（仅设「到期关闭」不算已选）。</summary>
        public bool HasAnyRule()
        {
            if (Yearly != null && Yearly.Month > 0 && Yearly.Day > 0)
            {
                return true;
            }
            if (Monthly != null && Monthly.Day > 0)
            {
                return true;
            }
            if (MonthlyLastN != null && MonthlyLastN.N >= 1 && MonthlyLastN.N <= 7)
            {
                return true;
            }
            if (MonthlyLastWeekday != null && MonthlyLastWeekday.Weekday >= 0 && MonthlyLastWeekday.Weekday <= 6
                && MonthlyLastWeekday.Weekday != -1)
            {
                return true;
            }
            if (Weekly != null && Weekly.Weekdays != null && Weekly.Weekdays.Count > 0)
            {
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 日程主记录（单母项 + 渲染期惰性展开；不预生成实例实体）。
    /// </summary>
    [DataContract]
    public class Schedule
    {
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public string Id { get; set; }

        [DataMember(Name = "seriesId", EmitDefaultValue = false)]
        public string SeriesId { get; set; }

        [DataMember(Name = "remindEnabled")]
        public bool RemindEnabled { get; set; }

        /// <summary>是否待办：只决定是否进入右侧待办面板 / 月格角标。</summary>
        [DataMember(Name = "isTodo", EmitDefaultValue = false)]
        public bool IsTodo { get; set; }

        /// <summary>已完成的发生日（yyyy-MM-dd）；一次性最多一条，重复可多条。不记完成时间。</summary>
        [DataMember(Name = "doneDates", EmitDefaultValue = false)]
        public List<string> DoneDates { get; set; }

        [DataMember(Name = "date")]
        public string Date { get; set; }

        [DataMember(Name = "time")]
        public string Time { get; set; }

        [DataMember(Name = "title", EmitDefaultValue = false)]
        public string Title { get; set; }

        [DataMember(Name = "note", EmitDefaultValue = false)]
        public string Note { get; set; }

        [DataMember(Name = "tag", EmitDefaultValue = false)]
        public string Tag { get; set; }

        /// <summary>标签模板 ID；null = 无标签。日程自身仍保存标签名/颜色/铃声快照。</summary>
        [DataMember(Name = "tagId", EmitDefaultValue = false)]
        public string TagId { get; set; }

        [DataMember(Name = "advanceMinutes")]
        public int AdvanceMinutes { get; set; }

        [DataMember(Name = "bgColor", EmitDefaultValue = false)]
        public string BgColor { get; set; }

        [DataMember(Name = "fontColor", EmitDefaultValue = false)]
        public string FontColor { get; set; }

        [DataMember(Name = "sound", EmitDefaultValue = false)]
        public ScheduleSound Sound { get; set; }

        [DataMember(Name = "recurrence", EmitDefaultValue = false)]
        public RecurrenceRule Recurrence { get; set; }

        /// <summary>
        /// DataContractJsonSerializer 反序列化时不调用构造函数，因此用 OnDeserializing 补默认值。
        /// remindEnabled 落盘缺省为 true。
        /// </summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            RemindEnabled = true;
            DoneDates = new List<string>();
        }

        public Schedule()
        {
            RemindEnabled = true;
            Recurrence = new RecurrenceRule();
            Sound = new ScheduleSound();
            DoneDates = new List<string>();
            AdvanceMinutes = 0;
        }

        // ---------- 便捷读取（不参与序列化） ----------

        public DateTime DateValue
        {
            get { return DateUtil.ParseDate(Date); }
        }

        public TimeSpan TimeValue
        {
            get { return DateUtil.ParseTime(Time); }
        }

        /// <summary>某次命中日的闹钟时刻（正点）。</summary>
        public DateTime GetAlarmTime(DateTime occurrenceDate)
        {
            return occurrenceDate.Date.Add(TimeValue);
        }

        /// <summary>某次命中日的提前提醒时刻。</summary>
        public DateTime GetAdvanceTime(DateTime occurrenceDate)
        {
            return GetAlarmTime(occurrenceDate).AddMinutes(-AdvanceMinutes);
        }

        /// <summary>是否开启重复（唯一标准：recurrence.enabled）。</summary>
        public bool IsRecurring
        {
            get { return Recurrence != null && Recurrence.Enabled; }
        }

        /// <summary>该次发生日是否已完成。</summary>
        public bool IsDone(DateTime occurrenceDate)
        {
            if (DoneDates == null)
            {
                return false;
            }
            string key = DateUtil.FormatDate(occurrenceDate.Date);
            for (int i = 0; i < DoneDates.Count; i++)
            {
                if (string.Equals(DoneDates[i], key, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>标记 / 取消某次发生日的完成状态；返回是否发生变化。</summary>
        public bool SetDone(DateTime occurrenceDate, bool done)
        {
            if (DoneDates == null)
            {
                DoneDates = new List<string>();
            }
            string key = DateUtil.FormatDate(occurrenceDate.Date);
            int index = DoneDates.IndexOf(key);
            if (done)
            {
                if (index >= 0)
                {
                    return false;
                }
                DoneDates.Add(key);
                DoneDates.Sort(StringComparer.Ordinal);
                return true;
            }
            if (index < 0)
            {
                return false;
            }
            DoneDates.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// 补齐缺省字段，保证后续逻辑无需到处判空。
        /// </summary>
        public void Normalize()
        {
            if (string.IsNullOrEmpty(Id))
            {
                Id = IdGenerator.NewId();
            }
            if (string.IsNullOrEmpty(SeriesId))
            {
                SeriesId = Id;
            }
            if (string.IsNullOrEmpty(Date))
            {
                Date = DateUtil.FormatDate(DateTime.Today);
            }
            if (string.IsNullOrEmpty(Time))
            {
                Time = "09:00";
            }
            if (Title == null)
            {
                Title = string.Empty;
            }
            if (Note == null)
            {
                Note = string.Empty;
            }
            if (Tag == null)
            {
                Tag = string.Empty;
            }
            if (AdvanceMinutes < 0)
            {
                AdvanceMinutes = 0;
            }
            if (AdvanceMinutes > 60)
            {
                AdvanceMinutes = 60;
            }
            if (Recurrence == null)
            {
                Recurrence = new RecurrenceRule();
            }
            if (Recurrence.Weekly == null)
            {
                Recurrence.Weekly = new WeeklyRule();
            }
            if (Recurrence.Weekly.Weekdays == null)
            {
                Recurrence.Weekly.Weekdays = new List<int>();
            }
            if (Sound == null)
            {
                Sound = new ScheduleSound();
            }
            if (DoneDates == null)
            {
                DoneDates = new List<string>();
            }
            else
            {
                // 归一化：只保留可解析日期，统一格式，去重（旧数据 / 手改 JSON 兜底）
                List<string> unique = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < DoneDates.Count; i++)
                {
                    DateTime parsed;
                    if (!DateUtil.TryParseDateText(DoneDates[i], out parsed))
                    {
                        continue;
                    }
                    string key = DateUtil.FormatDate(parsed.Date);
                    if (seen.Add(key))
                    {
                        unique.Add(key);
                    }
                }
                unique.Sort(StringComparer.Ordinal);
                DoneDates = unique;
            }
        }

        /// <summary>
        /// 数据字段整体搬运：把 other 的全部落盘字段（[DataMember]）写入自身，跳过身份字段 Id / SeriesId。
        ///
        /// 为什么用反射而不是逐字段手写：手写清单曾两次漏项（Sound、RemindEnabled），
        /// 症状是「界面改了、保存后静默不生效」，且不会有任何编译错误。
        /// 以 [DataMember] 为准遍历，新增落盘字段会被自动带上，不会再漏。
        /// 引用类型字段（Sound / Recurrence）做深拷贝，避免两个日程实例共享同一对象；
        /// 今后若新增引用类型字段，请在此登记其拷贝方式（值类型与 string 会自动生效）。
        /// </summary>
        public void CopyValuesFrom(Schedule other)
        {
            if (other == null || ReferenceEquals(other, this))
            {
                return;
            }
            foreach (PropertyInfo property in DataFields)
            {
                object value = property.GetValue(other, null);
                if (property.PropertyType == typeof(ScheduleSound))
                {
                    ScheduleSound sound = value as ScheduleSound;
                    property.SetValue(this, sound == null
                        ? new ScheduleSound()
                        : new ScheduleSound { Preset = sound.Preset }, null);
                }
                else if (property.PropertyType == typeof(RecurrenceRule))
                {
                    RecurrenceRule rule = value as RecurrenceRule;
                    property.SetValue(this, rule == null ? new RecurrenceRule() : rule.Clone(), null);
                }
                else if (property.PropertyType == typeof(List<string>))
                {
                    List<string> source = value as List<string>;
                    property.SetValue(this, source == null
                        ? new List<string>()
                        : new List<string>(source), null);
                }
                else
                {
                    property.SetValue(this, value, null);
                }
            }
        }

        /// <summary>
        /// 可搬运的数据字段：带 setter 且标了 [DataMember] 的属性，排除身份字段
        /// （Id / SeriesId —— 它们的值由新建 / 分段逻辑决定，不随草稿搬走）。
        /// 首次访问时反射一次后缓存。
        /// </summary>
        private static readonly PropertyInfo[] DataFields = BuildDataFields();

        private static PropertyInfo[] BuildDataFields()
        {
            List<PropertyInfo> fields = new List<PropertyInfo>();
            foreach (PropertyInfo property in typeof(Schedule).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite)
                {
                    continue;
                }
                if (property.GetCustomAttributes(typeof(DataMemberAttribute), true).Length == 0)
                {
                    continue;
                }
                if (string.Equals(property.Name, "Id", StringComparison.Ordinal)
                    || string.Equals(property.Name, "SeriesId", StringComparison.Ordinal))
                {
                    continue;
                }
                fields.Add(property);
            }
            return fields.ToArray();
        }

        /// <summary>值拷贝（分段 / 编辑暂存用）。字段清单复用 CopyValuesFrom，避免两套清单各自漏项。</summary>
        public Schedule Clone()
        {
            Schedule copy = new Schedule();
            copy.CopyValuesFrom(this);
            copy.Id = Id;
            copy.SeriesId = SeriesId;
            return copy;
        }
    }

    /// <summary>
    /// 主键生成器：前缀 + yyyyMMddHHmmss + 随秒重置的两位计数器。
    /// 01 = 日程 / SeriesId，02 = 标签；两套前缀各自维护时间戳与计数器，互不占号。
    /// 必须显式传 InvariantCulture（强制格里历）；落盘为字符串。
    /// 不使用日期字段反解主键，主键只作不透明标识。
    /// </summary>
    public static class IdGenerator
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, PrefixState> States =
            new Dictionary<string, PrefixState>(StringComparer.Ordinal);

        private sealed class PrefixState
        {
            public string LastStamp;
            public int Counter;
        }

        public static string NewId()
        {
            return NewKey("01");
        }

        public static string NewTagId()
        {
            return NewKey("02");
        }

        private static string NewKey(string prefix)
        {
            lock (Gate)
            {
                PrefixState state;
                if (!States.TryGetValue(prefix, out state))
                {
                    state = new PrefixState();
                    States[prefix] = state;
                }

                string stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                if (!string.Equals(state.LastStamp, stamp, StringComparison.Ordinal))
                {
                    state.LastStamp = stamp;
                    state.Counter = 0;
                }

                int sequence = state.Counter;
                state.Counter++;
                return prefix + stamp + sequence.ToString("D2", CultureInfo.InvariantCulture);
            }
        }
    }
}
