using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using ScheduleReminder.Tools.WorldClock;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 小工具自己的数据文件 data\tools.json（与 schedules.json / settings.json 互不干扰）。
    /// 目前只有世界时钟用到：城市列表（含顺序）是唯一需要持久化的小工具数据。
    /// </summary>
    [DataContract]
    public sealed class ToolsData
    {
        /// <summary>已添加城市名数组；城市名即标识，不存时区、偏移与国家名。</summary>
        [DataMember(Name = "worldClock", EmitDefaultValue = false)]
        public List<string> WorldClock { get; set; }
    }

    /// <summary>
    /// 世界时钟城市列表的读写。落盘只写城市名，读取时过滤掉城市库里已不存在或重复的名称。
    /// 语义与原型一致：文件不存在用默认四城；存在但为空数组则保持为空，不再回落默认值。
    /// </summary>
    public static class ToolsDataStore
    {
        private static string FilePath
        {
            get { return Path.Combine(AppPaths.DataDirectory, "tools.json"); }
        }

        /// <summary>
        /// 读取城市名列表。返回 null 表示「文件不存在或 worldClock 字段缺失」，调用方应改用默认四城；
        /// 返回空列表表示用户确实清空了列表，必须保持为空。
        /// </summary>
        public static List<string> LoadCities()
        {
            ToolsData data;
            Exception readError;
            if (!JsonStore.TryLoadWithRetry<ToolsData>(FilePath, out data, out readError))
            {
                if (JsonStore.IsTransientReadError(readError))
                {
                    LogWriter.WriteError(
                        new DataError(DataErrorCode.LiteReadOccupied,
                            "数据被独占", "tools.json 暂时无法读取"),
                        readError,
                        new ErrorContext
                        {
                            Operation = "Load",
                            FilePath = FilePath,
                            Result = false
                        });
                    DialogService.Alert(
                        "工具数据被独占",
                        "tools.json 暂时无法读取，已使用默认城市。\r\n\r\n"
                        + "错误代码：" + DataErrorCode.LiteReadOccupied);
                    data = new ToolsData();
                }
                else
                {
                    ToolsData backup;
                    if (RecoveryService.TryValidateBackupObject(FilePath, out backup))
                    {
                        SaveOutcome outcome = RecoveryService.ReplaceWithBackup(FilePath);
                        if (outcome == SaveOutcome.Ok)
                        {
                            data = backup;
                            LogWriter.WriteError(
                                new DataError(DataErrorCode.LiteRecovered,
                                    "数据检测到损坏", "tools.json 已通过备份恢复"),
                                null,
                                new ErrorContext
                                {
                                    Operation = "Recover",
                                    FilePath = FilePath,
                                    BackupPath = RecoveryService.FixedBackupPath(FilePath),
                                    Result = true
                                });
                            DialogService.Alert(
                                "工具数据检测到损坏",
                                "tools.json 数据检测到损坏，已通过上次备份恢复。\r\n\r\n"
                                + "错误代码：" + DataErrorCode.LiteRecovered);
                        }
                        else
                        {
                            data = new ToolsData();
                            LogWriter.WriteError(
                                new DataError(DataErrorCode.LiteRecoveryFailed,
                                    "工具数据恢复失败", "tools.json 恢复失败"),
                                null,
                                new ErrorContext
                                {
                                    Operation = "Recover",
                                    FilePath = FilePath,
                                    BackupPath = RecoveryService.FixedBackupPath(FilePath),
                                    Result = false
                                });
                            DialogService.Alert(
                                "工具数据检测到损坏",
                                "tools.json 数据检测到损坏，备份恢复失败，已恢复默认数据。\r\n\r\n"
                                + "错误代码：" + DataErrorCode.LiteRecoveryFailed);
                        }
                    }
                    else
                    {
                        data = new ToolsData();
                        LogWriter.WriteError(
                            new DataError(DataErrorCode.LiteBackupUnavailable,
                                "工具数据检测到损坏", "tools.json 备份不可用"),
                            null,
                            new ErrorContext
                            {
                                Operation = "Recover",
                                FilePath = FilePath,
                                BackupPath = RecoveryService.FixedBackupPath(FilePath),
                                Result = false
                            });
                        DialogService.Alert(
                            "工具数据检测到损坏",
                            "tools.json 数据检测到损坏，备份不可用，已恢复默认数据。\r\n\r\n"
                            + "错误代码：" + DataErrorCode.LiteBackupUnavailable);
                    }
                }
            }
            if (data == null || data.WorldClock == null)
            {
                return null;
            }

            List<string> result = new List<string>();
            for (int i = 0; i < data.WorldClock.Count; i++)
            {
                string name = data.WorldClock[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (WorldClockCatalog.FindByName(name) == null)
                {
                    continue;
                }
                if (result.Contains(name))
                {
                    continue;
                }
                result.Add(name);
            }
            return result;
        }

        /// <summary>立即写入城市名列表（添加、删除、调序都即时落盘）。</summary>
        public static void SaveCities(IList<string> names)
        {
            if (!AppPaths.EnsureDataDirectory())
            {
                return;
            }
            try
            {
                ToolsData data = new ToolsData();
                data.WorldClock = names == null
                    ? new List<string>()
                    : new List<string>(names);
                JsonStore.Save(FilePath, data);
            }
            catch (Exception ex)
            {
                LogWriter.WriteError(
                    new DataError(DataErrorCode.SaveReadOnly,
                        "保存失败", "tools.json 写入失败"),
                    ex,
                    new ErrorContext
                    {
                        Operation = "Save",
                        FilePath = FilePath,
                        Result = false
                    });
            }
        }
    }
}
