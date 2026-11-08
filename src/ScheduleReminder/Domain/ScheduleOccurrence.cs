using System;

namespace ScheduleReminder.Domain
{
    /// <summary>
    /// 一次「日程 × 命中日期」的展开结果（惰性展开，不落盘）。
    /// 它只描述"日程在某一天的那一次"，
    /// 不含任何界面状态，属于领域概念。
    /// </summary>
    public class ScheduleOccurrence
    {
        public Schedule Schedule { get; set; }

        public DateTime OccurrenceDate { get; set; }

        public DateTime AlarmTime
        {
            get { return Schedule.GetAlarmTime(OccurrenceDate); }
        }

        /// <summary>编辑窗 / 预览窗的灰蒙版判定：alarmTime &lt; now。</summary>
        public bool IsPast
        {
            get { return AlarmTime < DateTime.Now; }
        }
    }
}
