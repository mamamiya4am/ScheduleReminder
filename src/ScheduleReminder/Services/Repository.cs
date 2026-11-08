using System;
using System.Collections.Generic;
using System.IO;
using ScheduleReminder.Models;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 日程数据仓储（单一事实源：data\schedules.json）。
    /// 内存中只保存扁平的主记录列表，不做任何展开实例。
    /// </summary>
    public class Repository
    {
        private readonly List<Schedule> _items = new List<Schedule>();

        /// <summary>
        /// 数据损坏保护开关：schedules.json 解析失败时为 true。
        /// 此时**禁止任何自动保存**，否则会用空列表把坏文件覆盖掉（历史数据彻底无法找回）。
        /// </summary>
        private bool _saveBlocked;

        public List<Schedule> All
        {
            get { return _items; }
        }

        /// <summary>是否因数据损坏而暂停保存（供 UI 提示用）。</summary>
        public bool SaveBlocked
        {
            get { return _saveBlocked; }
        }

        /// <summary>
        /// 刷新按钮使用：先探测磁盘；暂时 IO 失败保留旧数据，JSON 损坏才进入恢复。
        /// 成功返回 true；损坏且恢复失败后，内存会清空并阻止保存。
        /// </summary>
        public bool Reload(out string message)
        {
            List<Schedule> probe;
            Exception error;
            if (JsonStore.TryLoadWithRetry(
                    AppPaths.SchedulesPath, out probe, out error))
            {
                NormalizeAll(probe);
                ReplaceItems(probe);
                _saveBlocked = false;
                message = null;
                return true;
            }

            if (JsonStore.IsTransientReadError(error))
            {
                _saveBlocked = true;
                message = "schedules.json 暂时无法读取，已保留当前界面数据。请稍后重试。";
                LogWriter.WriteError(
                    new DataError(DataErrorCode.ReadOccupied, "数据被独占", message),
                    error,
                    new ErrorContext
                    {
                        Operation = "Reload",
                        FilePath = AppPaths.SchedulesPath,
                        Result = false
                    });
                DialogService.Alert(
                    "日程数据暂时无法读取",
                    "schedules.json 当前被占用或无法读取，本次不会自动保存。\r\n"
                    + "请关闭占用程序后点击刷新重试。\r\n\r\n"
                    + "错误代码：" + DataErrorCode.ReadOccupied);
                return false;
            }

            _items.Clear();
            HandleCorruptSchedules();
            message = _saveBlocked ? "日程数据损坏，恢复失败，当前日程已不可用。" : null;
            return !_saveBlocked;
        }

        private void ReplaceItems(List<Schedule> items)
        {
            _items.Clear();
            if (items == null)
            {
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                Schedule item = items[i];
                if (item == null)
                {
                    continue;
                }
                _items.Add(item);
            }
        }

        private static void NormalizeAll(List<Schedule> items)
        {
            if (items == null)
            {
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null)
                {
                    items[i].Normalize();
                }
            }
        }

        public void Load()
        {
            _items.Clear();
            List<Schedule> loaded;
            Exception readError;
            bool ok = JsonStore.TryLoadWithRetry(
                AppPaths.SchedulesPath, out loaded, out readError);
            if (ok)
            {
                NormalizeAll(loaded);
                ReplaceItems(loaded);
                _saveBlocked = false;
                return;
            }

            _saveBlocked = true;
            if (JsonStore.IsTransientReadError(readError))
            {
                string message = "schedules.json 当前被占用或无法读取，本次不会自动保存。\r\n"
                    + "请关闭占用程序后重启或点击刷新重试。";
                LogWriter.WriteError(
                    new DataError(DataErrorCode.ReadOccupied, "数据被独占", message),
                    readError,
                    new ErrorContext
                    {
                        Operation = "Load",
                        FilePath = AppPaths.SchedulesPath,
                        Result = false
                    });
                DialogService.Alert(
                    "日程数据暂时无法读取",
                    message + "\r\n\r\n错误代码：" + DataErrorCode.ReadOccupied);
                return;
            }

            HandleCorruptSchedules();
        }

        private void HandleCorruptSchedules()
        {
            string sourcePath = AppPaths.SchedulesPath;
            RecoveryService.ArchiveCorruptCopy(sourcePath);

            List<Schedule> backup;
            if (RecoveryService.TryValidateBackup(sourcePath, out backup))
            {
                SaveOutcome replaced = RecoveryService.ReplaceWithBackup(sourcePath);
                if (replaced == SaveOutcome.Ok)
                {
                    NormalizeAll(backup);
                    ReplaceItems(backup);
                    _saveBlocked = false;
                    LogWriter.WriteError(
                        new DataError(DataErrorCode.RecoveryReplaced,
                            "数据检测到损坏", "已通过上次备份恢复"),
                        null,
                        new ErrorContext
                        {
                            Operation = "Recover",
                            FilePath = sourcePath,
                            BackupPath = RecoveryService.FixedBackupPath(sourcePath),
                            Result = true,
                            RestoredCount = _items.Count
                        });
                    DialogService.Alert(
                        "数据检测到损坏",
                        "schedules.json 数据检测到损坏，已通过上次备份恢复。\r\n"
                        + "损坏数据已保存至「" + AppPaths.JsonCorruptDirectory + "」文件夹下。\r\n\r\n"
                        + "错误代码：" + DataErrorCode.RecoveryReplaced);
                    return;
                }

                RecoveryService.DeletePrimary(sourcePath);
                _saveBlocked = true;
                RecoveryService.OpenCorruptFolder();
                LogWriter.WriteError(
                    new DataError(DataErrorCode.RecoveryFailed,
                        "恢复失败", "备份恢复失败"),
                    null,
                    new ErrorContext
                    {
                        Operation = "Recover",
                        FilePath = sourcePath,
                        BackupPath = RecoveryService.FixedBackupPath(sourcePath),
                        Result = false
                    });
                DialogService.Alert(
                    "恢复失败",
                    "备份文件或数据文件恢复失败。\r\n"
                    + "保存功能暂时关闭，请排除问题后点击「刷新」恢复功能。\r\n\r\n"
                    + "错误代码：" + DataErrorCode.RecoveryFailed);
                return;
            }

            bool delete = DialogService.Confirm(
                "数据检测到损坏",
                "schedules.json 数据检测到损坏且无备份，是否删除损坏数据？\r\n"
                + "损坏数据已保存至「" + AppPaths.JsonCorruptDirectory + "」文件夹下。\r\n\r\n"
                + "错误代码：" + DataErrorCode.BackupUnavailable,
                "确定",
                true);

            if (delete)
            {
                RecoveryService.DeletePrimary(sourcePath);
            }

            _saveBlocked = false;
            RecoveryService.OpenCorruptFolder();
            LogWriter.WriteError(
                new DataError(DataErrorCode.BackupUnavailable,
                    "数据检测到损坏", "无可用备份"),
                null,
                new ErrorContext
                {
                    Operation = "Recover",
                    FilePath = sourcePath,
                    BackupPath = RecoveryService.FixedBackupPath(sourcePath),
                    Decision = delete ? "Delete" : "Keep",
                    Result = true
                });
        }

        public List<Schedule> SnapshotShallow()
        {
            return new List<Schedule>(_items);
        }

        /// <summary>候选保存：只有写盘成功才替换内存。</summary>
        public SaveOutcome TrySave(List<Schedule> candidate)
        {
            if (_saveBlocked)
            {
                LogWriter.WriteError(
                    new DataError(DataErrorCode.SaveBlocked, "无法保存", "数据保护状态"),
                    null,
                    new ErrorContext
                    {
                        Operation = "Save",
                        FilePath = AppPaths.SchedulesPath,
                        Result = false
                    });
                return SaveOutcome.Blocked;
            }
            try
            {
                JsonStore.Save(AppPaths.SchedulesPath, candidate);
                ReplaceItems(candidate);
                return SaveOutcome.Ok;
            }
            catch (IOException ex)
            {
                SaveOutcome outcome = FileErrorClassifier.IsSharingViolation(ex)
                    ? SaveOutcome.Occupied
                    : SaveOutcome.ReadOnlyOrIoError;
                LogSaveError(ex, outcome);
                return outcome;
            }
            catch (UnauthorizedAccessException ex)
            {
                LogSaveError(ex, SaveOutcome.ReadOnlyOrIoError);
                return SaveOutcome.ReadOnlyOrIoError;
            }
            catch (Exception ex)
            {
                LogSaveError(ex, SaveOutcome.ReadOnlyOrIoError);
                return SaveOutcome.ReadOnlyOrIoError;
            }
        }

        private void LogSaveError(Exception ex, SaveOutcome outcome)
        {
            LogWriter.WriteError(
                new DataError(
                    outcome == SaveOutcome.Occupied
                        ? DataErrorCode.SaveOccupied
                        : DataErrorCode.SaveReadOnly,
                    "保存失败",
                    "schedules.json 写入失败"),
                ex,
                new ErrorContext
                {
                    Operation = "Save",
                    FilePath = AppPaths.SchedulesPath,
                    Result = false
                });
        }

        public void Add(Schedule schedule)
        {
            if (schedule == null)
            {
                return;
            }
            _items.Add(schedule);
        }

        public void Remove(Schedule schedule)
        {
            if (schedule == null)
            {
                return;
            }
            _items.Remove(schedule);
        }

        public void RemoveRange(IEnumerable<Schedule> schedules)
        {
            if (schedules == null)
            {
                return;
            }
            List<Schedule> list = new List<Schedule>(schedules);
            for (int i = 0; i < list.Count; i++)
            {
                _items.Remove(list[i]);
            }
        }

        public Schedule FindById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Id == id)
                {
                    return _items[i];
                }
            }
            return null;
        }

        public List<Schedule> FindBySeries(string seriesId)
        {
            List<Schedule> result = new List<Schedule>();
            if (string.IsNullOrEmpty(seriesId))
            {
                return result;
            }
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].SeriesId == seriesId)
                {
                    result.Add(_items[i]);
                }
            }
            return result;
        }
    }
}
