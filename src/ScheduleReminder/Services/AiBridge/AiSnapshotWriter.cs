using System;
using System.Collections.Generic;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    public static class AiSnapshotWriter
    {
        private const string Notice =
            "本文件是只读快照，用于 AI 读取；请勿修改或据此直接写 schedules.json。";

        public static void Write()
        {
            if (AppServices.Settings == null || !AppServices.Settings.AiIntegrationEnabled)
            {
                // 关闭 AI 只停止导出，不删除旧快照；是否执行仍以总开关为准。
                return;
            }
            if (AppServices.Repository == null || AppServices.Tags == null)
            {
                return;
            }
            if (AppServices.Repository.SaveBlocked || AppServices.Tags.SaveBlocked)
            {
                LogWriter.Write("AI 快照跳过：数据处于阻断状态");
                return;
            }
            try
            {
                AppPaths.EnsureDataDirectory();
                AiSnapshot snapshot = new AiSnapshot();
                snapshot.GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                snapshot.SchemaVersion = 1;
                snapshot.Notice = Notice;
                snapshot.Tags = new List<TagTemplate>(AppServices.Tags.All);
                snapshot.Schedules = new List<Schedule>(AppServices.Repository.All);
                JsonStore.Save(AppPaths.AiSnapshotPath, snapshot, false);
            }
            catch (Exception ex)
            {
                LogWriter.Write("写 AI 快照失败", ex);
            }
        }

    }

    public static class AiBridgeCoordinator
    {
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }
            _initialized = true;
            AppServices.DataChanged += OnDataChanged;
            AppServices.ScheduleFieldChanged += OnDataChanged;
            AiSnapshotWriter.Write();
        }

        private static void OnDataChanged(object sender, EventArgs e)
        {
            AiSnapshotWriter.Write();
        }
    }
}
