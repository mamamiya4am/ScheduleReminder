using System;
using System.Globalization;
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
    /// 日程详细内容窗（只读展示）：由提醒浮层的「日程详细」按钮打开。
    /// 普通非模态窗口、任务栏可见、仅右上角关闭键（不可最小化 / 最大化）、
    /// 不可手动缩放，但随「界面大小」档位整体缩放。
    /// </summary>
    public partial class DetailWindow : Window
    {
        public DetailWindow(
            Schedule schedule, DateTime occurrenceDate, bool isAdvance, int remainingMinutes)
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();

            // 随「界面大小」档位缩放；最小尺寸传 0 —— 不设下限，避免档位切换时被最小尺寸钳制
            ThemeService.ApplyScale(this, Width, Height, 0, 0);
            Closed += OnClosed;

            Fill(schedule, occurrenceDate, isAdvance, remainingMinutes);
        }

        /// <summary>把日程内容填进只读界面。</summary>
        private void Fill(
            Schedule schedule, DateTime occurrenceDate, bool isAdvance, int remainingMinutes)
        {
            if (schedule == null)
            {
                return;
            }

            TitleText.Text = isAdvance
                ? "提前提醒 — 还有 " + remainingMinutes + " 分钟"
                : "正点提醒";
            Title = TitleText.Text;

            // 标题块：背景 = 日程自己的背景色（空 = 不铺底色）；文字 = 日程自己的文字色（空 = 跟随系统文字色）
            ScheduleTitleText.Text = schedule.Title;
            if (string.IsNullOrEmpty(schedule.BgColor))
            {
                TitleBlock.Background = Brushes.Transparent;
                TitleBlock.BorderBrush = CreateFaintBorderBrush();
                TitleBlock.BorderThickness = new Thickness(1);
            }
            else
            {
                // 与月历格子 / 悬停预览一致：日程底色的透明度跟随「背景透明度」设置
                Color bg = ThemeService.ParseColor(schedule.BgColor);
                byte alpha = (byte)Math.Round(bg.A * ThemeService.BgOpacity);
                TitleBlock.Background = new SolidColorBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
                TitleBlock.BorderBrush = null;
                TitleBlock.BorderThickness = new Thickness(0);
            }
            if (string.IsNullOrEmpty(schedule.FontColor))
            {
                // 直接引用系统文字画笔：改「系统文字颜色」设置时这里会实时跟着变
                ScheduleTitleText.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            }
            else
            {
                ScheduleTitleText.Foreground = new SolidColorBrush(ThemeService.ParseColor(schedule.FontColor));
            }

            TimeValueText.Text = DateUtil.FormatTime(schedule.TimeValue);

            bool hasTag = !string.IsNullOrEmpty(schedule.Tag);
            TagBlock.Visibility = hasTag ? Visibility.Visible : Visibility.Collapsed;
            if (hasTag)
            {
                TagValueText.Text = schedule.Tag;
                TagBlock.Background = ThemeService.ScheduleHoverBrush(schedule.BgColor);
                if (string.IsNullOrEmpty(schedule.FontColor))
                {
                    TagValueText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                }
                else
                {
                    TagValueText.Foreground = new SolidColorBrush(ThemeService.ParseColor(schedule.FontColor));
                }
            }

            // 提前提醒：只有本次是「提前提醒」弹窗时才显示该字段；正点提醒一律不显示
            if (isAdvance)
            {
                AdvancePanel.Visibility = Visibility.Visible;
                AdvanceValueText.Text = schedule.AdvanceMinutes.ToString(CultureInfo.InvariantCulture) + " 分钟";
            }
            else
            {
                AdvancePanel.Visibility = Visibility.Collapsed;
            }

            NoteValueText.Text = string.IsNullOrWhiteSpace(schedule.Note) ? "-" : schedule.Note;
            RecurValueText.Text = RecurrenceService.Describe(schedule);
        }

        private Brush CreateFaintBorderBrush()
        {
            SolidColorBrush source = TryFindResource("BorderWeakBrush") as SolidColorBrush;
            Color color = source != null ? source.Color : Colors.White;
            return new SolidColorBrush(Color.FromArgb(26, color.R, color.G, color.B));
        }

        private void OnClosed(object sender, EventArgs e)
        {
            ThemeService.UnregisterScale(this);
        }

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
