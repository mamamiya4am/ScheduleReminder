using System;
using System.Collections.Generic;
using System.IO;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 提示音素材库：管理程序数据目录下的 sound\ 文件夹（WAV / MP3）。
    ///
    /// 规则：
    /// ① 音效统一放在程序数据目录的 data\sound\ 下；用户可直接在文件夹里增删文件，
    ///    列表**每次实时读磁盘**，增删立即生效；
    /// ② 列表最后永远有一个「-」选项：选中它 = 不用音效（提醒照常弹，但不播放声音）；
    ///    即使文件夹里一个音效都没有，「-」也不会消失；
    /// ③ 音效文件被删掉时程序不得崩溃：解析时找不到文件就返回 null（调用方按「静音」处理并记日志）；
    /// </summary>
    public static class SoundLibrary
    {
        /// <summary>「不使用音效」选项的显示名，永远排在列表最后一位。</summary>
        public const string NoneOption = "-";

        private static readonly string[] SupportedExtensions = { ".wav", ".mp3" };

        /// <summary>音效目录（data\sound）。</summary>
        public static string Directory
        {
            get { return AppPaths.SoundDirectory; }
        }

        /// <summary>确保音效目录存在；返回 false 表示创建失败（只读空间等）。</summary>
        public static bool EnsureDirectory()
        {
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                {
                    System.IO.Directory.CreateDirectory(Directory);
                }
                return true;
            }
            catch (Exception ex)
            {
                LogWriter.Write("创建音效目录失败：" + Directory, ex);
                return false;
            }
        }

        /// <summary>列出当前音效文件名（不含路径），按名称排序；读不到就返回空列表（不抛异常）。</summary>
        public static List<string> ListNames()
        {
            List<string> names = new List<string>();
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                {
                    return names;
                }
                string[] files = System.IO.Directory.GetFiles(Directory);
                for (int i = 0; i < files.Length; i++)
                {
                    if (!IsSupportedFile(files[i]))
                    {
                        continue;
                    }
                    names.Add(Path.GetFileName(files[i]));
                }
                names.Sort(StringComparer.CurrentCultureIgnoreCase);
            }
            catch (Exception ex)
            {
                LogWriter.Write("枚举音效文件失败：" + Directory, ex);
            }
            return names;
        }

        /// <summary>
        /// 把「设置里存的名字」解析成完整文件路径。
        /// 返回 null 表示：没选 / 选了「-」/ 文件已被删除。
        /// 调用方一律按「静音」处理，绝不抛异常。
        /// </summary>
        public static string ResolvePath(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name) || name == NoneOption)
                {
                    return null;
                }
                string full = Path.Combine(Directory, name);
                if (!File.Exists(full))
                {
                    // 用户删掉了音效文件：静默降级为「无音效」，不弹错、不崩溃
                    LogWriter.Write("音效文件已不存在，按「无音效」处理：" + full);
                    return null;
                }
                if (!IsSupportedFile(full))
                {
                    return null;
                }
                return full;
            }
            catch (Exception ex)
            {
                LogWriter.Write("解析音效路径失败：" + name, ex);
                return null;
            }
        }

        private static bool IsSupportedFile(string path)
        {
            try
            {
                string ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext))
                {
                    return false;
                }
                ext = ext.ToLowerInvariant();
                for (int i = 0; i < SupportedExtensions.Length; i++)
                {
                    if (ext == SupportedExtensions[i])
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
