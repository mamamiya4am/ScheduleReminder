using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScheduleReminder.Domain;
using ScheduleReminder.Models;
using ScheduleReminder.Services;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 右下角自绘提醒浮层（P0-6）：置顶、不超时、不自动关闭，必须手动关闭。
    /// 位置可拖动，但不落盘；浮层始终落在工作区右下角、距边 16。
    /// 尺寸固定为 300×176（100% 档位），只随「界面大小」档位整体缩放。
    /// </summary>
    public partial class ReminderWindow : Window
    {
        /// <summary>本次显示的提醒条目（「日程详细」按钮取用它的日程编号 / 发生日期 / 提醒类型）。</summary>
        private ReminderEntry _entry;
        public ReminderWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Icon = IconHelper.GetImageSource();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 背景由资源统一驱动（且本窗用 PopupBgBrush，不受背景透明度影响），无需按窗口应用。
            // 整体缩放：固定以 300×176 为 100%，登记基准供改档时实时重算。
            ThemeService.RegisterScaleBase(
                this, AppSettings.DefaultPopupWidth, AppSettings.DefaultPopupHeight);
        }

        /// <summary>显示一条提醒。</summary>
        public void ShowReminder(ReminderEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            _entry = entry; // 记住本次提醒条目：点「日程详细」时要用它的日程编号 / 发生日期 / 提醒类型

            TitleText.Text = string.IsNullOrEmpty(entry.Title) ? "日程提醒" : entry.Title;
            if (string.IsNullOrEmpty(entry.FontColor))
            {
                TitleText.Foreground = (Brush)FindResource("TextPrimaryBrush");
            }
            else
            {
                TitleText.Foreground = new SolidColorBrush(ThemeService.ParseColor(entry.FontColor));
            }

            // 标题整行底色 = 该日程的背景色；日程没设背景色时保持透明，
            // 并用 1px、10% 的淡边框把标题块与浮层背景分开。
            bool hasBackground = !string.IsNullOrEmpty(entry.BgColor);
            if (!hasBackground)
            {
                TitleRowBg.Background = Brushes.Transparent;
                TitleRowBg.BorderBrush = CreateFaintBorderBrush();
                TitleRowBg.BorderThickness = new Thickness(1);
            }
            else
            {
                TitleRowBg.Background = new SolidColorBrush(ThemeService.ParseColor(entry.BgColor));
                TitleRowBg.BorderBrush = null;
                TitleRowBg.BorderThickness = new Thickness(0);
            }

            // 标签单独占右侧列，标题在剩余空间内居中。
            bool hasTag = !string.IsNullOrWhiteSpace(entry.Tag);
            TagBlock.Visibility = hasTag ? Visibility.Visible : Visibility.Collapsed;
            if (hasTag)
            {
                TagText.Text = entry.Tag;
                TagBlock.Background = ThemeService.ScheduleHoverBrush(entry.BgColor);
                if (string.IsNullOrEmpty(entry.FontColor))
                {
                    TagText.Foreground = (Brush)FindResource("TextPrimaryBrush");
                }
                else
                {
                    TagText.Foreground = new SolidColorBrush(
                        ThemeService.ParseColor(entry.FontColor));
                }
            }

            HeaderText.Text = entry.IsAdvance
                ? "还有 " + entry.RemainingMinutes + " 分钟"
                : "提醒";

            if (string.IsNullOrWhiteSpace(entry.Note))
            {
                NoteText.Visibility = Visibility.Visible;
                NoteText.Text = "-";
            }
            else
            {
                NoteText.Visibility = Visibility.Visible;
                NoteText.Text = entry.Note;
            }

            ApplySize();
            PositionToWorkArea();
            BringToFront();
        }

        /// <summary>
        /// 多条提醒叠层时，把指定浮层重新送到最前。
        /// XAML 设了 ShowActivated="False"（不抢键盘焦点），所以这里用
        /// 「取消置顶 → 重新置顶」的方式强制 Z 序前置，而不用 Activate() 抢焦点。
        /// </summary>
        private Brush CreateFaintBorderBrush()
        {
            SolidColorBrush source = TryFindResource("BorderWeakBrush") as SolidColorBrush;
            Color color = source != null ? source.Color : Colors.White;
            return new SolidColorBrush(Color.FromArgb(26, color.R, color.G, color.B));
        }

        internal void BringToFront()
        {
            if (!IsVisible)
            {
                Show();
                return;
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Topmost = false;
            Topmost = true;
        }

        private void ApplySize()
        {
            double scale = ThemeService.UiScale;
            Width = AppSettings.DefaultPopupWidth * scale;
            Height = AppSettings.DefaultPopupHeight * scale;
            FrameworkElement content = Content as FrameworkElement;
            if (content != null)
            {
                content.LayoutTransform = new ScaleTransform(scale, scale);
            }
        }

        private void PositionToWorkArea()
        {
            Rect area = ScreenHelper.GetWorkArea();
            const double offset = 16;
            Left = area.Right - Width - offset;
            Top = area.Bottom - Height - offset;
            if (Left < area.Left)
            {
                Left = area.Left;
            }
            if (Top < area.Top)
            {
                Top = area.Top;
            }
        }

        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        /// <summary>
        /// 「日程详细」：打开该日程的详细内容窗（普通非模态窗口），并关闭本浮层。
        /// 声音由 ReminderScheduler 在浮层关闭时统一停止。
        /// </summary>
        private void OnDetailClick(object sender, RoutedEventArgs e)
        {
            Schedule schedule = null;
            if (_entry != null && AppServices.Repository != null)
            {
                schedule = AppServices.Repository.FindById(_entry.ScheduleId);
            }
            if (schedule != null)
            {
                DetailWindow window = new DetailWindow(
                    schedule, _entry.OccurrenceDate, _entry.IsAdvance, _entry.RemainingMinutes);
                window.Show();
                window.Activate(); // 非模态窗口：只带到前台，不阻塞其它窗口
            }
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // 关闭任意浮层都停止全部声音；声音由 ReminderScheduler 统一处理。
            ThemeService.UnregisterScale(this);
            base.OnClosed(e);
        }
    }
}
