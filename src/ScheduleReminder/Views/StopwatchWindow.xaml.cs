using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ScheduleReminder.Services;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 秒表窗（小工具）：非模态、单实例、可拖拽移动、可拖拽边框缩放、无最大化按钮。
    /// 计时取 Stopwatch 的真实累计值，显示刷新定时器只负责取值与更新文本，
    /// 绝不用定时器触发次数累加计时（防止掉帧、休眠恢复后走偏）。
    /// 关闭即停止并销毁，不写任何存储，下次打开从 00:00:00.000 开始。
    /// </summary>
    public partial class StopwatchWindow : Window
    {
        private static StopwatchWindow _instance;

        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly DispatcherTimer _timer;

        public StopwatchWindow()
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();

            // 20 毫秒刷新一次：只更新文本，不参与计时
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(20);
            _timer.Tick += OnTimerTick;

            // 随「界面大小」档位缩放：基准取本窗 XAML 的 Width/Height。
            ThemeService.ApplyScale(this, Width, Height, MinWidth, MinHeight);
            Closed += OnClosed;
        }

        /// <summary>统一打开入口：单实例——已经开着就激活并前置，否则新建。</summary>
        public static void Open()
        {
            if (_instance != null)
            {
                if (_instance.WindowState == WindowState.Minimized)
                {
                    _instance.WindowState = WindowState.Normal;
                }
                _instance.Activate();
                return;
            }

            StopwatchWindow window = new StopwatchWindow();
            _instance = window;
            window.Closed += OnInstanceClosed;
            window.Show();
        }

        private static void OnInstanceClosed(object sender, EventArgs e)
        {
            _instance = null;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _timer.Stop();
            _stopwatch.Stop();
            ThemeService.UnregisterScale(this);
        }

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>主按钮三态：开始 → 暂停 → 继续（再点一次回到暂停）。</summary>
        private void OnMainClick(object sender, RoutedEventArgs e)
        {
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
                _timer.Stop();
                MainButton.Content = "继续";
            }
            else
            {
                _stopwatch.Start();
                _timer.Start();
                MainButton.Content = "暂停";
                ResetButton.IsEnabled = true;
            }
            UpdateTimeText();
        }

        /// <summary>复位：停止计时、归零、主按钮回「开始」、复位按钮重新禁用。</summary>
        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            _stopwatch.Reset();
            _timer.Stop();
            MainButton.Content = "开始";
            ResetButton.IsEnabled = false;
            UpdateTimeText();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            UpdateTimeText();
        }

        private void UpdateTimeText()
        {
            long total = (long)_stopwatch.Elapsed.TotalMilliseconds;
            if (total < 0)
            {
                total = 0;
            }

            long milliseconds = total % 1000;
            long seconds = total / 1000 % 60;
            long minutes = total / 60000 % 60;
            long hours = total / 3600000;

            HoursText.Text = hours.ToString("00", CultureInfo.InvariantCulture);
            MinutesText.Text = minutes.ToString("00", CultureInfo.InvariantCulture);
            SecondsText.Text = seconds.ToString("00", CultureInfo.InvariantCulture);
            MillisecondsText.Text = milliseconds.ToString("000", CultureInfo.InvariantCulture);
        }
    }
}
