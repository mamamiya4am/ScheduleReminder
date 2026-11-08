using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ScheduleReminder.Domain;
using ScheduleReminder.Domain.Statistics;
using ScheduleReminder.Models;
using ScheduleReminder.Services;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 统计数据窗口：只读统计，默认近 30 天 / 周粒度 / 状态。
    /// 统计走后台线程；窗口关闭时取消计算并注销事件订阅。
    /// </summary>
    public partial class StatisticsWindow : Window
    {
        private static StatisticsWindow _instance;

        private readonly List<ToggleButton> _rangeButtons = new List<ToggleButton>();
        private readonly List<ToggleButton> _granularityButtons = new List<ToggleButton>();
        private readonly List<ToggleButton> _viewButtons = new List<ToggleButton>();
        private readonly HashSet<string> _hiddenSeries = new HashSet<string>(StringComparer.Ordinal);

        private DateTime _today;
        private DateTime _earliest;
        private DateTime _rangeStart;
        private DateTime _rangeEnd;
        private DateTime _calendarMonth;
        private bool _hasAnySchedule;
        private bool _showTotal;
        private bool _editingStartDate;
        private bool _isLoaded;
        private string _rangeKey = "30";
        private StatisticsGranularity _granularity = StatisticsGranularity.Week;
        private StatisticsView _statisticsView = StatisticsView.Completion;
        private CancellationTokenSource _calculation;
        private StatisticsResult _result;

        public StatisticsWindow()
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();
            ThemeService.ApplyScale(this, Width, Height, MinWidth, MinHeight);

            Loaded += OnLoaded;
            Closed += OnClosed;
            AppServices.DataChanged += OnDataChanged;
            AppServices.ScheduleFieldChanged += OnScheduleFieldChanged;

            BuildControlButtons();
            RefreshTodayAndEarliest();
            ApplyPreset("30", false);
            ApplyTheme();
            ShowLoading();
        }

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

            StatisticsWindow window = new StatisticsWindow();
            _instance = window;
            window.Closed += OnInstanceClosed;
            window.Show();
        }

        private static void OnInstanceClosed(object sender, EventArgs e)
        {
            _instance = null;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            RecalculateAsync();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _isLoaded = false;
            if (_calculation != null)
            {
                _calculation.Cancel();
                _calculation = null;
            }
            AppServices.DataChanged -= OnDataChanged;
            AppServices.ScheduleFieldChanged -= OnScheduleFieldChanged;
            ThemeService.UnregisterScale(this);
        }

        private void OnDataChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OnExternalDataChanged));
                return;
            }
            OnExternalDataChanged();
        }

        private void OnExternalDataChanged()
        {
            ApplyTheme();
            RefreshTodayAndEarliest();
            if (string.Equals(_rangeKey, "all", StringComparison.Ordinal))
            {
                ApplyPreset("all", false);
            }
            else
            {
                SetRange(_rangeStart, _rangeEnd, _rangeKey);
            }
            RecalculateAsync();
        }

        private void OnScheduleFieldChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RecalculateAsync));
                return;
            }
            RecalculateAsync();
        }

        private void BuildControlButtons()
        {
            string[] rangeKeys = { "all", "7", "30", "90", "lastweek", "lastmonth", "year", "custom" };
            string[] rangeLabels = { "全部", "近 7 天", "近 30 天", "近 90 天", "上周", "上月", "今年", "自定义" };
            for (int i = 0; i < rangeKeys.Length; i++)
            {
                ToggleButton button = new ToggleButton();
                // 与粒度 / 划分共用同一套 ToggleButton 状态样式：
                // 选中由 IsChecked 触发器负责，悬浮由 IsMouseOver 触发器负责，代码不再写本地颜色。
                button.Style = (Style)FindResource("StatsSegmentStyle");
                button.Content = rangeLabels[i];
                button.Tag = rangeKeys[i];
                button.Click += OnRangeClick;
                RangePanel.Children.Add(button);
                _rangeButtons.Add(button);
            }

            AddSegment(GranularityPanel, _granularityButtons, "day", "日", OnGranularityClick);
            AddSegment(GranularityPanel, _granularityButtons, "week", "周", OnGranularityClick);
            AddSegment(GranularityPanel, _granularityButtons, "month", "月", OnGranularityClick);
            AddSegment(ViewPanel, _viewButtons, "completion", "状态", OnViewClick);
            AddSegment(ViewPanel, _viewButtons, "tag", "标签", OnViewClick);

            UpdateRangeButtons();
            UpdateSegments();
        }

        private void AddSegment(
            Panel panel, List<ToggleButton> list, string key, string label, RoutedEventHandler handler)
        {
            ToggleButton button = new ToggleButton();
            button.Style = (Style)FindResource("StatsSegmentStyle");
            button.Content = label;
            button.Tag = key;
            button.Click += handler;
            panel.Children.Add(button);
            list.Add(button);
        }

        private void OnRangeClick(object sender, RoutedEventArgs e)
        {
            ToggleButton button = sender as ToggleButton;
            if (button == null)
            {
                return;
            }
            string key = button.Tag as string;
            ApplyPreset(key, true);
            if (string.Equals(key, "custom", StringComparison.Ordinal))
            {
                OpenCalendar(false);
            }
        }

        private void ApplyPreset(string key, bool recalculate)
        {
            if (string.IsNullOrEmpty(key))
            {
                key = "30";
            }
            _rangeKey = key;
            DateTime start = _rangeStart;
            DateTime end = _rangeEnd;

            if (key == "all")
            {
                start = _earliest;
                end = _today;
            }
            else if (key == "7")
            {
                start = _today.AddDays(-6);
                end = _today;
            }
            else if (key == "30")
            {
                start = _today.AddDays(-29);
                end = _today;
            }
            else if (key == "90")
            {
                start = _today.AddDays(-89);
                end = _today;
            }
            else if (key == "lastweek")
            {
                DateTime thisMonday = StartOfWeek(_today);
                start = thisMonday.AddDays(-7);
                end = thisMonday.AddDays(-1);
            }
            else if (key == "lastmonth")
            {
                DateTime firstThisMonth = new DateTime(_today.Year, _today.Month, 1);
                DateTime firstLastMonth = firstThisMonth.AddMonths(-1);
                start = firstLastMonth;
                end = firstThisMonth.AddDays(-1);
            }
            else if (key == "year")
            {
                start = new DateTime(_today.Year, 1, 1);
                end = _today;
            }
            else if (key == "custom")
            {
                // 保留当前起止，打开日历时由用户改。
            }

            SetRange(start, end, key);
            if (recalculate)
            {
                ClearHiddenSeries();
                RecalculateAsync();
            }
        }

        private void SetRange(DateTime start, DateTime end, string key)
        {
            start = start.Date;
            end = end.Date;
            if (start > end)
            {
                DateTime t = start;
                start = end;
                end = t;
            }
            if (end > _today)
            {
                end = _today;
            }
            if (end < _earliest)
            {
                end = _earliest;
            }
            if (start < _earliest)
            {
                start = _earliest;
            }
            if (start > end)
            {
                start = end;
            }

            _rangeStart = start;
            _rangeEnd = end;
            _rangeKey = key;
            StartDateButton.Content = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            EndDateButton.Content = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            DaysText.Text = "共 " + ((end - start).Days + 1).ToString(CultureInfo.InvariantCulture) + " 天";
            UpdateRangeButtons();
        }

        private void UpdateRangeButtons()
        {
            for (int i = 0; i < _rangeButtons.Count; i++)
            {
                ToggleButton button = _rangeButtons[i];
                button.IsChecked = string.Equals(
                    button.Tag as string, _rangeKey, StringComparison.Ordinal);
            }
        }

        private void OnGranularityClick(object sender, RoutedEventArgs e)
        {
            ToggleButton button = sender as ToggleButton;
            if (button == null)
            {
                return;
            }
            if (string.Equals(button.Tag as string, "day", StringComparison.Ordinal))
            {
                _granularity = StatisticsGranularity.Day;
            }
            else if (string.Equals(button.Tag as string, "month", StringComparison.Ordinal))
            {
                _granularity = StatisticsGranularity.Month;
            }
            else
            {
                _granularity = StatisticsGranularity.Week;
            }
            UpdateSegments();
            ClearHiddenSeries();
            RecalculateAsync();
        }

        private void OnViewClick(object sender, RoutedEventArgs e)
        {
            ToggleButton button = sender as ToggleButton;
            if (button == null)
            {
                return;
            }
            _statisticsView = string.Equals(button.Tag as string, "tag", StringComparison.Ordinal)
                ? StatisticsView.Tag
                : StatisticsView.Completion;
            UpdateSegments();
            ClearHiddenSeries();
            RecalculateAsync();
        }

        private void UpdateSegments()
        {
            for (int i = 0; i < _granularityButtons.Count; i++)
            {
                ToggleButton button = _granularityButtons[i];
                bool selected = false;
                string key = button.Tag as string;
                if (_granularity == StatisticsGranularity.Day) selected = key == "day";
                else if (_granularity == StatisticsGranularity.Month) selected = key == "month";
                else selected = key == "week";
                button.IsChecked = selected;
            }
            for (int i = 0; i < _viewButtons.Count; i++)
            {
                ToggleButton button = _viewButtons[i];
                button.IsChecked = string.Equals(button.Tag as string,
                    _statisticsView == StatisticsView.Tag ? "tag" : "completion",
                    StringComparison.Ordinal);
            }
        }

        private void RefreshTodayAndEarliest()
        {
            _today = DateTime.Now.Date;
            _hasAnySchedule = false;
            DateTime? earliest = null;

            if (AppServices.Repository != null)
            {
                List<Schedule> all = AppServices.Repository.All;
                for (int i = 0; i < all.Count; i++)
                {
                    Schedule schedule = all[i];
                    if (schedule == null)
                    {
                        continue;
                    }
                    DateTime date = schedule.DateValue.Date;
                    if (date < DateUtil.MinSupportedDate || date > DateUtil.MaxSupportedDate)
                    {
                        continue;
                    }
                    _hasAnySchedule = true;
                    if (!earliest.HasValue || date < earliest.Value)
                    {
                        earliest = date;
                    }
                }
            }

            if (!earliest.HasValue || earliest.Value > _today)
            {
                _earliest = _today;
            }
            else
            {
                _earliest = earliest.Value;
            }
        }

        private async void RecalculateAsync()
        {
            if (!_isLoaded)
            {
                return;
            }

            if (_calculation != null)
            {
                // 只取消，不在旧任务仍可能读取 IsCancellationRequested 时 Dispose。
                // 旧任务会在自己的 finally 中释放对应的 CTS。
                _calculation.Cancel();
            }
            CancellationTokenSource cts = new CancellationTokenSource();
            _calculation = cts;

            ShowLoading();

            DateTime start = _rangeStart;
            DateTime end = _rangeEnd;
            StatisticsGranularity granularity = _granularity;
            StatisticsView view = _statisticsView;

            List<Schedule> schedules = new List<Schedule>();
            if (AppServices.Repository != null)
            {
                List<Schedule> all = AppServices.Repository.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null)
                    {
                        schedules.Add(all[i].Clone());
                    }
                }
            }

            List<TagTemplate> templates = new List<TagTemplate>();
            if (AppServices.Tags != null)
            {
                for (int i = 0; i < AppServices.Tags.All.Count; i++)
                {
                    TagTemplate template = AppServices.Tags.All[i];
                    if (template != null)
                    {
                        templates.Add(template.Clone());
                    }
                }
            }

            try
            {
                StatisticsResult result = await Task.Run(delegate
                {
                    return StatisticsEngine.Compute(
                        schedules, templates, start, end, granularity, view,
                        delegate { return cts.IsCancellationRequested; });
                });

                if (cts.IsCancellationRequested || !ReferenceEquals(_calculation, cts))
                {
                    return;
                }

                _result = result;
                ApplyResult(result);
            }
            catch (Exception ex)
            {
                if (!cts.IsCancellationRequested)
                {
                    LogWriter.Write("统计数据计算失败", ex);
                    ShowEmpty("统计失败，请稍后重试");
                }
            }
            finally
            {
                if (ReferenceEquals(_calculation, cts))
                {
                    _calculation = null;
                }
                cts.Dispose();
            }
        }

        private void ApplyResult(StatisticsResult result)
        {
            if (result == null)
            {
                ShowEmpty("还没有可统计的日程");
                return;
            }

            TotalValueText.Text = StatisticsCaliber.FormatNumber(result.Total);
            AverageValueText.Text = StatisticsCaliber.FormatNumber(result.Average);
            RateValueText.Text = StatisticsCaliber.FormatRate(result.Done, result.Total);
            PeakValueText.Text = StatisticsCaliber.FormatNumber(result.Peak);

            bool empty = result.Total == 0;
            if (empty)
            {
                ShowEmpty(_hasAnySchedule ? "这段时间没有日程" : "还没有可统计的日程");
                return;
            }

            EmptyText.Visibility = Visibility.Collapsed;
            Chart.Visibility = Visibility.Visible;
            Chart.Result = result;
            BuildLegend(result);
            ApplyChartPalette(result);
            Chart.SetHiddenKeys(_hiddenSeries);
            Chart.ShowTotal = _showTotal;
            Chart.InvalidateVisual();
        }

        private void ShowLoading()
        {
            EmptyText.Text = "正在统计…";
            EmptyText.Visibility = Visibility.Visible;
            Chart.Visibility = Visibility.Collapsed;
            LegendPanel.Children.Clear();
        }

        private void ShowEmpty(string text)
        {
            EmptyText.Text = text;
            EmptyText.Visibility = Visibility.Visible;
            Chart.Visibility = Visibility.Collapsed;
            Chart.Result = null;
            LegendPanel.Children.Clear();
        }

        private void ClearHiddenSeries()
        {
            _hiddenSeries.Clear();
            if (Chart != null)
            {
                Chart.SetHiddenKeys(_hiddenSeries);
            }
        }

        private void BuildLegend(StatisticsResult result)
        {
            LegendPanel.Children.Clear();

            ToggleButton total = new ToggleButton();
            total.Style = (Style)FindResource("StatsLegendStyle");
            total.Content = "总计";
            total.Tag = "__total__";
            total.IsChecked = _showTotal;
            total.BorderBrush = TotalBrush();
            total.FontFamily = (FontFamily)FindResource("AppFont");
            total.FontSize = (double)FindResource("FontBodyS");
            total.Click += OnLegendClick;
            total.ToolTip = "点击显示 / 隐藏总计线";
            LegendPanel.Children.Add(total);

            for (int i = 0; i < result.Series.Count; i++)
            {
                StatisticsSeries series = result.Series[i];
                ToggleButton button = new ToggleButton();
                button.Style = (Style)FindResource("StatsLegendStyle");
                button.Content = series.DisplayName;
                button.Tag = series.Key;
                button.IsChecked = !_hiddenSeries.Contains(series.Key);
                button.BorderBrush = SeriesBrush(series.PaletteIndex);
                button.FontFamily = (FontFamily)FindResource("AppFont");
                button.FontSize = (double)FindResource("FontBodyS");
                button.Click += OnLegendClick;
                button.ToolTip = "点击显示 / 隐藏";
                LegendPanel.Children.Add(button);
            }

            Chart.SetHiddenKeys(_hiddenSeries);
            Chart.ShowTotal = _showTotal;
        }

        private void OnLegendClick(object sender, RoutedEventArgs e)
        {
            ToggleButton button = sender as ToggleButton;
            if (button == null)
            {
                return;
            }
            string key = button.Tag as string;
            if (key == "__total__")
            {
                _showTotal = button.IsChecked == true;
                Chart.ShowTotal = _showTotal;
                Chart.InvalidateVisual();
                return;
            }
            if (button.IsChecked == true)
            {
                _hiddenSeries.Remove(key);
            }
            else
            {
                _hiddenSeries.Add(key);
            }
            Chart.SetHiddenKeys(_hiddenSeries);
            Chart.InvalidateVisual();
        }

        private void ApplyChartPalette(StatisticsResult result)
        {
            Dictionary<string, Brush> brushes = new Dictionary<string, Brush>(StringComparer.Ordinal);
            for (int i = 0; i < result.Series.Count; i++)
            {
                brushes[result.Series[i].Key] = SeriesBrush(result.Series[i].PaletteIndex);
            }
            Chart.SetPalette(brushes, TotalBrush());
            Chart.SetTypeface(GetTypeface());
            Chart.SetChartBrushes(
                (Brush)FindResource("TextSecondaryBrush"),
                (Brush)FindResource("BorderWeakBrush"),
                (Brush)FindResource("TextPrimaryBrush"),
                (Brush)FindResource("TextSecondaryBrush"),
                (Brush)FindResource("PopupBgBrush"));
        }

        private void ApplyTheme()
        {
            if (AppServices.Settings == null)
            {
                return;
            }
            try
            {
                RootBorder.Background = (Brush)FindResource("WindowBgBrush");
            }
            catch (Exception)
            {
                Color background = ThemeService.ParseColor(AppServices.Settings.WindowBgColor);
                RootBorder.Background = new SolidColorBrush(background);
            }

            Chart.SetTypeface(GetTypeface());
            Chart.SetChartBrushes(
                (Brush)FindResource("TextSecondaryBrush"),
                (Brush)FindResource("BorderWeakBrush"),
                (Brush)FindResource("TextPrimaryBrush"),
                (Brush)FindResource("TextSecondaryBrush"),
                (Brush)FindResource("PopupBgBrush"));

            if (_result != null)
            {
                ApplyChartPalette(_result);
            }
        }

        private Brush SeriesBrush(int index)
        {
            bool light = IsLightTheme();
            string prefix = light ? "StatisticsLight" : "StatisticsDark";
            int slot = index + 1;
            if (slot < 1) slot = 1;
            if (slot > 12) slot = 12;
            object value = TryFindResource(prefix + slot.ToString("00", CultureInfo.InvariantCulture));
            Color color = value is Color ? (Color)value : Colors.Gray;
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private Brush TotalBrush()
        {
            bool light = IsLightTheme();
            object value = TryFindResource(light ? "StatisticsTotalLight" : "StatisticsTotalDark");
            Color color = value is Color ? (Color)value : (light ? Colors.Black : Colors.White);
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private bool IsLightTheme()
        {
            if (AppServices.Settings == null)
            {
                return false;
            }
            try
            {
                return ThemeService.IsLightBackground(ThemeService.ParseColor(AppServices.Settings.WindowBgColor));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private Typeface GetTypeface()
        {
            FontFamily font = FindResource("AppFont") as FontFamily;
            return new Typeface(font ?? new FontFamily("Microsoft YaHei UI"),
                FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        }

        private static DateTime StartOfWeek(DateTime date)
        {
            int offset = ((int)date.DayOfWeek + 6) % 7;
            return date.Date.AddDays(-offset);
        }

        // ---------------- 日期弹层 ----------------

        private void OnStartDateClick(object sender, RoutedEventArgs e)
        {
            OpenCalendar(true);
        }

        private void OnEndDateClick(object sender, RoutedEventArgs e)
        {
            OpenCalendar(false);
        }

        private void OpenCalendar(bool start)
        {
            _editingStartDate = start;
            _calendarMonth = new DateTime(
                (start ? _rangeStart : _rangeEnd).Year,
                (start ? _rangeStart : _rangeEnd).Month, 1);
            DatePopup.PlacementTarget = start ? StartDateButton : EndDateButton;
            DatePopup.IsOpen = true;
            RenderCalendar();
        }

        private void RenderCalendar()
        {
            CalendarMonthText.Text = _calendarMonth.ToString("yyyy年MM月", CultureInfo.InvariantCulture);
            CalendarPrevButton.IsEnabled = _calendarMonth > new DateTime(_earliest.Year, _earliest.Month, 1);
            CalendarNextButton.IsEnabled = _calendarMonth < new DateTime(_today.Year, _today.Month, 1);
            CalendarHint.Text = _editingStartDate ? "正在选择开始日期" : "正在选择结束日期";

            CalendarDays.Children.Clear();
            DateTime first = new DateTime(_calendarMonth.Year, _calendarMonth.Month, 1);
            int offset = ((int)first.DayOfWeek + 6) % 7;
            DateTime cursor = first.AddDays(-offset);

            for (int i = 0; i < 42; i++)
            {
                DateTime date = cursor.AddDays(i);
                Button button = new Button();
                button.Style = (Style)FindResource("StatsCalendarDayStyle");
                button.Content = date.Day.ToString(CultureInfo.InvariantCulture);
                button.Tag = date;

                bool inMonth = date.Month == _calendarMonth.Month && date.Year == _calendarMonth.Year;
                bool allowed = inMonth && date >= _earliest && date <= _today;
                button.IsEnabled = allowed;

                if (date == _rangeStart || date == _rangeEnd)
                {
                    button.Background = (Brush)FindResource("AccentBrush");
                    button.Foreground = (Brush)FindResource("TodayOnColorBrush");
                    button.FontWeight = FontWeights.SemiBold;
                }
                else if (inMonth && date > _rangeStart && date < _rangeEnd)
                {
                    button.Background = (Brush)FindResource("BgHoverBrush");
                }

                if (!inMonth)
                {
                    button.Visibility = Visibility.Hidden;
                }

                button.Click += OnCalendarDayClick;
                CalendarDays.Children.Add(button);
            }
        }

        private void OnCalendarPrevClick(object sender, RoutedEventArgs e)
        {
            _calendarMonth = _calendarMonth.AddMonths(-1);
            RenderCalendar();
        }

        private void OnCalendarNextClick(object sender, RoutedEventArgs e)
        {
            _calendarMonth = _calendarMonth.AddMonths(1);
            RenderCalendar();
        }

        private void OnCalendarDayClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null || !(button.Tag is DateTime))
            {
                return;
            }
            DateTime date = (DateTime)button.Tag;
            if (date < _earliest || date > _today)
            {
                return;
            }

            if (_editingStartDate)
            {
                _rangeStart = date;
                if (_rangeStart > _rangeEnd)
                {
                    _rangeEnd = _rangeStart;
                }
            }
            else
            {
                _rangeEnd = date;
                if (_rangeEnd < _rangeStart)
                {
                    _rangeStart = _rangeEnd;
                }
            }

            SetRange(_rangeStart, _rangeEnd, "custom");
            DatePopup.IsOpen = false;
            ClearHiddenSeries();
            RecalculateAsync();
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && DatePopup.IsOpen)
            {
                DatePopup.IsOpen = false;
                e.Handled = true;
            }
        }

        // ---------------- 标题栏 ----------------

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
    }
}
