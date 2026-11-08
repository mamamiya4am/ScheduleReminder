using System;
using ScheduleReminder.Models;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>全局服务容器（单实例，进程级）。</summary>
    public static class AppServices
    {
        static AppServices()
        {
            Settings = new AppSettings();
            Tags = new TagStore();
        }

        public static AppSettings Settings { get; set; }

        public static TagStore Tags { get; set; }

        public static Repository Repository { get; set; }

        public static CalendarService Calendar { get; set; }

        public static ScheduleService Schedules { get; set; }

        public static ReminderScheduler Scheduler { get; set; }

        public static ISoundService Sound { get; set; }

        public static Views.MainWindow MainWindow { get; set; }

        private static int _batchDepth;

        public static bool IsBatching
        {
            get { return _batchDepth > 0; }
        }

        public static void BeginBatch()
        {
            _batchDepth++;
        }

        public static void EndBatch()
        {
            if (_batchDepth <= 0)
            {
                return;
            }
            _batchDepth--;
            if (_batchDepth == 0)
            {
                RaiseDataChanged();
            }
        }

        /// <summary>任意 CRUD 后触发：刷新界面 + 重建提醒队列。</summary>
        public static event EventHandler DataChanged;

        public static void RaiseDataChanged()
        {
            if (IsBatching)
            {
                return;
            }
            EventHandler handler = DataChanged;
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 单条日程的「字段级」变化（例如勾选开关）：结构没变、只是某一行里的值变了。
        /// 与 DataChanged 的区别：订阅方**就地更新那一行**即可，不必清空重建整表、更不必重建主窗日历
        /// （勾选不参与日历渲染）。提醒队列仍要听它（否则关掉的日程还会弹提醒）。
        /// </summary>
        public static event EventHandler ScheduleFieldChanged;

        public static void RaiseScheduleFieldChanged()
        {
            if (IsBatching)
            {
                return;
            }
            EventHandler handler = ScheduleFieldChanged;
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        public static bool SaveSettings()
        {
            bool ok = SettingsStore.Save(Settings);
            if (!ok)
            {
                DialogService.ShowReadOnlyError();
            }
            return ok;
        }
    }
}
