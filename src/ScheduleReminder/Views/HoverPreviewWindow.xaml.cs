using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ScheduleReminder.Models;
using ScheduleReminder.Services;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 悬停预览浮层（P0-2）：单实例，全程只 show/hide，绝不 new/destroy；
    /// 非激活式（ShowActivated=False）+ 鼠标穿透（IsHitTestVisible=False）+ 置顶。
    /// </summary>
    public partial class HoverPreviewWindow : Window
    {
        private readonly double _baseWidth;

        public HoverPreviewWindow()
        {
            InitializeComponent();
            _baseWidth = Width;
        }

        /// <summary>显示某日的日程预览；若该日无日程则直接隐藏。</summary>
        public void ShowPreview(DayCellVm cell, double cellLeft, double cellTop, double cellWidth, double cellHeight)
        {
            if (cell == null)
            {
                HidePreview();
                return;
            }

            // 每次都构建一份全新的行清单并整体替换 ItemsSource：
            // 强制 ItemsControl 从头生成所有行，杜绝“窗口藏在后台时只补画部分行”的不稳定（时多时少）。
            List<PreviewRowVm> rows = BuildRows(cell.Date);
            if (rows.Count == 0)
            {
                HidePreview();
                return;
            }

            TitleText.Text = DateUtil.FormatDateTitle(cell.Date) + BuildSuffix(cell.Date);

            double scale = ThemeService.UiScale;

            // 宽度以 XAML 为基准；高度仍随行数动态，多行时由内部 ScrollViewer 滚动。
            double computed = (112 + rows.Count * 59 + 4) * scale;
            Rect area = ScreenHelper.GetWorkArea();
            double maxH = (area.Bottom - area.Top) * 0.85;
            Height = Math.Min(computed, maxH);
            Width = _baseWidth * scale;

            FrameworkElement content = Content as FrameworkElement;
            if (content != null)
            {
                content.LayoutTransform = new ScaleTransform(scale, scale);
            }

            RowItems.ItemsSource = rows;

            Position(cellLeft, cellTop, cellWidth);

            if (!IsVisible)
            {
                Show();
            }
        }

        public void HidePreview()
        {
            if (IsVisible)
            {
                Hide();
            }
        }

        private static string BuildSuffix(DateTime date)
        {
            if (AppServices.Calendar == null)
            {
                return string.Empty;
            }
            string suffix = AppServices.Calendar.GetTitleSuffix(date);
            if (string.IsNullOrEmpty(suffix))
            {
                return string.Empty;
            }
            return " " + suffix;
        }

        private static List<PreviewRowVm> BuildRows(DateTime date)
        {
            List<PreviewRowVm> rows = new List<PreviewRowVm>();
            if (AppServices.Schedules == null)
            {
                return rows;
            }

            List<ScheduleOccurrence> occurrences = AppServices.Schedules.GetOccurrences(date);
            for (int i = 0; i < occurrences.Count; i++)
            {
                ScheduleOccurrence occurrence = occurrences[i];
                Schedule schedule = occurrence.Schedule;
                PreviewRowVm row = new PreviewRowVm();
                row.TimeText = DateUtil.FormatTime(schedule.TimeValue);
                row.TitleText = schedule.Title;
                // OPT-017 方案 A：值带「分钟」、表头不带单位（与编辑窗「提前提醒」列完全一致）
                row.AdvanceText = schedule.AdvanceMinutes == 0
                    ? "-"
                    : schedule.AdvanceMinutes.ToString(CultureInfo.InvariantCulture) + " 分钟";
                row.NoteText = string.IsNullOrEmpty(schedule.Note) ? "-" : schedule.Note;
                row.HasTag = !string.IsNullOrEmpty(schedule.Tag);
                row.TagText = row.HasTag ? schedule.Tag : "-";
                row.TagBackgroundBrush = row.HasTag
                    ? ThemeService.ScheduleHoverBrush(schedule.BgColor)
                    : Brushes.Transparent;
                row.BgColor = schedule.BgColor;
                row.FontColor = schedule.FontColor;
                row.RemindEnabled = schedule.RemindEnabled;
                row.IsTodo = schedule.IsTodo;
                row.IsPast = occurrence.IsPast;
                rows.Add(row);
            }

            return rows;
        }

        private void Position(double cellLeft, double cellTop, double cellWidth)
        {
            Rect area = ScreenHelper.GetWorkArea();

            double left = cellLeft + cellWidth + 8;
            if (left + Width > area.Right)
            {
                left = cellLeft - 8 - Width;
            }
            if (left < area.Left)
            {
                left = area.Left;
            }
            if (left + Width > area.Right)
            {
                left = area.Right - Width;
            }

            double top = cellTop;
            if (top + Height > area.Bottom)
            {
                top = area.Bottom - Height;
            }
            if (top < area.Top)
            {
                top = area.Top;
            }

            Left = left;
            Top = top;
        }
    }
}
