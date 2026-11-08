using System;
using System.IO;
using System.Reflection;
using System.Text;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>崩溃 / 异常日志（写入 data\logs\crash.log）。保证「静默消失」可追溯。</summary>
    public static class LogWriter
    {
        private static readonly object SyncRoot = new object();

        public static void Write(string message)
        {
            Write(message, null);
        }

        public static void Write(Exception ex)
        {
            Write("未处理异常", ex);
        }

        public static void Write(string message, Exception ex)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("[").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
                sb.Append(message);
                if (ex != null)
                {
                    sb.Append(Environment.NewLine).Append(ex);
                }
                sb.Append(Environment.NewLine);
                AppendToLog(sb.ToString());
            }
            catch (Exception)
            {
                // 日志本身失败时绝不再次抛出（避免二次崩溃）
            }
        }

        /// <summary>数据链路的统一日志入口：错误码、操作、文件、结果和技术细节一次写入。</summary>
        public static void WriteError(DataError error, Exception ex, ErrorContext context)
        {
            try
            {
                if (error == null)
                {
                    return;
                }
                context = context ?? new ErrorContext();
                StringBuilder sb = new StringBuilder();
                sb.Append("[").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append("] [").Append(ResolveLevel(error.Code)).Append("]")
                    .Append(Environment.NewLine);

                AppendField(sb, "code", error.Code);
                AppendField(sb, "title", error.Title);
                AppendField(sb, "detail", error.UserMessage);
                AppendField(sb, "op", context.Operation);
                AppendField(sb, "file", context.FilePath);
                AppendField(sb, "backup", context.BackupPath);
                AppendField(sb, "decision", context.Decision);
                AppendField(sb, "restored", context.RestoredCount.HasValue
                    ? context.RestoredCount.Value.ToString()
                    : null);
                AppendField(sb, "result", context.Result ? "Ok" : "Failed");
                AppendField(sb, "version", string.IsNullOrEmpty(context.AppVersion)
                    ? GetAppVersion()
                    : context.AppVersion);

                if (ex != null)
                {
                    AppendField(sb, "exc", ex.GetType().Name);
                    AppendField(sb, "hresult", "0x" + ex.HResult.ToString("X8"));
                    AppendField(sb, "message", OneLine(ex.Message));
                    sb.Append("stack=").Append(ex).Append(Environment.NewLine);
                }

                AppendToLog(sb.ToString());
            }
            catch (Exception)
            {
                // 日志本身失败时绝不再次抛出
            }
        }

        private static string ResolveLevel(string code)
        {
            if (string.Equals(code, DataErrorCode.ReadOccupied, StringComparison.Ordinal)
                || string.Equals(code, DataErrorCode.LiteReadOccupied, StringComparison.Ordinal))
            {
                return "WARN";
            }
            if (string.Equals(code, DataErrorCode.RecoveryReplaced, StringComparison.Ordinal)
                || string.Equals(code, DataErrorCode.LiteRecovered, StringComparison.Ordinal))
            {
                return "INFO";
            }
            return "ERROR";
        }

        private static string GetAppVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static void AppendField(StringBuilder sb, string name, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            sb.Append(name).Append("=").Append(OneLine(value)).Append(Environment.NewLine);
        }

        private static string OneLine(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value.Replace("\r", " ").Replace("\n", " ");
        }

        private static void AppendToLog(string text)
        {
            lock (SyncRoot)
            {
                string dir = Path.GetDirectoryName(AppPaths.LogPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                RotateIfNeeded();
                File.AppendAllText(AppPaths.LogPath, text, Encoding.UTF8);
            }
        }

        private const long MaxLogBytes = 2 * 1024 * 1024;
        private const int MaxHistoryFiles = 30;

        /// <summary>当前日志达到 2 MB 后改名归档，并只保留最近 30 个历史日志。</summary>
        private static void RotateIfNeeded()
        {
            try
            {
                FileInfo current = new FileInfo(AppPaths.LogPath);
                if (!current.Exists || current.Length < MaxLogBytes)
                {
                    return;
                }

                string directory = AppPaths.LogDirectory;
                string history = Path.Combine(
                    directory,
                    "crash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".log");
                for (int i = 1; File.Exists(history) && i < 1000; i++)
                {
                    history = Path.Combine(
                        directory,
                        "crash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
                        + "_" + i.ToString("00") + ".log");
                }
                File.Move(AppPaths.LogPath, history);
                DeleteOldHistory(directory);
            }
            catch
            {
                // 轮转失败不能影响日志写入
            }
        }

        private static void DeleteOldHistory(string directory)
        {
            string[] files = Directory.GetFiles(directory, "crash_*.log");
            if (files.Length <= MaxHistoryFiles)
            {
                return;
            }

            Array.Sort(files, StringComparer.Ordinal);
            int removeCount = files.Length - MaxHistoryFiles;
            for (int i = 0; i < removeCount; i++)
            {
                try
                {
                    File.Delete(files[i]);
                }
                catch
                {
                    // 删除失败不阻断日志写入
                }
            }
        }
    }
}
