using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ScheduleReminder.Models;
using ScheduleReminder.Services;
using ScheduleReminder.Views;
using ScheduleReminder.Domain;
using ScheduleReminder.Tools;

namespace ScheduleReminder
{
    /// <summary>
    /// 应用程序入口：单实例 Mutex、全局异常钩子、服务初始化、托盘与提醒调度器启动。
    /// 因 App.xaml 设 ShutdownMode="OnExplicitShutdown"，只有托盘菜单「退出程序」调用 Shutdown() 才真正退出。
    /// </summary>
    public partial class App : Application
    {
        private static Mutex _singleInstanceMutex;
        private TrayService _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "ScheduleReminder_SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("日程提醒已经在运行中（请查看系统托盘）。", "日程提醒",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            if (!AppPaths.EnsureDataDirectory())
            {
                MessageBox.Show("当前空间只读，请移动到其他硬盘空间再试", "日程提醒",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            try
            {
                AppSettings settings = SettingsStore.Load();

                // 从未选择过音效时，默认选中列表里的第一个音效；
                // 目录为空时落到「-」（不播放声音）。用户主动选过「-」会存成 "-"，不会被这里覆盖。
                if (settings.SoundFilePath == null)
                {
                    List<string> sounds = SoundLibrary.ListNames();
                    settings.SoundFilePath = sounds.Count > 0 ? sounds[0] : SoundLibrary.NoneOption;
                    SettingsStore.Save(settings);
                }

                AppServices.Settings = settings;

                TagStore tags = new TagStore();
                tags.Load();
                AppServices.Tags = tags;

                Repository repository = new Repository();
                repository.Load();
                AppServices.Repository = repository;

                CalendarService calendar = new CalendarService();
                calendar.Load();
                AppServices.Calendar = calendar;

                SoundService sound = new SoundService();
                sound.Configure(AppServices.Settings.SoundFilePath);
                AppServices.Sound = sound;

                AppServices.Schedules = new ScheduleService(repository);

                ThemeService.ApplySettings();

                // 小工具注册：顺序就是左列面板里的排列顺序（两列按行填充）。
                // 必须在建主窗之前注册——面板每次打开都读注册表，注册晚了第一次打开是空面板。
                ToolRegistry.Register(new CalculatorTool());
                ToolRegistry.Register(new UnitConverterTool());
                ToolRegistry.Register(new StopwatchTool());
                ToolRegistry.Register(new WorldClockTool());

                MainWindow main = new MainWindow();
                AppServices.MainWindow = main;
                main.Show();

                ReminderScheduler scheduler = new ReminderScheduler(repository, AppServices.Sound);
                AppServices.Scheduler = scheduler;
                AppServices.DataChanged += OnDataChanged;
                // 字段级变化（如勾选开关）不重建界面，但提醒队列必须跟着重建，否则关掉的日程还会弹提醒
                AppServices.ScheduleFieldChanged += OnDataChanged;
                scheduler.Start();

                AiBridgeCoordinator.Initialize();

                _tray = new TrayService();
                _tray.Initialize();
            }
            catch (Exception ex)
            {
                LogWriter.Write("启动失败", ex);
                MessageBox.Show("程序启动失败：" + ex.Message + "\r\n详细信息已写入 data\\logs\\crash.log",
                    "日程提醒", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_tray != null)
                {
                    _tray.Dispose();
                    _tray = null;
                }
                if (AppServices.Scheduler != null)
                {
                    AppServices.Scheduler.Stop();
                    AppServices.Scheduler.StopSound();
                }
                if (AppServices.Settings != null)
                {
                    SettingsStore.Save(AppServices.Settings);
                }
            }
            catch (Exception ex)
            {
                LogWriter.Write("退出时清理失败", ex);
            }
            finally
            {
                base.OnExit(e);
            }
        }

        private static void OnDataChanged(object sender, EventArgs e)
        {
            if (AppServices.Scheduler != null)
            {
                AppServices.Scheduler.Rebuild();
            }
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogWriter.Write("UI 线程未处理异常", e.Exception);
            try
            {
                e.Handled = true;
                MessageBox.Show("发生未预期的错误：" + e.Exception.Message + "\r\n详细信息已写入 data\\logs\\crash.log",
                    "日程提醒", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception)
            {
                // 忽略：保证不因弹窗失败而崩溃
            }
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null)
            {
                LogWriter.Write("非 UI 线程未处理异常", ex);
            }
            else
            {
                LogWriter.Write("非 UI 线程未处理异常：" + e.ExceptionObject);
            }
        }
    }
}
