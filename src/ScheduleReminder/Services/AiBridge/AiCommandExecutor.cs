using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using ScheduleReminder.Domain;
using ScheduleReminder.Models;

namespace ScheduleReminder.Services
{
    /// <summary>AI 文件投递执行器：刷新时处理一次 inbox，写 outbox 后消费 inbox。</summary>
    public static class AiCommandExecutor
    {
        private const int MaxCommands = 30;
        private const int MaxInboxBytes = 1024 * 50;
        private const int MaxJsonChars = 1024 * 1024;

        public static void ProcessIfEnabled()
        {
            if (AppServices.Settings == null || !AppServices.Settings.AiIntegrationEnabled
                || AppServices.Repository == null || AppServices.Tags == null)
            {
                return;
            }
            if (!File.Exists(AppPaths.AiInboxPath))
            {
                return;
            }

            string raw;
            try
            {
                raw = File.ReadAllText(AppPaths.AiInboxPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogWriter.Write("读取 AI inbox 失败", ex);
                return;
            }

            if (Encoding.UTF8.GetByteCount(raw) > MaxInboxBytes)
            {
                CompleteSkipped("输入超过 50 KB", null, null);
                return;
            }

            AiCommandSet set;
            List<object> rawCommands;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = MaxJsonChars;
                object parsed = serializer.DeserializeObject(raw);
                rawCommands = ExtractCommands(parsed);
                set = serializer.Deserialize<AiCommandSet>(raw);
            }
            catch (Exception ex)
            {
                LogWriter.Write("解析 AI inbox 失败", ex);
                CompleteSkipped("格式错误", raw, null);
                return;
            }
            if (set == null || set.Commands == null || set.Commands.Count == 0)
            {
                CompleteSkipped("没有命令", raw, rawCommands);
                return;
            }
            if (set.Commands.Count > MaxCommands)
            {
                CompleteSkipped("命令条数超过三十条上限", null, rawCommands);
                return;
            }

            List<AiCommandResult> results = new List<AiCommandResult>();
            AppServices.BeginBatch();
            try
            {
                for (int i = 0; i < set.Commands.Count; i++)
                {
                    AiCommandResult result;
                    try
                    {
                        result = Execute(set.Commands[i]);
                    }
                    catch (Exception ex)
                    {
                        LogWriter.Write("执行 AI 命令失败", ex);
                        result = new AiCommandResult
                        {
                            Seq = set.Commands[i] != null ? set.Commands[i].Seq : i + 1,
                            Ok = false,
                            Reason = "保存失败"
                        };
                    }
                    results.Add(result);
                }
            }
            finally
            {
                AppServices.EndBatch();
            }

            AiSnapshotWriter.Write();
            Dictionary<string, object> box = NewOutbox();
            box["inbox"] = rawCommands;
            box["results"] = BuildResults(results);
            WriteOutbox(box);
            ClearInbox();
        }

        private static void CompleteSkipped(
            string reason, string raw, List<object> commands)
        {
            Dictionary<string, object> box = NewOutbox();
            box["batchSkipped"] = true;
            box["reason"] = reason;
            if (commands != null)
            {
                box["inbox"] = commands;
            }
            if (raw != null)
            {
                box["inboxText"] = raw.Length > MaxInboxBytes
                    ? raw.Substring(0, MaxInboxBytes)
                    : raw;
            }
            box["results"] = new List<object>();
            WriteOutbox(box);
            ClearInbox();
        }

