using System;
using System.Collections.Generic;
using System.Globalization;
using ScheduleReminder.Models;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 日程 CRUD 服务，封装「分段 + 软删除 + seriesId 生命周期」（P0-8 ⑥⑦）。
    /// </summary>
    public class ScheduleService
    {
        private readonly Repository _repository;

        public ScheduleService(Repository repository)
        {
            _repository = repository;
            NormalizeTags();
        }

        /// <summary>
        /// 标签文本归一：仅去首尾空格。
        /// 不再校验「是否仍在模板列表中」——日程上的标签名是一份文本快照，
        /// 模板之后改名或被删除，都不应抹掉用户日程上已有的标签。
        /// </summary>
        private void NormalizeTags()
        {
            bool changed = false;
            List<Schedule> candidate = _repository.SnapshotShallow();
            for (int i = 0; i < candidate.Count; i++)
            {
                Schedule schedule = candidate[i];
                if (schedule == null || string.IsNullOrEmpty(schedule.Tag))
                {
                    continue;
                }
                string trimmed = schedule.Tag.Trim();
                if (!string.Equals(trimmed, schedule.Tag, StringComparison.Ordinal))
                {
                    Schedule copy = schedule.Clone();
                    copy.Tag = trimmed;
                    candidate[i] = copy;
                    changed = true;
                }
            }
            if (changed)
            {
                _repository.TrySave(candidate);
            }
        }

        // ---------------- 查询 ----------------

        /// <summary>某日的全部日程（含重复日程惰性展开命中项），按时间升序。</summary>
        public List<ScheduleOccurrence> GetOccurrences(DateTime date)
        {
            List<ScheduleOccurrence> result = new List<ScheduleOccurrence>();
            List<Schedule> all = _repository.All;
            for (int i = 0; i < all.Count; i++)
            {
                Schedule schedule = all[i];
                if (schedule == null)
                {
                    continue;
                }
                // 超范围的日程一律不显示：
                // 数据里若混进了 1900 之前 / 2100 之后的记录，不渲染、不统计，
                // 避免出现「数据存在但月历翻不到、也删不掉」的半吊子状态。
                if (schedule.DateValue.Date < DateUtil.MinSupportedDate
                    || schedule.DateValue.Date > DateUtil.MaxSupportedDate)
                {
                    continue;
                }
                if (RecurrenceService.IsHit(schedule, date))
                {
                    ScheduleOccurrence occurrence = new ScheduleOccurrence();
                    occurrence.Schedule = schedule;
                    occurrence.OccurrenceDate = date;
                    result.Add(occurrence);
                }
            }
            result.Sort(CompareOccurrence);
            return result;
        }

        private static int CompareOccurrence(ScheduleOccurrence a, ScheduleOccurrence b)
        {
            int compare = a.AlarmTime.CompareTo(b.AlarmTime);
            if (compare != 0)
            {
                return compare;
            }
            return string.CompareOrdinal(a.Schedule.Id, b.Schedule.Id);
        }

        // ---------------- 过期 / 生效判定 ----------------

        // 同名静态方法转发到 Domain 规则，供各窗口直接调用。

        /// <summary>已过期判定。规则详见 Domain\ScheduleRules.IsExpired。</summary>
        public static bool IsExpired(Schedule schedule, DateTime now)
        {
            return ScheduleRules.IsExpired(schedule, now);
        }

        /// <summary>是否已生效过一次。规则详见 Domain\ScheduleRules.HasTakenEffect。</summary>
        public static bool HasTakenEffect(Schedule schedule, DateTime now)
        {
            return ScheduleRules.HasTakenEffect(schedule, now);
        }

        // ---------------- 输入校验 ----------------

        /// <summary>时间文本校验。规则详见 Domain\ScheduleRules.TryParseTimeText。</summary>
        public static bool TryParseTimeText(string text, out TimeSpan value)
        {
            return ScheduleRules.TryParseTimeText(text, out value);
        }

        /// <summary>提前提醒文本校验。规则详见 Domain\ScheduleRules.TryParseAdvanceText。</summary>
        public static bool TryParseAdvanceText(string text, out int minutes)
        {
            return ScheduleRules.TryParseAdvanceText(text, out minutes);
        }

        /// <summary>边界秒规则。规则详见 Domain\ScheduleRules.CheckBoundary。</summary>
        public static bool CheckBoundary(DateTime date, TimeSpan time, int advanceMinutes)
        {
            return ScheduleRules.CheckBoundary(date, time, advanceMinutes);
        }

        // ---------------- 创建 ----------------

        public SaveOutcome Create(Schedule draft)
        {
            if (draft == null)
            {
                return SaveOutcome.Ok;
            }
            draft.Id = IdGenerator.NewId();
            draft.SeriesId = draft.Id;
            draft.Normalize();
            // RemindEnabled 由调用方给定（新建窗的「开启提醒」勾选框），不再强制置 true
            NormalizeAnchor(draft);
            List<Schedule> candidate = _repository.SnapshotShallow();
            candidate.Add(draft);
            return Commit(candidate);
        }

        // ---------------- 更新 ----------------

        /// <summary>
        /// 更新日程：已生效重复先按留档字段判断是否分段；只改外观/状态则原地改并同步全系列视觉。
        /// </summary>
        public SaveOutcome Update(Schedule existing, Schedule draft)
        {
            if (existing == null || draft == null)
            {
                return SaveOutcome.Ok;
            }
            DateTime now = DateTime.Now;
            DateTime today = now.Date;
            if (string.IsNullOrEmpty(draft.Id))
            {
                draft.Id = existing.Id;
            }
            if (string.IsNullOrEmpty(draft.SeriesId))
            {
                draft.SeriesId = existing.SeriesId;
            }
            draft.Normalize();

            List<Schedule> candidate = _repository.SnapshotShallow();
            int index = candidate.IndexOf(existing);
            if (index < 0)
            {
                return SaveOutcome.Ok;
            }

            if (IsExpired(existing, now))
            {
                LogWriter.Write("拒绝更新已过期日程：" + existing.Id);
                return SaveOutcome.Ok;
            }

            // 已生效重复的起始日期不落库；该值只用于校验与展示。
            if (existing.IsRecurring && HasTakenEffect(existing, now))
            {
                draft.Date = existing.Date;
            }

            if (!existing.IsRecurring)
            {
                // 未过期的一次性日程：id / seriesId 均不变
                Schedule edited = existing.Clone();
                edited.CopyValuesFrom(draft);
                NormalizeAnchor(edited);
                candidate[index] = edited;
                return Commit(candidate);
            }

            if (!HasTakenEffect(existing, now))
            {
                // 重复日程从未生效：不分段，直接修改原记录
                Schedule edited = existing.Clone();
                edited.CopyValuesFrom(draft);
                NormalizeAnchor(edited);
                candidate[index] = edited;
                return Commit(candidate);
            }

            if (!HasArchiveFieldChanged(existing, draft))
            {
                // 只改外观或状态：原地改；只有外观变化时才同步全系列（含历史段）
                Schedule edited = existing.Clone();
                edited.CopyValuesFrom(draft);
                candidate[index] = edited;
                string oldSound = existing.Sound != null ? existing.Sound.Preset : null;
                string newSound = edited.Sound != null ? edited.Sound.Preset : null;
                if (!string.Equals(existing.BgColor ?? string.Empty, edited.BgColor ?? string.Empty,
                        StringComparison.Ordinal)
                    || !string.Equals(existing.FontColor ?? string.Empty,
                        edited.FontColor ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(oldSound ?? string.Empty, newSound ?? string.Empty,
                        StringComparison.Ordinal))
                {
                    SyncSeriesVisuals(candidate, existing.SeriesId,
                        edited.BgColor, edited.FontColor, newSound);
                }
                return Commit(candidate);
            }

            // 已生效的重复日程：分段（旧段收敛为历史段 + 新建未来段）
            bool todayIsHit = RecurrenceService.IsHit(existing, today);
            bool todayNotYet = existing.TimeValue > now.TimeOfDay;
            bool todayGoesToFuture = todayIsHit && todayNotYet;

            DateTime oldUntil = todayGoesToFuture ? today.AddDays(-1) : today;
            DateTime newStart = todayGoesToFuture ? today : today.AddDays(1);

            if (draft.DateValue > newStart)
            {
                newStart = draft.DateValue;
            }

            // 到期关闭早于分段点：只收敛旧段、不再新建未来段
            if (draft.IsRecurring && draft.Recurrence != null
                && !string.IsNullOrEmpty(draft.Recurrence.Until))
            {
                DateTime draftUntil;
                if (DateUtil.TryParseDateText(draft.Recurrence.Until, out draftUntil)
                    && draftUntil < newStart)
                {
                    DateTime close = draftUntil < oldUntil ? draftUntil : oldUntil;
                    if (close < existing.DateValue)
                    {
                        candidate.RemoveAt(index);
                        return Commit(candidate);
                    }
                    Schedule clipped = existing.Clone();
                    clipped.Recurrence.Until = DateUtil.FormatDate(close);
                    candidate[index] = clipped;
                    return Commit(candidate);
                }
            }

            Schedule future = draft.Clone();
            future.Id = IdGenerator.NewId();
            future.RemindEnabled = draft.RemindEnabled;

            bool titleChanged = !string.Equals(draft.Title, existing.Title, StringComparison.Ordinal);
            bool timeChanged = !string.Equals(draft.Time, existing.Time, StringComparison.Ordinal);
            future.SeriesId = (titleChanged && timeChanged) ? future.Id : existing.SeriesId;

            future.Date = DateUtil.FormatDate(newStart);
            NormalizeAnchor(future);

            DateTime futureUntil = DateTime.MinValue;
            bool hasFutureUntil = future.Recurrence != null
                && !string.IsNullOrEmpty(future.Recurrence.Until)
                && DateUtil.TryParseDateText(future.Recurrence.Until, out futureUntil);
            if (draft.IsRecurring && draft.Recurrence != null
                && (!future.IsRecurring || (hasFutureUntil && future.DateValue > futureUntil)))
            {
                if (oldUntil < existing.DateValue)
                {
                    candidate.RemoveAt(index);
                }
                else
                {
                    Schedule past = existing.Clone();
                    past.Recurrence.Until = DateUtil.FormatDate(oldUntil);
                    candidate[index] = past;
                }
                return Commit(candidate);
            }

            if (oldUntil < existing.DateValue)
            {
                candidate.RemoveAt(index);
            }
            else
            {
                Schedule past = existing.Clone();
                past.Recurrence.Until = DateUtil.FormatDate(oldUntil);
                TrimDoneDates(past, DateTime.MinValue, oldUntil);
                candidate[index] = past;
            }
            TrimDoneDates(future, newStart, DateTime.MaxValue);
            candidate.Add(future);
            return Commit(candidate);
        }

        /// <summary>返回同系列所有段中最早的起始日；没有系列时返回 fallback。</summary>
        public DateTime GetSeriesMinStart(string seriesId, DateTime fallback)
        {
            DateTime min = fallback.Date;
            if (string.IsNullOrEmpty(seriesId))
            {
                return min;
            }
            List<Schedule> sameSeries = _repository.FindBySeries(seriesId);
            for (int i = 0; i < sameSeries.Count; i++)
            {
                Schedule item = sameSeries[i];
                if (item != null && item.DateValue < min)
                {
                    min = item.DateValue;
                }
            }
            return min;
        }

        /// <summary>一次性日程是否完全没有变化：用于保存早退，重复日程一律不早退。</summary>
        public static bool IsUnchanged(Schedule existing, Schedule draft)
        {
            if (existing == null || draft == null || existing.IsRecurring)
            {
                return false;
            }
            if (!SameText(existing.Title, draft.Title)
                || !SameText(existing.Date, draft.Date)
                || !SameText(existing.Time, draft.Time)
                || !SameText(existing.Note, draft.Note)
                || !SameText(existing.Tag, draft.Tag)
                || !SameText(existing.TagId, draft.TagId)
                || existing.AdvanceMinutes != draft.AdvanceMinutes
                || !SameText(existing.BgColor, draft.BgColor)
                || !SameText(existing.FontColor, draft.FontColor)
                || !SameText(existing.Sound != null ? existing.Sound.Preset : null,
                    draft.Sound != null ? draft.Sound.Preset : null)
                || existing.RemindEnabled != draft.RemindEnabled
                || existing.IsTodo != draft.IsTodo
                || !SameDates(existing.DoneDates, draft.DoneDates))
            {
                return false;
            }
            return RecurrenceEquals(existing.Recurrence, draft.Recurrence);
        }

        private static bool SameText(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty,
                StringComparison.Ordinal);
        }

        private static bool SameDates(List<string> a, List<string> b)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i] ?? string.Empty, b[i] ?? string.Empty,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>是否改到留档字段（决定已生效重复是否分段）。</summary>
        private static bool HasArchiveFieldChanged(Schedule existing, Schedule draft)
        {
            if (existing == null || draft == null)
            {
                return false;
            }
            if (!string.Equals(existing.Title, draft.Title, StringComparison.Ordinal)
                || !string.Equals(existing.Time, draft.Time, StringComparison.Ordinal)
                || !string.Equals(existing.Note ?? string.Empty, draft.Note ?? string.Empty,
                    StringComparison.Ordinal)
                || existing.AdvanceMinutes != draft.AdvanceMinutes
                || !string.Equals(existing.Tag ?? string.Empty, draft.Tag ?? string.Empty,
                    StringComparison.Ordinal)
                || !string.Equals(existing.TagId ?? string.Empty, draft.TagId ?? string.Empty,
                    StringComparison.Ordinal))
            {
                return true;
            }
            return !RecurrenceEquals(existing.Recurrence, draft.Recurrence);
        }

        private static bool RecurrenceEquals(RecurrenceRule a, RecurrenceRule b)
        {
            bool aEnabled = a != null && a.Enabled;
            bool bEnabled = b != null && b.Enabled;
            if (aEnabled != bEnabled)
            {
                return false;
            }
            if (!aEnabled)
            {
                return true;
            }

            if (!string.Equals(a.Until ?? string.Empty, b.Until ?? string.Empty,
                    StringComparison.Ordinal))
            {
                return false;
            }
            if (!YearlyEquals(a.Yearly, b.Yearly)
                || !MonthlyEquals(a.Monthly, b.Monthly)
                || !MonthlyLastNEquals(a.MonthlyLastN, b.MonthlyLastN)
                || !MonthlyLastWeekdayEquals(a.MonthlyLastWeekday, b.MonthlyLastWeekday))
            {
                return false;
            }
            return WeekdaysEqual(a.Weekly, b.Weekly);
        }

        private static bool YearlyEquals(YearlyRule a, YearlyRule b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            return a.Month == b.Month && a.Day == b.Day;
        }

        private static bool MonthlyEquals(MonthlyRule a, MonthlyRule b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            return a.Day == b.Day;
        }

        private static bool MonthlyLastNEquals(MonthlyLastNRule a, MonthlyLastNRule b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            return a.N == b.N;
        }

        private static bool MonthlyLastWeekdayEquals(
            MonthlyLastWeekdayRule a, MonthlyLastWeekdayRule b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            return a.Weekday == b.Weekday;
        }

        private static bool WeekdaysEqual(WeeklyRule a, WeeklyRule b)
        {
            List<int> left = a != null && a.Weekdays != null
                ? new List<int>(a.Weekdays)
                : new List<int>();
            List<int> right = b != null && b.Weekdays != null
                ? new List<int>(b.Weekdays)
                : new List<int>();
            if (left.Count != right.Count)
            {
                return false;
            }
            left.Sort();
            right.Sort();
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>把外观字段同步到同系列全部段（含过期段）。必须 clone 后替换，避免写盘失败时污染内存。</summary>
        private static void SyncSeriesVisuals(
            List<Schedule> candidate, string seriesId, string bgColor, string fontColor,
            string soundPreset)
        {
            if (candidate == null || string.IsNullOrEmpty(seriesId))
            {
                return;
            }
            for (int i = 0; i < candidate.Count; i++)
            {
                Schedule item = candidate[i];
                if (item == null || !string.Equals(item.SeriesId, seriesId, StringComparison.Ordinal))
                {
                    continue;
                }
                Schedule copy = item.Clone();
                copy.BgColor = bgColor;
                copy.FontColor = fontColor;
                copy.Sound = new ScheduleSound { Preset = soundPreset };
                candidate[i] = copy;
            }
        }

        /// <summary>把完成记录裁剪到 [min, max] 范围内（分段用）。</summary>
        private static void TrimDoneDates(Schedule schedule, DateTime min, DateTime max)
        {
            if (schedule == null || schedule.DoneDates == null)
            {
                return;
            }
            for (int i = schedule.DoneDates.Count - 1; i >= 0; i--)
            {
                DateTime parsed;
                if (!DateUtil.TryParseDateText(schedule.DoneDates[i], out parsed)
                    || parsed.Date < min.Date
                    || parsed.Date > max.Date)
                {
                    schedule.DoneDates.RemoveAt(i);
                }
            }
        }


        /// <summary>
        /// 锚点归一（P0-8 ⑧）：重复日程的顶层 date = 规则在「≥ max(当前值, 今天)」的第一个实际发生日。
        /// </summary>
        private static void NormalizeAnchor(Schedule schedule)
        {
            if (!schedule.IsRecurring)
            {
                return;
            }
            DateTime start = schedule.DateValue;
            if (start < DateTime.Today)
            {
                start = DateTime.Today;
            }
            // 到期日纳入锚点计算：只在 [start, until] 内找首响日。
            // 若 until 之内没有任何符合规则的命中（例如「每月5号」+ 到期日 10-20：下一个5号是 11-05 已超期），
            // 则该重复实际无意义——关闭重复、退回一次性（起始日 = start），
            // 而绝不让 Date 落到 until 之后（否则会变成「永远不显示」的幽灵日程，即「到期关闭无法落实」）。
            DateTime? until = null;
            if (schedule.Recurrence != null && !string.IsNullOrEmpty(schedule.Recurrence.Until))
            {
                until = DateUtil.ParseDateNullable(schedule.Recurrence.Until);
            }
            DateTime? first = RecurrenceService.FirstHitOnOrAfter(schedule.Recurrence, start, until: until);
            if (first.HasValue)
            {
                schedule.Date = DateUtil.FormatDate(first.Value);
            }
            else
            {
                schedule.Recurrence.Enabled = false;
                schedule.Date = DateUtil.FormatDate(start);
            }
        }

        // ---------------- 删除 ----------------

        public DeletePlan PlanDelete(Schedule schedule)
        {
            DateTime now = DateTime.Now;
            DeletePlan plan = new DeletePlan();
            plan.Source = schedule;
            if (schedule == null)
            {
                return plan;
            }
            plan.Title = schedule.Title;

            if (IsExpired(schedule, now))
            {
                // 删除过期日程：批量清理同一 seriesId 的全部已过期记录
                plan.Kind = DeleteKind.PurgeExpired;
                List<Schedule> sameSeries = _repository.FindBySeries(schedule.SeriesId);
                for (int i = 0; i < sameSeries.Count; i++)
                {
                    if (IsExpired(sameSeries[i], now))
                    {
                        plan.Targets.Add(sameSeries[i]);
                    }
                }
                if (!plan.Targets.Contains(schedule))
                {
                    plan.Targets.Add(schedule);
                }
                return plan;
            }

            if (!schedule.IsRecurring)
            {
                plan.Kind = DeleteKind.RemoveOne;
                plan.Targets.Add(schedule);
                return plan;
            }

            if (HasTakenEffect(schedule, now))
            {
                // 软删除：收敛 until = 今日，停止未来，保留过往
                plan.Kind = DeleteKind.StopFuture;
                plan.Targets.Add(schedule);
            }
            else
            {
                plan.Kind = DeleteKind.RemoveOne;
                plan.Targets.Add(schedule);
            }
            return plan;
        }

        public SaveOutcome ExecuteDelete(DeletePlan plan)
        {
            if (plan == null)
            {
                return SaveOutcome.Ok;
            }
            List<Schedule> candidate = _repository.SnapshotShallow();
            if (plan.Kind == DeleteKind.StopFuture)
            {
                if (plan.Source == null || plan.Source.Recurrence == null)
                {
                    return SaveOutcome.Ok;
                }
                int index = candidate.IndexOf(plan.Source);
                if (index < 0)
                {
                    return SaveOutcome.Ok;
                }
                Schedule clipped = plan.Source.Clone();
                clipped.Recurrence.Until = DateUtil.FormatDate(DateTime.Today);
                candidate[index] = clipped;
            }
            else
            {
                for (int i = 0; i < plan.Targets.Count; i++)
                {
                    candidate.Remove(plan.Targets[i]);
                }
            }
            return Commit(candidate);
        }

        /// <summary>彻底删除整个系列（含未来段与历史段）。</summary>
        public SaveOutcome DeleteEntireSeries(string seriesId)
        {
            if (string.IsNullOrEmpty(seriesId))
            {
                return SaveOutcome.Ok;
            }
            List<Schedule> candidate = _repository.SnapshotShallow();
            List<Schedule> targets = _repository.FindBySeries(seriesId);
            for (int i = 0; i < targets.Count; i++)
            {
                candidate.Remove(targets[i]);
            }
            return Commit(candidate);
        }

        // ---------------- 单项字段快速写入 ----------------

        public SaveOutcome SetRemindEnabled(Schedule schedule, bool enabled)
        {
            if (schedule == null || schedule.RemindEnabled == enabled)
            {
                return SaveOutcome.Ok;
            }
            bool old = schedule.RemindEnabled;
            schedule.RemindEnabled = enabled;
            SaveOutcome outcome = SaveCurrent();
            if (outcome != SaveOutcome.Ok)
            {
                schedule.RemindEnabled = old;
            }
            AppServices.RaiseScheduleFieldChanged();
            return outcome;
        }

        /// <summary>勾选 / 取消某次发生日的完成状态；只改 doneDates，不动 RemindEnabled。</summary>
        public SaveOutcome SetDone(Schedule schedule, DateTime occurrenceDate, bool done)
        {
            if (schedule == null || !schedule.SetDone(occurrenceDate, done))
            {
                return SaveOutcome.Ok;
            }
            SaveOutcome outcome = SaveCurrent();
            if (outcome != SaveOutcome.Ok)
            {
                schedule.SetDone(occurrenceDate, !done);
            }
            AppServices.RaiseScheduleFieldChanged();
            return outcome;
        }

        /// <summary>切换待办标记；只改 isTodo，不影响完成记录与提醒开关。</summary>
        public SaveOutcome SetTodo(Schedule schedule, bool isTodo)
        {
            if (schedule == null || schedule.IsTodo == isTodo)
            {
                return SaveOutcome.Ok;
            }
            bool old = schedule.IsTodo;
            schedule.IsTodo = isTodo;
            SaveOutcome outcome = SaveCurrent();
            if (outcome != SaveOutcome.Ok)
            {
                schedule.IsTodo = old;
            }
            AppServices.RaiseScheduleFieldChanged();
            return outcome;
        }

        // ---------------- 落盘 ----------------

        private SaveOutcome Commit(List<Schedule> candidate)
        {
            SaveOutcome outcome = _repository.TrySave(candidate);
            if (outcome == SaveOutcome.Ok)
            {
                AppServices.RaiseDataChanged();
            }
            return outcome;
        }

        private SaveOutcome SaveCurrent()
        {
            return _repository.TrySave(_repository.SnapshotShallow());
        }
    }
}
