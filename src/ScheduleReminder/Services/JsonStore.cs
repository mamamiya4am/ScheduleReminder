using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// JSON 读写（BCL 自带 DataContractJsonSerializer，零 NuGet 依赖）。
    /// 写入采用「临时文件 + 覆盖」的原子写，避免写一半断电导致数据损坏。
    /// </summary>
    public static class JsonStore
    {
        /// <summary>
        /// 带「成功 / 失败」标志的读取。
        /// 注意：为什么要区分——旧实现读失败就返回空对象，调用方照常保存 →
        ///    坏文件会被**静默覆盖成空数据**，日程全部丢失且无法找回。
        ///    现在调用方（Repository / SettingsStore）拿到 ok=false 后必须先备份坏文件、阻断自动保存。
        /// </summary>
        public static bool TryLoad<T>(string path, out T value) where T : class, new()
        {
            Exception ignored;
            return TryLoad(path, out value, out ignored);
        }

        /// <summary>
        /// 带异常信息的读取：用于刷新时区分“文件暂时读不到”和“JSON 已损坏”。
        /// 文件不存在或 0 字节仍按首次运行处理。
        /// </summary>
        public static bool TryLoad<T>(string path, out T value, out Exception error)
            where T : class, new()
        {
            value = new T();
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    // 文件不存在 = 首次运行，不算失败
                    return true;
                }
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length == 0)
                    {
                        return true;
                    }
                    SkipUtf8Bom(stream);

                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                    T result = serializer.ReadObject(stream) as T;
                    value = result ?? new T();
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex;
                LogWriter.Write("读取 JSON 失败（文件可能损坏或暂不可读）：" + path, ex);
                return false;
            }
        }

        /// <summary>IO/权限/占用属于暂时读取失败；序列化异常才按数据损坏处理。</summary>
        public static bool IsTransientReadError(Exception error)
        {
            return error is IOException
                || error is UnauthorizedAccessException
                || error is NotSupportedException;
        }

        /// <summary>暂时读取失败时等待 300ms 再试一次；损坏不重试。</summary>
        public static bool TryLoadWithRetry<T>(string path, out T value, out Exception error)
            where T : class, new()
        {
            if (TryLoad(path, out value, out error))
            {
                return true;
            }
            if (!IsTransientReadError(error))
            {
                return false;
            }
            System.Threading.Thread.Sleep(300);
            return TryLoad(path, out value, out error);
        }

        /// <summary>
        /// 读取「简单字典格式」的 JSON：{ "key": "value", ... }，即 holidays.json 的格式。
        ///
        /// 注意（关键陷阱）：DataContractJsonSerializer 默认把 Dictionary 序列化 / 反序列化为
        ///    数组形式 [{"Key":"k","Value":"v"}]，与手写的 {"k":"v"} 不兼容。
        ///    不启用 UseSimpleDictionaryFormat 时，它**不抛异常、只静默返回空字典**，
        ///    曾导致「节假日全程失效但 crash.log 干净」——典型的静默失败。
        /// </summary>
        public static bool TryLoadStringDictionary(
            string path, out Dictionary<string, string> value, out Exception error)
        {
            value = new Dictionary<string, string>();
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    return true;
                }
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length == 0)
                    {
                        return true;
                    }
                    SkipUtf8Bom(stream);

                    DataContractJsonSerializerSettings settings = new DataContractJsonSerializerSettings();
                    settings.UseSimpleDictionaryFormat = true;
                    DataContractJsonSerializer serializer =
                        new DataContractJsonSerializer(typeof(Dictionary<string, string>), settings);
                    Dictionary<string, string> result =
                        serializer.ReadObject(stream) as Dictionary<string, string>;
                    value = result ?? new Dictionary<string, string>();
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex;
                LogWriter.Write("读取 JSON 字典失败：" + path, ex);
                return false;
            }
        }

        /// <summary>若流以 UTF-8 BOM（EF BB BF）开头则跳过 3 字节，否则回到开头。</summary>
        private static void SkipUtf8Bom(Stream stream)
        {
            if (stream.Length < 3)
            {
                stream.Position = 0;
                return;
            }
            byte[] head = new byte[3];
            stream.Read(head, 0, 3);
            bool hasBom = head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
            stream.Position = hasBom ? 3 : 0;
        }

        public static void Save<T>(string path, T value) where T : class
        {
            Save(path, value, true);
        }

        /// <summary>
        /// 保存 JSON。captureBackup=false 用于恢复流程写入，避免把恢复结果再当成新版本备份。
        /// </summary>
        public static void Save<T>(string path, T value, bool captureBackup) where T : class
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = path + ".tmp";
            using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, value);
            }

            // INV-11 原子落盘：优先用 File.Replace（同分区内的原子替换），
            // 只有在目标尚不存在等不适用场景下才退回 File.Copy（后者是「覆盖写」，中断会留下半截文件）。
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch (Exception)
            {
                File.Copy(tempPath, path, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (IOException)
                {
                    // 临时文件删不掉不影响主流程
                }
            }

            if (captureBackup)
            {
                JsonBackup.Capture(path);
            }
        }

        /// <summary>导出为格式化的 JSON 文本（供设置窗「导出 JSON」使用）。</summary>
        public static string ToPrettyJson<T>(T value) where T : class
        {
            using (MemoryStream stream = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