        private static Dictionary<string, object> NewOutbox()
        {
            Dictionary<string, object> box = new Dictionary<string, object>();
            box["executedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return box;
        }

        private static List<object> ExtractCommands(object parsed)
        {
            IDictionary<string, object> root = parsed as IDictionary<string, object>;
            if (root == null)
            {
                return null;
            }
            object commands;
            if (!root.TryGetValue("commands", out commands))
            {
                return null;
            }
            IList list = commands as IList;
            if (list == null)
            {
                return null;
            }
            List<object> result = new List<object>();
            for (int i = 0; i < list.Count; i++)
            {
                result.Add(list[i]);
            }
            return result;
        }

        private static List<object> BuildResults(List<AiCommandResult> results)
        {
            List<object> list = new List<object>();
            for (int i = 0; i < results.Count; i++)
            {
                AiCommandResult item = results[i];
                Dictionary<string, object> map = new Dictionary<string, object>();
                map["seq"] = item.Seq;
                map["ok"] = item.Ok;
                if (!string.IsNullOrEmpty(item.NewId))
                {
                    map["newId"] = item.NewId;
                }
                if (!string.IsNullOrEmpty(item.Reason))
                {
                    map["reason"] = item.Reason;
                }
                list.Add(map);
            }
            return list;
        }

        private static void WriteOutbox(Dictionary<string, object> box)
        {
            try
            {
                AppPaths.EnsureDataDirectory();
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = MaxJsonChars;
                File.WriteAllText(
                    AppPaths.AiOutboxPath,
                    serializer.Serialize(box),
                    new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                LogWriter.Write("写 AI outbox 失败", ex);
            }
        }

        private static void ClearInbox()
        {
            try
            {
                if (File.Exists(AppPaths.AiInboxPath))
                {
                    File.Delete(AppPaths.AiInboxPath);
                }
            }
            catch (Exception ex)
            {
                LogWriter.Write("清理 AI inbox 失败", ex);
            }
        }

        private static AiCommandResult Execute(AiCommand cmd)
        {
            AiCommandResult result = new AiCommandResult();
            result.Seq = cmd != null ? cmd.Seq : 0;
            if (cmd == null || string.IsNullOrEmpty(cmd.Type) || string.IsNullOrEmpty(cmd.Op))
            {
                result.Reason = "不支持的操作";
                return result;
            }

            if (string.Equals(cmd.Type, "schedule", StringComparison.Ordinal))
            {
                return ExecuteSchedule(cmd);
            }
            if (string.Equals(cmd.Type, "tag", StringComparison.Ordinal))
            {
                return ExecuteTag(cmd);
            }
            result.Reason = "不支持的操作";
            return result;
        }

        private static AiCommandResult ExecuteSchedule(AiCommand cmd)
        {
            AiCommandResult result = new AiCommandResult();
            result.Seq = cmd.Seq;

            if (string.Equals(cmd.Op, "create", StringComparison.Ordinal))
            {
                return CreateSchedule(cmd);
            }
            if (string.Equals(cmd.Op, "update", StringComparison.Ordinal))
            {
                return UpdateSchedule(cmd);
            }
            if (string.Equals(cmd.Op, "delete", StringComparison.Ordinal))
            {
                return DeleteSchedule(cmd);
            }
            if (string.Equals(cmd.Op, "setDone", StringComparison.Ordinal))
            {
                return SetDone(cmd);
            }
            if (string.Equals(cmd.Op, "setTodo", StringComparison.Ordinal))
            {
                return SetTodo(cmd);
            }
            result.Reason = "不支持的操作";
            return result;
        }

        private static AiCommandResult CreateSchedule(AiCommand cmd)
        {
            AiCommandResult result = new AiCommandResult();
            result.Seq = cmd.Seq;
            string error;
            TagTemplate tag;
            if (!TryResolveTag(cmd.TagId, out tag, out error))
            {
                result.Reason = error;
                return result;
            }

            Schedule draft = new Schedule();
            draft.Title = cmd.Title;
            draft.Date = cmd.Date;
            draft.Time = cmd.Time;
            draft.AdvanceMinutes = cmd.AdvanceMinutes.HasValue ? cmd.AdvanceMinutes.Value : 0;
            draft.Note = cmd.Note;
            draft.RemindEnabled = cmd.RemindEnabled.HasValue ? cmd.RemindEnabled.Value : true;
            draft.IsTodo = cmd.IsTodo.HasValue ? cmd.IsTodo.Value : false;
            draft.Recurrence = cmd.Recurrence;

            if (tag != null)
            {
                ScheduleTagService.ApplyToSchedule(draft, tag);
            }
            ApplyExplicitStyle(draft, cmd);
            if (!ScheduleValidator.IsValidColor(draft.BgColor)
                || !ScheduleValidator.IsValidColor(draft.FontColor))
            {
                result.Reason = "颜色格式错误";
                return result;
            }
            if (cmd.Sound != null
                && !ScheduleValidator.IsValidSound(draft.Sound != null ? draft.Sound.Preset : null))
            {
                result.Reason = "声音不存在";
                return result;
            }

            if (draft.AdvanceMinutes > 60)
            {
                draft.AdvanceMinutes = 60;
            }
            DateTime minStart;
            if (!DateUtil.TryParseDateText(draft.Date, out minStart))
            {
                minStart = DateTime.Today;
            }
            error = ValidateDraft(draft, minStart);
            if (error != null)
            {
                result.Reason = error;
                return result;
            }

            SaveOutcome outcome = AppServices.Schedules.Create(draft);
            if (outcome != SaveOutcome.Ok)
            {
                result.Reason = SaveReason(outcome);
                return result;
            }
            result.Ok = true;
            result.NewId = draft.Id;
            return result;
        }

        private static AiCommandResult UpdateSchedule(AiCommand cmd)
        {
            AiCommandResult result = new AiCommandResult();
            result.Seq = cmd.Seq;
            Schedule existing = FindSchedule(cmd.TargetId);
            if (existing == null)
            {
                result.Reason = "目标不存在";
                return result;
            }
            DateTime now = DateTime.Now;
            if (ScheduleService.IsExpired(existing, now))
            {
                result.Reason = "目标已过期";
                return result;
            }

            string error;
            TagTemplate tag;
            if (!TryResolveTag(cmd.TagId, out tag, out error))
            {
                result.Reason = error;
                return result;
            }

            Schedule draft = existing.Clone();
            ApplyFields(draft, cmd, tag, existing);
            if (cmd.Sound != null
                && !ScheduleValidator.IsValidSound(draft.Sound != null ? draft.Sound.Preset : null))
            {
                result.Reason = "声音不存在";
                return result;
            }
            bool activeRecurring = existing.IsRecurring && ScheduleService.HasTakenEffect(existing, now);
            if (activeRecurring)
            {
                // 已生效重复的 date 不落库；给校验一个不小于今天的值，Update 内部仍会置回 existing.Date。
                draft.Date = DateUtil.FormatDate(DateTime.Today);
            }
            if (draft.AdvanceMinutes > 60)
            {
                draft.AdvanceMinutes = 60;
            }

            DateTime minStart = AppServices.Schedules.GetSeriesMinStart(
                existing.SeriesId, DateTime.Today);
            error = ValidateDraft(draft, minStart);
            if (error != null)
            {
                result.Reason = error;
                return result;
            }

            HashSet<string> before = SnapshotIds();
            SaveOutcome outcome = AppServices.Schedules.Update(existing, draft);
            if (outcome != SaveOutcome.Ok)
            {
                result.Reason = SaveReason(outcome);
                return result;
            }
            result.Ok = true;
            result.NewId = FindNewId(before, existing.Id);
            return result;
        }

        private static AiCommandResult DeleteSchedule(AiCommand cmd)
        {
            AiCommandResult result = new AiCommandResult();
            result.Seq = cmd.Seq;
            Schedule target = FindSchedule(cmd.TargetId);
            if (target == null)
            {
                result.Reason = "目标不存在";
                return result;
            }
            if (ScheduleService.IsExpired(target, DateTime.Now))
            {
                result.Reason = "目标已过期";
                return result;
            }
            DeletePlan plan = AppServices.Schedules.PlanDelete(target);
            SaveOutcome outcome = AppServices.Schedules.ExecuteDelete(plan);
            if (outcome != SaveOutcome.Ok)
            {
                result.Reason = SaveReason(outcome);
                return result;
            }
            result.Ok = true;
            return result;
        }

        private static AiCommandResult SetDone(AiCommand cmd)
        {
            AiCommandResult result = new AiCommandResult();
            result.Seq = cmd.Seq;
            Schedule target = FindSchedule(cmd.TargetId);
            if (target == null)
            {
                result.Reason = "目标不存在";
                return result;
            }
            DateTime occurrence;
            if (!DateUtil.TryParseDateText(cmd.OccurrenceDate, out occurrence)
                || !RecurrenceService.IsHit(target, occurrence.Date))
            {
                result.Reason = "发生日期不存在";
                return result;
            }
            if (!cmd.Done.HasValue)
            {
                result.Reason = "缺必填";
                return result;
            }
            SaveOutcome outcome = AppServices.Schedules.SetDone(
                target, occurrence.Date, cmd.Done.Value);
            if (outcome != SaveOutcome.Ok)
            {
                result.Reason = SaveReason(outcome);
                return result;
            }
            result.Ok = true;
            return result;
        }

        private static AiCommandResult SetTodo(AiCommand cmd)
        {
            AiCommandResult result = new AiCommandResult();
            result.Seq = cmd.Seq;
            Schedule target = FindSchedule(cmd.TargetId);
            if (target == null)
            {
                result.Reason = "目标不存在";
                return result;
            }
            if (!cmd.IsTodo.HasValue)
            {
                result.Reason = "缺必填";
                return result;
            }
            SaveOutcome outcome = AppServices.Schedules.SetTodo(target, cmd.IsTodo.Value);
            if (outcome != SaveOutcome.Ok)
            {
                result.Reason = SaveReason(outcome);
                return result;
            }
            result.Ok = true;
            return result;
        }

        private static AiCommandResult ExecuteTag(AiCommand cmd)
        {
            AiCommandResult result = new AiCommandResult();
            result.Seq = cmd.Seq;
            if (string.Equals(cmd.Op, "create", StringComparison.Ordinal))
            {
                string error = TagValidator.ValidateName(cmd.Name);
                if (error != null)
                {
                    result.Reason = error;
                    return result;
                }
                string bg = EmptyToNull(cmd.BgColor);
                string font = EmptyToNull(cmd.FontColor);
                string sound = cmd.Sound != null
                    ? SoundPreset(cmd.Sound)
                    : SoundPreset(AppServices.Settings != null
                        ? AppServices.Settings.SoundFilePath
                        : null);
                if (!ScheduleValidator.IsValidColor(bg)
                    || !ScheduleValidator.IsValidColor(font))
                {
                    result.Reason = "颜色格式错误";
                    return result;
                }
                if (!ScheduleValidator.IsValidSound(sound))
                {
                    result.Reason = "声音不存在";
                    return result;
                }
                TagTemplate tag = new TagTemplate(cmd.Name.Trim(), bg, font, sound);
                tag.Id = IdGenerator.NewTagId();
                List<TagTemplate> candidate = new List<TagTemplate>(AppServices.Tags.All);
                candidate.Add(tag);
                SaveOutcome outcome = AppServices.Tags.TrySave(candidate);
                if (outcome != SaveOutcome.Ok)
                {
                    result.Reason = SaveReason(outcome);
                    return result;
                }
                result.Ok = true;
                result.NewId = tag.Id;
                return result;
            }

            if (string.Equals(cmd.Op, "update", StringComparison.Ordinal))
            {
                TagTemplate target = FindTag(cmd.TargetId);
                if (target == null)
                {
                    result.Reason = "目标不存在";
                    return result;
                }
                TagTemplate edited = target.Clone();
                if (cmd.Name != null)
                {
                    edited.Name = cmd.Name.Trim();
                }
                if (cmd.BgColor != null)
                {
                    edited.BgColor = EmptyToNull(cmd.BgColor);
                }
                if (cmd.FontColor != null)
                {
                    edited.FontColor = EmptyToNull(cmd.FontColor);
                }
                if (cmd.Sound != null)
                {
                    edited.Sound = SoundPreset(cmd.Sound);
                }
                string error = TagValidator.ValidateName(edited.Name);
                if (error == null && !ScheduleValidator.IsValidColor(edited.BgColor))
                {
                    error = "颜色格式错误";
                }
                if (error == null && !ScheduleValidator.IsValidColor(edited.FontColor))
                {
                    error = "颜色格式错误";
                }
                if (error == null && cmd.Sound != null
                    && !ScheduleValidator.IsValidSound(edited.Sound))
                {
                    error = "声音不存在";
                }
                if (error != null)
                {
                    result.Reason = error;
                    return result;
                }
                SaveOutcome pushed = ScheduleTagService.PushToSchedules(edited);
                if (pushed != SaveOutcome.Ok)
                {
                    result.Reason = SaveReason(pushed);
                    return result;
                }
                List<TagTemplate> candidate = new List<TagTemplate>(AppServices.Tags.All);
                int index = candidate.FindIndex(
                    delegate(TagTemplate t) { return t != null && t.Id == target.Id; });
                if (index < 0)
                {
                    result.Reason = "目标不存在";
                    return result;
                }
                candidate[index] = edited;
                SaveOutcome outcome = AppServices.Tags.TrySave(candidate);
                if (outcome != SaveOutcome.Ok)
                {
                    result.Reason = SaveReason(outcome);
                    return result;
                }
                result.Ok = true;
                return result;
            }

            if (string.Equals(cmd.Op, "delete", StringComparison.Ordinal))
            {
                TagTemplate target = FindTag(cmd.TargetId);
                if (target == null)
                {
                    result.Reason = "目标不存在";
                    return result;
                }
                List<TagTemplate> candidate = new List<TagTemplate>(AppServices.Tags.All);
                candidate.Remove(target);
                SaveOutcome outcome = AppServices.Tags.TrySave(candidate);
                if (outcome != SaveOutcome.Ok)
                {
                    result.Reason = SaveReason(outcome);
                    return result;
                }
                result.Ok = true;
                return result;
            }

            result.Reason = "不支持的操作";
            return result;
        }

        private static string ValidateDraft(Schedule draft, DateTime minStart)
        {
            string error = ScheduleValidator.Validate(draft, minStart, true);
            if (error != null)
            {
                return error;
            }
            int advance = draft.RemindEnabled ? draft.AdvanceMinutes : 0;
            if (!ScheduleService.CheckBoundary(draft.DateValue, draft.TimeValue, advance))
            {
                return "当前时间已超过日程提醒或提前提醒";
            }
            return null;
        }

        private static void ApplyFields(
            Schedule draft, AiCommand cmd, TagTemplate tag, Schedule existing)
        {
            if (cmd.Title != null)
            {
                draft.Title = cmd.Title;
            }
            if (cmd.Date != null)
            {
                draft.Date = cmd.Date;
            }
            if (cmd.Time != null)
            {
                draft.Time = cmd.Time;
            }
            if (cmd.AdvanceMinutes.HasValue)
            {
                draft.AdvanceMinutes = cmd.AdvanceMinutes.Value;
            }
            if (cmd.Note != null)
            {
                draft.Note = cmd.Note;
            }
            if (cmd.RemindEnabled.HasValue)
            {
                draft.RemindEnabled = cmd.RemindEnabled.Value;
            }
            if (cmd.IsTodo.HasValue)
            {
                draft.IsTodo = cmd.IsTodo.Value;
            }
            if (cmd.Recurrence != null)
            {
                draft.Recurrence = cmd.Recurrence;
            }
            if (cmd.TagId != null)
            {
                bool changed = !string.Equals(existing.TagId ?? string.Empty,
                    cmd.TagId, StringComparison.Ordinal);
                if (cmd.TagId.Length == 0)
                {
                    draft.TagId = null;
                    draft.Tag = null;
                }
                else if (tag != null)
                {
                    draft.TagId = tag.Id;
                    if (changed)
                    {
                        // tagId 未变时保留日程自己的 Tag 快照，不因模板改名触发分段。
                        draft.Tag = tag.Name;
                        ScheduleTagService.ApplyToSchedule(draft, tag);
                    }
                }
            }
            ApplyExplicitStyle(draft, cmd);
        }

        private static void ApplyExplicitStyle(Schedule draft, AiCommand cmd)
        {
            if (cmd.BgColor != null)
            {
                draft.BgColor = EmptyToNull(cmd.BgColor);
            }
            if (cmd.FontColor != null)
            {
                draft.FontColor = EmptyToNull(cmd.FontColor);
            }
            if (cmd.Sound != null)
            {
                draft.Sound = new ScheduleSound
                {
                    Preset = SoundPreset(cmd.Sound)
                };
            }
        }

        private static bool TryResolveTag(
            string tagId, out TagTemplate tag, out string error)
        {
            tag = null;
            error = null;
            if (tagId == null || tagId.Length == 0)
            {
                return true;
            }
            tag = FindTag(tagId);
            if (tag == null)
            {
                error = "标签不存在";
                return false;
            }
            return true;
        }

        private static TagTemplate FindTag(string id)
        {
            if (string.IsNullOrEmpty(id) || AppServices.Tags == null)
            {
                return null;
            }
            List<TagTemplate> all = AppServices.Tags.All;
            for (int i = 0; i < all.Count; i++)
            {
                TagTemplate tag = all[i];
                if (tag != null && string.Equals(tag.Id, id, StringComparison.Ordinal))
                {
                    return tag;
                }
            }
            return null;
        }

        private static Schedule FindSchedule(string id)
        {
            if (string.IsNullOrEmpty(id) || AppServices.Repository == null)
            {
                return null;
            }
            return AppServices.Repository.FindById(id);
        }

        private static HashSet<string> SnapshotIds()
        {
            HashSet<string> ids = new HashSet<string>();
            List<Schedule> all = AppServices.Repository.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && !string.IsNullOrEmpty(all[i].Id))
                {
                    ids.Add(all[i].Id);
                }
            }
            return ids;
        }

