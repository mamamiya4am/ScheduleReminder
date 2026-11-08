using System;
using System.IO;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>程序目录与数据目录定位（数据固定放在程序运行目录下的 data\ 子目录）。</summary>
    public static class AppPaths
    {
        public static string BaseDirectory
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string DataDirectory
        {
            get { return Path.Combine(BaseDirectory, "data"); }
        }

        public static string SchedulesPath
        {
            get { return Path.Combine(DataDirectory, "schedules.json"); }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(DataDirectory, "settings.json"); }
        }

        public static string TagsPath
        {
            get { return Path.Combine(DataDirectory, "tags.json"); }
        }

        public static string JsonBackupDirectory
        {
            get { return Path.Combine(DataDirectory, "backup", "json"); }
        }

        public static string JsonCorruptDirectory
        {
            get { return Path.Combine(JsonBackupDirectory, "corrupt"); }
        }

        public static string LogDirectory
        {
            get { return Path.Combine(DataDirectory, "logs"); }
        }

        public static string HolidaysPath
        {
            get { return Path.Combine(DataDirectory, "holidays.json"); }
        }

        public static string SoundDirectory
        {
            get { return Path.Combine(DataDirectory, "sound"); }
        }

        public static string AiDirectory
        {
            get { return Path.Combine(DataDirectory, "ai"); }
        }

        public static string AiInboxPath
        {
            get { return Path.Combine(AiDirectory, "inbox.json"); }
        }

        public static string AiOutboxPath
        {
            get { return Path.Combine(AiDirectory, "outbox.json"); }
        }

        public static string AiSnapshotPath
        {
            get { return Path.Combine(AiDirectory, "snapshot.json"); }
        }

        /// <summary>背景图片内部仓库（用户选的图会被复制到这里，见 BackgroundImageStore）。</summary>
        public static string BackgroundDirectory
        {
            get { return Path.Combine(DataDirectory, "background"); }
        }

        public static string LogPath
        {
            get { return Path.Combine(LogDirectory, "crash.log"); }
        }

        public static string IconPath
        {
            get { return Path.Combine(BaseDirectory, "Resources", "icon.ico"); }
        }

        /// <summary>任务栏专用 PNG（512×512 原图）：Windows 任务栏对 PNG 压缩帧的 ICO 兼容性较弱，
        /// 直接用高清 PNG 可避免任务栏误选小帧导致图标显小。EXE 文件图标与托盘仍用 icon.ico。</summary>
        public static string PngIconPath
        {
            get { return Path.Combine(BaseDirectory, "Resources", "icon_taskbar.png"); }
        }

        /// <summary>确保 data 目录存在；返回 false 表示目录不可写（调用方弹只读告警）。</summary>
        public static bool EnsureDataDirectory()
        {
            try
            {
                if (!Directory.Exists(DataDirectory))
                {
                    Directory.CreateDirectory(DataDirectory);
                }
                if (!Directory.Exists(AiDirectory))
                {
                    Directory.CreateDirectory(AiDirectory);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
