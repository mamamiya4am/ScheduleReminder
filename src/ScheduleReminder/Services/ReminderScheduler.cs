using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using ScheduleReminder.Models;
using ScheduleReminder.Views;
using Application = System.Windows.Application;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 提醒调度器：一条提醒一个独立浮层（容量上限 30 的队列仍保留）。
    /// 心跳 1 秒；每条提醒弹出并移除后立即以当前时间为起点补足窗口至最多 30 条（事件驱动「弹出即自补」），
    /// 不做每日 0:00 定时刷新；任意 CRUD 后即时重建队列。
    /// </summary>
    public class ReminderScheduler
    {
        private const int Capacity = 30;
        private const int SearchDays = 12000;

        private readonly Repository _repository;
        private readonly ISoundService _sound;
        private readonly List<ReminderEntry> _queue = new List<ReminderEntry>();
        private readonly HashSet<string> _fired = new HashSet<string>();

        /// <summary>「已提醒过」的淘汰顺序（FIFO）：超过 Capacity 就丢最早的一条，杜绝长期常驻的内存增长。</summary>
        private readonly Queue<string> _firedOrder = new Queue<string>();
        private readonly object _syncRoot = new object();
        private readonly List<ReminderWindow> _popups = new List<ReminderWindow>();

        private Timer _timer;

        public ReminderScheduler(Repository repository, ISoundService sound)
        {
            _repository = repository;
            _sound = sound;
        }

        public void Start()
        {
            Rebuild();
            _timer = new Timer(OnTick, null, 1000, 1000);
        }

        public void Stop()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
        }

        /// <summary>重建提醒队列（CRUD 后调用）。</summary>
        public void Rebuild()
        {
            lock (_syncRoot)
            {
                RebuildInternal();
            }
        }

        private void RebuildInternal()
        {
            DateTime now = DateTime.Now;
            DateTime from = now.Date;
            List<ReminderEntry> candidates = new List<ReminderEntry>();

            List<Schedule> all = _repository.All;
            for (int i = 0; i < all.Count; i++)
            {
                Schedule schedule = all[i];
                if (schedule == null)
                {
                    continue;
                }
                if (!schedule.RemindEnabled)
                {
                    continue;
                }

                // 只要「全局最早 30 条」，因此每条日程最多取 30 个命中日即可（升序截断是安全的）。
                List<DateTime> dates = RecurrenceService.EnumerateOccurrences(schedule, from, SearchDays, Capacity);
                for (int j = 0; j < dates.Count; j++)
                {
                    // 已勾选完成的那一次：只跳过该次，其他发生照常提醒
                    if (schedule.IsDone(dates[j]))
                    {
                        continue;
                    }
                    DateTime alarm = schedule.GetAlarmTime(dates[j]);
                    if (schedule.AdvanceMinutes > 0)
                    {
                        DateTime advanceTime = alarm.AddMinutes(-schedule.AdvanceMinutes);
                        if (advanceTime > now)
                        {
                            candidates.Add(MakeEntry(schedule, dates[j], advanceTime, 0));
                        }
                    }
                    if (alarm > now)
                    {
                        candidates.Add(MakeEntry(schedule, dates[j], alarm, 1));
                    }
                }
            }

            candidates.Sort(CompareByFireTime);
            _queue.Clear();
            for (int i = 0; i < candidates.Count && _queue.Count < Capacity; i++)
            {
                if (!_fired.Contains(candidates[i].Key))
                {
                    _queue.Add(candidates[i]);
                }
            }
        }

        /// <summary>
        /// 记录「这条已经提醒过了」，并把名单长度恒定限制在 Capacity（30）条以内：超出即淘汰最早的一条。
        /// 已知限制（用户已接受）：系统时间被改回过去时，已被淘汰的较早提醒可能重复弹一次。
        /// </summary>
        private void MarkFired(string key)
        {
            if (!_fired.Add(key))
            {
                return;
            }
            _firedOrder.Enqueue(key);
            while (_firedOrder.Count > Capacity)
            {
                _fired.Remove(_firedOrder.Dequeue());
            }
        }

        private static int CompareByFireTime(ReminderEntry a, ReminderEntry b)
        {
            int compare = a.FireTime.CompareTo(b.FireTime);
            if (compare != 0)
            {
                return compare;
            }
            return a.Kind.CompareTo(b.Kind);
        }

        private static ReminderEntry MakeEntry(Schedule schedule, DateTime occurrenceDate, DateTime fireTime, int kind)
        {
            ReminderEntry entry = new ReminderEntry();
            entry.ScheduleId = schedule.Id;
            entry.SeriesId = schedule.SeriesId;
            entry.OccurrenceDate = occurrenceDate;
            entry.FireTime = fireTime;
            entry.Kind = kind;
            entry.Title = schedule.Title;
            entry.TimeText = DateUtil.FormatTime(schedule.TimeValue);
            entry.Note = schedule.Note;
            entry.Tag = schedule.Tag;
            entry.BgColor = schedule.BgColor;
            entry.FontColor = schedule.FontColor;
            entry.SoundName = schedule.Sound != null ? schedule.Sound.Preset : null;
            entry.AdvanceMinutes = schedule.AdvanceMinutes;
            if (kind == 0)
            {
                TimeSpan span = schedule.GetAlarmTime(occurrenceDate) - fireTime;
                entry.RemainingMinutes = (int)Math.Max(1, Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero));
            }
            return entry;
        }

        private void OnTick(object state)
        {
            try
            {
                List<ReminderEntry> due = new List<ReminderEntry>();
                lock (_syncRoot)
                {
                    DateTime now = DateTime.Now;
                    while (_queue.Count > 0 && _queue[0].FireTime <= now)
                    {
                        ReminderEntry entry = _queue[0];
                        _queue.RemoveAt(0);
                        MarkFired(entry.Key);
                        due.Add(entry);
                    }
                }

                if (due.Count == 0)
                {
                    return;
                }

                Application app = Application.Current;
                if (app == null)
                {
                    return;
                }
                app.Dispatcher.BeginInvoke(new Action(delegate
                {
                    for (int i = 0; i < due.Count; i++)
                    {
                        Fire(due[i]);
                    }
                    // 弹出即自补：补足窗口至最多 30 条
                    Rebuild();
                }));
            }
            catch (Exception ex)
            {
                LogWriter.Write(ex);
            }
        }

        private void Fire(ReminderEntry entry)
        {
            Schedule schedule = _repository.FindById(entry.ScheduleId);
            if (schedule != null && !schedule.RemindEnabled)
            {
                return;
            }
            // 弹出前二次校验：该次已勾选完成则不再弹出
            if (schedule != null && schedule.IsDone(entry.OccurrenceDate))
            {
                return;
            }

            ReminderWindow popup = new ReminderWindow();
            popup.Closed += OnPopupClosed;
            _popups.Add(popup);

            // 新提醒接管声音；关闭任意浮层时统一停止全部声音。
            _sound.Stop();
            _sound.Play(entry != null ? entry.SoundName : null);
            popup.ShowReminder(entry);
        }

        private void OnPopupClosed(object sender, EventArgs e)
        {
            ReminderWindow popup = sender as ReminderWindow;
            if (popup == null)
            {
                return;
            }

            _popups.Remove(popup);
            popup.Closed -= OnPopupClosed;

            // 关闭任意浮层都停止全部声音（包括被压在下面的浮层）。
            _sound.Stop();
            if (_popups.Count > 0)
            {
                _popups[_popups.Count - 1].BringToFront();
            }
        }

        /// <summary>供程序退出时调用：停止声音。</summary>
        public void StopSound()
        {
            _sound.Stop();
        }
    }
}
