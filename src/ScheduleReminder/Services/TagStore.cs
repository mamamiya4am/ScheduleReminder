using System;
using System.Collections.Generic;
using System.IO;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 标签模板仓储（唯一事实源：data\tags.json）。
    /// 裸数组，不包含版本包装；不做旧 settings.tagTemplates 的运行时迁移。
    /// </summary>
    public class TagStore
    {
        private readonly List<TagTemplate> _items = new List<TagTemplate>();

        /// <summary>由于文件损坏而暂停自动保存，避免空列表覆盖原数据。</summary>
        public bool SaveBlocked { get; private set; }

        public List<TagTemplate> All
        {
            get { return _items; }
        }

        /// <summary>
        /// 刷新按钮使用：暂时读取失败保留旧标签；JSON 损坏才进入恢复。
        /// </summary>
        public bool Reload(out string message)
        {
            List<TagTemplate> probe;
            Exception error;
            if (JsonStore.TryLoadWithRetry(
                    AppPaths.TagsPath, out probe, out error))
            {
                _items.Clear();
                AddLoadedRange(probe);
                SaveBlocked = false;
                message = null;
                return true;
            }

            if (JsonStore.IsTransientReadError(error))
            {
                SaveBlocked = true;
                message = "tags.json 暂时无法读取，已保留当前标签数据。请稍后重试。";
                LogWriter.WriteError(
                    new DataError(DataErrorCode.ReadOccupied, "数据被独占", message),
                    error,
                    new ErrorContext
                    {
                        Operation = "Reload",
                        FilePath = AppPaths.TagsPath,
                        Result = false
                    });
                DialogService.Alert(
                    "标签数据暂时无法读取",
                    "tags.json 当前被占用或无法读取，本次不会自动保存。\r\n"
                    + "请关闭占用程序后点击刷新重试。\r\n\r\n"
                    + "错误代码：" + DataErrorCode.ReadOccupied);
                return false;
            }

            _items.Clear();
            HandleCorruptTags();
            message = SaveBlocked ? "标签数据损坏，恢复失败，当前标签已不可用。" : null;
            return !SaveBlocked;
        }

        public void Load()
        {
            _items.Clear();
            List<TagTemplate> loaded;
            Exception readError;
            bool ok = JsonStore.TryLoadWithRetry(
                AppPaths.TagsPath, out loaded, out readError);
            if (ok)
            {
                AddLoadedRange(loaded);
                SaveBlocked = false;
                return;
            }

            SaveBlocked = true;
            if (JsonStore.IsTransientReadError(readError))
            {
                string message = "tags.json 当前被占用或无法读取，本次不会自动保存。\r\n"
                    + "请关闭占用程序后重启或点击刷新重试。";
                LogWriter.WriteError(
                    new DataError(DataErrorCode.ReadOccupied, "数据被独占", message),
                    readError,
                    new ErrorContext
                    {
                        Operation = "Load",
                        FilePath = AppPaths.TagsPath,
                        Result = false
                    });
                DialogService.Alert(
                    "标签数据暂时无法读取",
                    message + "\r\n\r\n错误代码：" + DataErrorCode.ReadOccupied);
                return;
            }

            HandleCorruptTags();
        }

        private void HandleCorruptTags()
        {
            string sourcePath = AppPaths.TagsPath;
            RecoveryService.ArchiveCorruptCopy(sourcePath);

            List<TagTemplate> backup;
            if (RecoveryService.TryValidateBackup(sourcePath, out backup))
            {
                SaveOutcome replaced = RecoveryService.ReplaceWithBackup(sourcePath);
                if (replaced == SaveOutcome.Ok)
                {
                    _items.Clear();
                    AddLoadedRange(backup);
                    SaveBlocked = false;
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
                        "tags.json 数据检测到损坏，已通过上次备份恢复。\r\n"
                        + "损坏数据已保存至「" + AppPaths.JsonCorruptDirectory + "」文件夹下。\r\n\r\n"
                        + "错误代码：" + DataErrorCode.RecoveryReplaced);
                    return;
                }

                RecoveryService.DeletePrimary(sourcePath);
                SaveBlocked = true;
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
                "tags.json 数据检测到损坏且无备份，是否删除损坏数据？\r\n"
                + "损坏数据已保存至「" + AppPaths.JsonCorruptDirectory + "」文件夹下。\r\n\r\n"
                + "错误代码：" + DataErrorCode.BackupUnavailable,
                "确定",
                true);

            if (delete)
            {
                RecoveryService.DeletePrimary(sourcePath);
            }

            SaveBlocked = false;
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

        private void AddLoadedRange(List<TagTemplate> loaded)
        {
            if (loaded == null)
            {
                return;
            }

            for (int i = 0; i < loaded.Count; i++)
            {
                TagTemplate item = loaded[i];
                if (item == null)
                {
                    continue;
                }
                if (item.Name == null)
                {
                    item.Name = string.Empty;
                }
                _items.Add(item);
            }
        }

        public SaveOutcome TrySave(List<TagTemplate> candidate)
        {
            if (SaveBlocked)
            {
                LogWriter.WriteError(
                    new DataError(DataErrorCode.SaveBlocked, "无法保存", "数据保护状态"),
                    null,
                    new ErrorContext
                    {
                        Operation = "Save",
                        FilePath = AppPaths.TagsPath,
                        Result = false
                    });
                return SaveOutcome.Blocked;
            }
            try
            {
                JsonStore.Save(AppPaths.TagsPath, candidate);
                _items.Clear();
                AddLoadedRange(candidate);
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
                    "tags.json 写入失败"),
                ex,
                new ErrorContext
                {
                    Operation = "Save",
                    FilePath = AppPaths.TagsPath,
                    Result = false
                });
        }

    }
}
