using System;
using System.Collections.Generic;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>标签预设与标签推送的共享实现：手动设置窗与 AI 执行器同走一份。</summary>
    public static class ScheduleTagService
    {
        public static void ApplyToSchedule(Schedule draft, TagTemplate tag)
        {
            if (draft == null || tag == null)
            {
                return;
            }
            draft.TagId = tag.Id;
            draft.Tag = tag.Name;
            draft.BgColor = string.IsNullOrEmpty(tag.BgColor) ? null : tag.BgColor;
            draft.FontColor = string.IsNullOrEmpty(tag.FontColor) ? null : tag.FontColor;
            draft.Sound = new ScheduleSound
            {
                Preset = string.IsNullOrEmpty(tag.Sound) ? null : tag.Sound
            };
        }

        /// <summary>按 TagId 把模板名字/颜色/字色/铃声同步到全部日程（含过期段）。</summary>
        public static SaveOutcome PushToSchedules(TagTemplate target)
        {
            if (target == null || string.IsNullOrEmpty(target.Id)
                || AppServices.Repository == null)
            {
                return SaveOutcome.Ok;
            }

            List<Schedule> candidate = AppServices.Repository.SnapshotShallow();
            bool changed = false;
            for (int i = 0; i < candidate.Count; i++)
            {
                Schedule schedule = candidate[i];
                if (schedule == null
                    || string.IsNullOrEmpty(schedule.TagId)
                    || !string.Equals(schedule.TagId, target.Id, StringComparison.Ordinal))
                {
                    continue;
                }
                Schedule copy = schedule.Clone();
                copy.Tag = target.Name;
                copy.BgColor = string.IsNullOrEmpty(target.BgColor) ? null : target.BgColor;
                copy.FontColor = string.IsNullOrEmpty(target.FontColor) ? null : target.FontColor;
                copy.Sound = new ScheduleSound
                {
                    Preset = string.IsNullOrEmpty(target.Sound) ? null : target.Sound
                };
                candidate[i] = copy;
                changed = true;
            }

            if (!changed)
            {
                return SaveOutcome.Ok;
            }
            return AppServices.Repository.TrySave(candidate);
        }
    }
}
