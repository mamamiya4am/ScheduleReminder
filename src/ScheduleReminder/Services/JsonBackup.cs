using System;
using System.Collections.Generic;
using System.IO;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// data 根目录 JSON 的滚动备份：
    /// 固定备份 1 份 + 时间戳历史 3 份。
    ///
    /// 每次正式文件保存成功后调用：
    /// 1. 旧 XXX.json.bak 改名为时间戳历史；
    /// 2. 历史超过 3 份时删除最旧；
    /// 3. 当前正式文件原样复制为新的 XXX.json.bak。
    ///
    /// 备份失败只写日志，不阻断已经成功的正式保存。
    /// </summary>
    public static class JsonBackup
    {
        private const int HistoryLimit = 3;

        public static void Capture(string sourcePath)
        {
            try
            {
                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                {
                    return;
                }

                string fullPath = Path.GetFullPath(sourcePath);
                string dataDir = Path.GetFullPath(AppPaths.DataDirectory);
                string sourceDir = Path.GetDirectoryName(fullPath);
                if (!string.Equals(sourceDir, dataDir, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string fileName = Path.GetFileName(fullPath);
                string stem = Path.GetFileNameWithoutExtension(fullPath);
                string backupDir = Path.Combine(AppPaths.DataDirectory, "backup", "json", stem);
                Directory.CreateDirectory(backupDir);

                string fixedPath = Path.Combine(backupDir, fileName + ".bak");
                if (File.Exists(fixedPath))
                {
                    File.Move(fixedPath, CreateHistoryPath(backupDir, stem));
                }

                DeleteOldHistory(backupDir, stem);
                File.Copy(fullPath, fixedPath, true);
            }
            catch (Exception ex)
            {
                LogWriter.Write("JSON 备份失败：" + sourcePath, ex);
            }
        }

        private static string CreateHistoryPath(string backupDir, string stem)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string path = Path.Combine(backupDir, stem + "_" + stamp + ".json.bak");
            if (!File.Exists(path))
            {
                return path;
            }

            for (int i = 1; i < 1000; i++)
            {
                path = Path.Combine(backupDir,
                    stem + "_" + stamp + "_" + i.ToString("00") + ".json.bak");
                if (!File.Exists(path))
                {
                    return path;
                }
            }
            throw new IOException("无法为备份生成唯一文件名：" + stem);
        }

        private static void DeleteOldHistory(string backupDir, string stem)
        {
            string[] files = Directory.GetFiles(
                backupDir, stem + "_*.json.bak", SearchOption.TopDirectoryOnly);
            if (files.Length <= HistoryLimit)
            {
                return;
            }

            Array.Sort(files, StringComparer.Ordinal);
            int removeCount = files.Length - HistoryLimit;
            for (int i = 0; i < removeCount; i++)
            {
                try
                {
                    File.Delete(files[i]);
                }
                catch (Exception ex)
                {
                    LogWriter.Write("删除旧 JSON 备份失败：" + files[i], ex);
                }
            }
        }
    }
}