        private static string FindNewId(HashSet<string> before, string oldId)
        {
            List<Schedule> all = AppServices.Repository.All;
            for (int i = 0; i < all.Count; i++)
            {
                Schedule item = all[i];
                if (item != null && !string.IsNullOrEmpty(item.Id)
                    && !before.Contains(item.Id)
                    && !string.Equals(item.Id, oldId, StringComparison.Ordinal))
                {
                    return item.Id;
                }
            }
            return null;
        }

        private static string SoundPreset(object sound)
        {
            if (sound == null)
            {
                return null;
            }
            string text = sound as string;
            if (text != null)
            {
                string textPreset = EmptyToNull(text);
                return textPreset == SoundLibrary.NoneOption ? null : textPreset;
            }
            IDictionary<string, object> map = sound as IDictionary<string, object>;
            object preset;
            if (map != null && map.TryGetValue("preset", out preset))
            {
                return SoundPreset(preset);
            }
            return null;
        }

        private static string EmptyToNull(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value.Trim();
        }

        private static string SaveReason(SaveOutcome outcome)
        {
            if (outcome == SaveOutcome.Blocked)
            {
                return "保存被阻断";
            }
            if (outcome == SaveOutcome.Occupied)
            {
                return "文件被占用";
            }
            if (outcome == SaveOutcome.ReadOnlyOrIoError)
            {
                return "保存失败";
            }
            return null;
        }
    }
}
