using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ScheduleReminder.Services
{
    /// <summary>数据文件损坏恢复：归档、校验、替换、删除、打开目录。</summary>
    public static class RecoveryService
    {
        public static string FixedBackupPath(string sourcePath)
        {
            string file = Path.GetFileName(sourcePath);
            string stem = Path.GetFileNameWithoutExtension(sourcePath);
            return Path.Combine(AppPaths.JsonBackupDirectory, stem, file + ".bak");
        }

        /// <summary>复制损坏文件到 corrupt；失败返回 null，不阻断恢复。</summary>
        public static string ArchiveCorruptCopy(string sourcePath)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.JsonCorruptDirectory);
                string name = Path.GetFileName(sourcePath);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string target = Path.Combine(
                    AppPaths.JsonCorruptDirectory,
                    name + "." + stamp + ".corrupt");
                int index = 1;
                while (File.Exists(target))
                {
                    target = Path.Combine(
                        AppPaths.JsonCorruptDirectory,
                        name + "." + stamp + "_" + index.ToString("00") + ".corrupt");
                    index++;
                }
                File.Copy(sourcePath, target, false);
                return target;
            }
            catch (Exception ex)
            {
                LogWriter.Write("归档损坏 JSON 失败：" + sourcePath, ex);
                return null;
            }
        }

        public static bool TryValidateBackup<T>(string sourcePath, out List<T> items)
            where T : class, new()
        {
            items = null;
            string backupPath = FixedBackupPath(sourcePath);
            try
            {
                if (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
                {
                    return false;
                }
                return JsonStore.TryLoad(backupPath, out items) && items != null;
            }
            catch (Exception ex)
            {
                LogWriter.Write("校验固定备份失败：" + backupPath, ex);
                return false;
            }
        }

        public static bool TryValidateBackupObject<T>(string sourcePath, out T value)
            where T : class, new()
        {
            value = null;
            string backupPath = FixedBackupPath(sourcePath);
            try
            {
                if (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
                {
                    return false;
                }
                return JsonStore.TryLoad(backupPath, out value) && value != null;
            }
            catch (Exception ex)
            {
                LogWriter.Write("校验固定备份失败：" + backupPath, ex);
                return false;
            }
        }

        public static SaveOutcome ReplaceWithBackup(string sourcePath)
        {
            try
            {
                File.Copy(FixedBackupPath(sourcePath), sourcePath, true);
                return SaveOutcome.Ok;
            }
            catch (IOException ex)
            {
                LogWriter.Write("替换主文件失败：" + sourcePath, ex);
                return FileErrorClassifier.IsSharingViolation(ex)
                    ? SaveOutcome.Occupied
                    : SaveOutcome.ReadOnlyOrIoError;
            }
            catch (Exception ex)
            {
                LogWriter.Write("替换主文件失败：" + sourcePath, ex);
                return SaveOutcome.ReadOnlyOrIoError;
            }
        }

        public static bool DeletePrimary(string sourcePath)
        {
            try
            {
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                LogWriter.Write("删除主文件失败：" + sourcePath, ex);
                return false;
            }
        }

        public static void OpenCorruptFolder()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.JsonCorruptDirectory);
                Process.Start("explorer.exe", "\"" + AppPaths.JsonCorruptDirectory + "\"");
            }
            catch (Exception ex)
            {
                LogWriter.Write("打开 corrupt 目录失败", ex);
            }
        }

    }
}
