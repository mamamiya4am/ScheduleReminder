using System;
using ScheduleReminder.Models;

namespace ScheduleReminder.Services
{
    /// <summary>设置读写（data\settings.json）。</summary>
    public static class SettingsStore
    {
        public static AppSettings Load()
        {
            AppSettings settings;
            Exception readError;
            if (!JsonStore.TryLoadWithRetry(
                    AppPaths.SettingsPath, out settings, out readError))
            {
                if (JsonStore.IsTransientReadError(readError))
                {
                    LogWriter.WriteError(
                        new DataError(DataErrorCode.LiteReadOccupied,
                            "数据被独占", "settings.json 暂时无法读取"),
                        readError,
                        new ErrorContext
                        {
                            Operation = "Load",
                            FilePath = AppPaths.SettingsPath,
                            Result = false
                        });
                    DialogService.Alert(
                        "设置数据被独占",
                        "settings.json 暂时无法读取，已使用默认设置。\r\n\r\n"
                        + "错误代码：" + DataErrorCode.LiteReadOccupied);
                    settings = new AppSettings();
                }
                else
                {
                    AppSettings backup;
                    if (RecoveryService.TryValidateBackupObject(AppPaths.SettingsPath, out backup))
                    {
                        SaveOutcome outcome = RecoveryService.ReplaceWithBackup(
                            AppPaths.SettingsPath);
                        if (outcome == SaveOutcome.Ok)
                        {
                            settings = backup;
                            LogWriter.WriteError(
                                new DataError(DataErrorCode.LiteRecovered,
                                    "数据检测到损坏", "settings.json 已通过备份恢复"),
                                null,
                                new ErrorContext
                                {
                                    Operation = "Recover",
                                    FilePath = AppPaths.SettingsPath,
                                    BackupPath = RecoveryService.FixedBackupPath(
                                        AppPaths.SettingsPath),
                                    Result = true
                                });
                            DialogService.Alert(
                                "设置数据检测到损坏",
                                "settings.json 数据检测到损坏，已通过上次备份恢复。\r\n\r\n"
                                + "错误代码：" + DataErrorCode.LiteRecovered);
                        }
                        else
                        {
                            settings = new AppSettings();
                            LogWriter.WriteError(
                                new DataError(DataErrorCode.LiteRecoveryFailed,
                                    "设置数据恢复失败", "settings.json 恢复失败"),
                                null,
                                new ErrorContext
                                {
                                    Operation = "Recover",
                                    FilePath = AppPaths.SettingsPath,
                                    BackupPath = RecoveryService.FixedBackupPath(
                                        AppPaths.SettingsPath),
                                    Result = false
                                });
                            DialogService.Alert(
                                "设置数据检测到损坏",
                                "settings.json 数据检测到损坏，备份恢复失败，已恢复默认数据。\r\n\r\n"
                                + "错误代码：" + DataErrorCode.LiteRecoveryFailed);
                        }
                    }
                    else
                    {
                        settings = new AppSettings();
                        LogWriter.WriteError(
                            new DataError(DataErrorCode.LiteBackupUnavailable,
                                "设置数据检测到损坏", "settings.json 备份不可用"),
                            null,
                            new ErrorContext
                            {
                                Operation = "Recover",
                                FilePath = AppPaths.SettingsPath,
                                BackupPath = RecoveryService.FixedBackupPath(
                                    AppPaths.SettingsPath),
                                Result = false
                            });
                        DialogService.Alert(
                            "设置数据检测到损坏",
                            "settings.json 数据检测到损坏，备份不可用，已恢复默认数据。\r\n\r\n"
                            + "错误代码：" + DataErrorCode.LiteBackupUnavailable);
                    }
                }
            }
            if (settings == null)
            {
                settings = new AppSettings();
            }
            settings.Normalize();
            return settings;
        }

        public static bool Save(AppSettings settings)
        {
            try
            {
                JsonStore.Save(AppPaths.SettingsPath, settings);
                return true;
            }
            catch (Exception ex)
            {
                LogWriter.WriteError(
                    new DataError(DataErrorCode.SaveReadOnly,
                        "保存失败", "settings.json 写入失败"),
                    ex,
                    new ErrorContext
                    {
                        Operation = "Save",
                        FilePath = AppPaths.SettingsPath,
                        Result = false
                    });
                return false;
            }
        }
    }
}
