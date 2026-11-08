using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ScheduleReminder.Models;
using ScheduleReminder.Services;
using ScheduleReminder.Domain;
using ScheduleReminder.Converters;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 月历主窗口（P0-1）：自绘无边框、42 格（6 行 × 7 列）跨月显示、悬停预览（P0-2）、位置记忆、关闭到托盘。
    /// </summary>
    public partial class MainWindow : Window
    {
        // ---------------- 常量 ----------------

        /// <summary>年份切换下限：统一引用 DateUtil，避免各处各写一份数字（单一事实源）。</summary>
        private const int MinYear = DateUtil.MinSupportedYear;

        /// <summary>年份切换上限：与节气表覆盖范围、日程可保存范围一致。</summary>
        private const int MaxYear = DateUtil.MaxSupportedYear;

        /// <summary>单元格内日程行的固定高度（与 XAML 中日程行 Border 的 Height 一致，OPT-023）。
        /// 这是"我们自己定的行高"，不是从布局量出来的——格子里可能一条日程都没有，那时无从量起。</summary>
        private const double ScheduleRowHeight = 14.0;

        /// <summary>日程区容器的标记：代码按它在界面树里找到该元素并量真实高度（OPT-023）。</summary>
        private const string ScheduleAreaTag = "ScheduleArea";

        /// <summary>设计尺寸下每格大致能放的条数：量不到高度时的初始兜底值。</summary>
        private const int DefaultVisibleRows = 3;

        /// <summary>悬停延迟下限（秒），见 DEC-4。</summary>
        private const double MinHoverDelaySeconds = 0.01;

        /// <summary>悬停延迟上限（秒），见 DEC-4。</summary>
        private const double MaxHoverDelaySeconds = 5.0;

        private const int WmNcLButtonDown = 0x00A1;
        private const int HtBottomRight = 17;

        // ---------------- 字段 ----------------

        private readonly ObservableCollection<DayCellVm> _cells = new ObservableCollection<DayCellVm>();
        private readonly Dictionary<DateTime, EditWindow> _editWindows = new Dictionary<DateTime, EditWindow>();
        private readonly Dictionary<EditWindow, DateTime> _editDates = new Dictionary<EditWindow, DateTime>();
        private readonly DispatcherTimer _hoverTimer = new DispatcherTimer();

        /// <summary>每格显示条数重算的节流计时器（拖窗口时避免连续重算 42 格）。</summary>
        private readonly DispatcherTimer _rowRecalcTimer = new DispatcherTimer();

        /// <summary>上一次算出的「每格能放几条」：量不到真实高度时沿用它（避免格子空白）。</summary>
        private int _lastCapacity = DefaultVisibleRows;

        private int _displayYear;
        private int _displayMonth;
        private DateTime _todayCache;
        private DayCellVm _hoverCell;
        private FrameworkElement _hoverElement;
        private HoverPreviewWindow _preview;
        private bool _isExiting;
        private bool _isBoundsRestored;

        /// <summary>是否因「隐藏到托盘且没有其它窗口」而释放过背景图位图（内存方案 B）；唤起时据此决定是否要重建背景。</summary>
        private bool _bgReleasedWhileHidden;

        /// <summary>已订阅 Closed 的其它窗口：它们都关掉后补做一次背景图释放；退订时逐一移除。</summary>
        private readonly List<Window> _bgReleaseHooked = new List<Window>();

        /// <summary>当前单击选中的日期（用于单元格常驻高亮）；null = 未选中。</summary>
        private DateTime? _selectedDate;

        /// <summary>视图模式：month / week。</summary>
        private string _viewMode = "month";

        /// <summary>周视图当前周的周一。</summary>
        private DateTime _weekStart;

        /// <summary>周视图顶部日期行数据（复用月格 DayCellVm，角标统计同源）。</summary>
        private List<DayCellVm> _weekDays = new List<DayCellVm>();

        /// <summary>周视图今日列的 2px 边框覆盖层；不参与布局，选中时隐藏。</summary>
        private readonly Dictionary<DateTime, Border> _weekTodayOverlays =
            new Dictionary<DateTime, Border>();

        /// <summary>周视图每小时像素高度。</summary>
        private const double WeekHourHeight = 40.0;

        private int _weekAxisStart = 7;
        private int _weekAxisEnd = 19;

        /// <summary>手动双击判定：记录上次单元格按下的日期与 UTC 时刻（避免依赖 WPF 易失真的 ClickCount）。</summary>
        private DateTime? _lastClickDate;
        private long _lastClickTicks;
        private bool _suppressSearch;

        public MainWindow()
        {
            InitializeComponent();
            PreviewMouseDown += OnWindowPreviewMouseDown;
            Icon = IconHelper.GetImageSource();
            if (AppIconImage != null)
            {
                AppIconImage.Source = IconHelper.GetImageSource();
            }

            AppServices.MainWindow = this;

            Loaded += OnLoaded;
            Closing += OnClosing;
            Closed += OnClosed;
            StateChanged += OnStateChanged;

            // XAML 是默认尺寸；用户真正拉伸过的尺寸记忆优先。
            RestoreWindowSize();
            ThemeService.ApplyScale(this, Width, Height, MinWidth, MinHeight);
            if (RestoreWindowPosition())
            {
                CenterOnWorkArea();
            }
            _isBoundsRestored = true;
            // 尺寸与位置只在"用户拖完窗口"时记录（SizeChanged/LocationChanged 分不清是谁改的，已弃用）
            WindowSizeTracker.Track(this, delegate { UpdateBoundsToSettings(); });

            AppServices.DataChanged += OnDataChanged;
            AppServices.ScheduleFieldChanged += OnScheduleFieldChanged;
            _hoverTimer.Tick += OnHoverTimerTick;

            CalendarItems.ItemsSource = _cells;
            // 手动缩放窗口、切换缩放档位都会改变格子高度 → 重算每格能显示几条（OPT-023）
            CalendarItems.SizeChanged += OnCalendarSizeChanged;
            // 节流：拖窗口会连续不断触发尺寸变化，等到"手停下来"（100ms 内不再变化）再算一次
            _rowRecalcTimer.Interval = TimeSpan.FromMilliseconds(100);
            _rowRecalcTimer.Tick += OnRowRecalcTick;

            DateTime now = DateTime.Now;
            _todayCache = now.Date;
            _displayYear = now.Year;
            _displayMonth = now.Month;
            ApplyPanelState(AppServices.Settings != null && AppServices.Settings.RightPanelCollapsed);
            RefreshCalendar();

            _viewMode = AppServices.Settings != null
                && string.Equals(AppServices.Settings.ViewMode, "week", StringComparison.OrdinalIgnoreCase)
                ? "week"
                : "month";
            _weekStart = StartOfWeek(_todayCache);
            ApplyViewVisibility();
            if (_viewMode == "week")
            {
                Dispatcher.BeginInvoke(new Action(RenderWeek), DispatcherPriority.Loaded);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 尺寸已在构造函数里定死（见构造函数）；背景由 ThemeService 统一写入资源，无需按窗口应用。
            UpdateMaximizeButtonContent();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            AppServices.DataChanged -= OnDataChanged;
            AppServices.ScheduleFieldChanged -= OnScheduleFieldChanged;
            _hoverTimer.Stop();
            _hoverTimer.Tick -= OnHoverTimerTick;

            if (AppServices.MainWindow == this)
            {
                AppServices.MainWindow = null;
            }
            if (_preview != null)
            {
                _preview.Close();
                _preview = null;
            }
        }

        // ---------------- 日历渲染 ----------------

        /// <summary>
        /// 重建月历：刷新年 / 月标题、按钮可用性、42 格数据（含「今天」标记与过往灰蒙）。
        /// 仅在打开 / 切换月份 / CRUD 后调用。
        /// </summary>
        public void RefreshCalendar()
        {
            DateTime now = DateTime.Now;
            _todayCache = now.Date;

            YearText.Text = CalendarService.YearText(_displayYear);
            MonthText.Text = CalendarService.MonthText(_displayMonth);

            PrevYearButton.IsEnabled = _displayYear > MinYear;
            NextYearButton.IsEnabled = _displayYear < MaxYear;
            PrevMonthButton.IsEnabled = !(_displayYear == MinYear && _displayMonth == 1);
            NextMonthButton.IsEnabled = !(_displayYear == MaxYear && _displayMonth == 12);

            BuildCells();
            // 显示条数依赖「布局之后的格子实际高度」，故排到布局完成后再算一次（幂等，可重复调用）
            Dispatcher.BeginInvoke(new Action(RecomputeVisibleRows), DispatcherPriority.Loaded);
            RefreshTodoPanel();
        }

        /// <summary>生成 42 格（上月尾 + 当月 + 下月头），周一为一周第一天。</summary>
        private void BuildCells()
        {
            _cells.Clear();

            DateTime first = new DateTime(_displayYear, _displayMonth, 1);
            int offset = ((int)first.DayOfWeek + 6) % 7; // 周一 = 0
            DateTime start = first.AddDays(-offset);

            for (int i = 0; i < 42; i++)
            {
                DateTime date = start.AddDays(i);
                DayCellVm cell = new DayCellVm();
                cell.Date = date;
                cell.IsCurrentMonth = date.Month == _displayMonth && date.Year == _displayYear;
                cell.IsToday = date == _todayCache;
                cell.IsPast = date < _todayCache;
                cell.IsSelected = _selectedDate.HasValue && date == _selectedDate.Value;
                cell.SubText = AppServices.Calendar != null ? AppServices.Calendar.GetSubLabel(date) : string.Empty;

                int todoPending = 0;
                if (AppServices.Schedules != null)
                {
                    // 重复日程按 RecurrenceService.IsHit 惰性展开，不预生成实例
                    List<ScheduleOccurrence> occurrences = AppServices.Schedules.GetOccurrences(date);
                    // OPT-023：先收全部条目；显示几条 / 隐藏几条由 RecomputeVisibleRows 按格子高度决定
                    for (int j = 0; j < occurrences.Count; j++)
                    {
                        Schedule schedule = occurrences[j].Schedule;
                        if (schedule == null)
                        {
                            continue;
                        }
                        ScheduleItemVm item = new ScheduleItemVm();
                        item.TimeText = DateUtil.FormatTime(schedule.TimeValue);
                        item.TitleText = schedule.Title; // 过长的显示截断交给 XAML 的 TextTrimming（按宽度自适应）
                        item.BgColor = schedule.BgColor;
                        item.FontColor = schedule.FontColor;
                        cell.AllItems.Add(item);
                        if (schedule.IsTodo && !schedule.IsDone(date))
                        {
                            todoPending++;
                        }
                    }
                }
                cell.TodoPendingCount = date < _todayCache ? todoPending : 0;
                _cells.Add(cell);
            }
        }

        /// <summary>
        /// 按格子「实际可用高度」重算每格显示几条、隐藏几条（OPT-023）。
        /// 容量 = floor(可用高 / 行高)；装不下时再预留 1 行给底部的「+N」。
        /// 触发：日历区尺寸变化（手动缩放窗口 / 切换档位）+ 每次重建数据后（由 RefreshCalendar 排到布局后执行）。
        /// 说明：无需为缩放写专门分支——LayoutTransform 对整棵内容树等比缩放，可用高与行高的比例不变。
        /// </summary>
        private void RecomputeVisibleRows()
        {
            if (CalendarItems.ItemsSource == null)
            {
                // 缩到托盘时日历内容已被卸载（省内存），此时高度必然是 0 ——
                // 真去算会把每格日程全判成「放不下」而只剩「+N」，故直接跳过（回到界面时会重新算）。
                return;
            }

            // 直接量「第一个格子」里日程区的真实高度（OPT-023）：
            // 42 格用的是均分表格（UniformGrid），高度必然相同，所以只量第一个就够，
            // 不再用「格子总高 − 常量」去推算——那样一旦界面布局改动就会静默失准。
            double available = MeasureScheduleAreaHeight();
            int capacity;
            if (available > 0)
            {
                capacity = (int)Math.Floor(available / ScheduleRowHeight);
                if (capacity < 0)
                {
                    capacity = 0;
                }
                _lastCapacity = capacity;
            }
            else
            {
                // 量不到（容器还没生成 / 还没排版 / 界面已卸载）→ 沿用上一次算出的容量
                capacity = _lastCapacity;
            }

            for (int i = 0; i < _cells.Count; i++)
            {
                DayCellVm cell = _cells[i];
                int total = cell.TotalCount;
                int visible = total;
                if (total > capacity)
                {
                    // 溢出：底部要留一行显示「+N」，故可显示条数再减一行
                    visible = available > 0
                        ? (int)Math.Floor((available - ScheduleRowHeight) / ScheduleRowHeight)
                        : capacity - 1;
                    if (visible < 0)
                    {
                        visible = 0;
                    }
                    if (visible > capacity)
                    {
                        visible = capacity;
                    }
                }
                if (visible > total)
                {
                    visible = total;
                }

                List<ScheduleItemVm> shown = new List<ScheduleItemVm>();
                for (int j = 0; j < visible; j++)
                {
                    shown.Add(cell.AllItems[j]);
                }
                cell.Items = shown;
                cell.HiddenCount = total - visible;
            }
        }

        /// <summary>
        /// 量出「第一个格子」里日程区的**真实可用高度**（OPT-023）。
        /// 42 格用均分表格（UniformGrid），高度必然相同，所以只量第一个。
        /// 返回 <= 0 表示这一刻量不到，调用方应沿用上一次的结果。
        ///
        /// 为什么会量不到（即"找不到 / 高度是 0"）：
        /// 1. 容器还没生成：数据刚重建，界面尚未为新数据生成对应元素（虚拟化时尤其常见）；
        /// 2. 元素还没排版：元素已存在，但本轮排版没跑，ActualHeight 仍是 0；
        /// 3. 界面被卸载：缩到托盘时日历内容被卸掉（外层已判空跳过）；
        /// 4. 窗口尚未显示 / 处于隐藏状态：整棵树都不参与排版；
        /// 5. 以后有人改了单元格模板却忘了带 Tag 标记。
        /// 因此必须有兜底（沿用上次容量），不能假设"一定能量到"。
        /// </summary>
        private double MeasureScheduleAreaHeight()
        {
            DependencyObject container = CalendarItems.ItemContainerGenerator.ContainerFromIndex(0);
            FrameworkElement area = FindTaggedDescendant(container, ScheduleAreaTag) as FrameworkElement;
            if (area == null)
            {
                return -1;
            }
            return area.ActualHeight;
        }

        /// <summary>在界面元素树里按 Tag 找后代元素（找不到返回 null）。</summary>
        private static DependencyObject FindTaggedDescendant(DependencyObject parent, string tag)
        {
            if (parent == null)
            {
                return null;
            }
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                FrameworkElement element = child as FrameworkElement;
                if (element != null && string.Equals(element.Tag as string, tag, StringComparison.Ordinal))
                {
                    return element;
                }
                DependencyObject found = FindTaggedDescendant(child, tag);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// 日历区尺寸变化（手动缩放窗口 / 切换档位）→ **延迟**重算每格显示条数。
        /// 拖窗口时这里会被连续触发上百次，故每次都重置计时器，只在停手 100ms 后算最后一次。
        /// </summary>
        private void OnCalendarSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _rowRecalcTimer.Stop();
            _rowRecalcTimer.Start();
        }

        private void OnRowRecalcTick(object sender, EventArgs e)
        {
            _rowRecalcTimer.Stop();
            RecomputeVisibleRows();
        }

        // ---------------- 悬停预览（P0-2 / DEC-4） ----------------

        /// <summary>按设置刷新悬停延迟（下限 0.01s、上限 5s）。</summary>
        private void ApplyHoverDelay()
        {
            double delay = MinHoverDelaySeconds;
            AppSettings settings = AppServices.Settings;
            if (settings != null && !double.IsNaN(settings.HoverDelaySeconds))
            {
                delay = settings.HoverDelaySeconds;
            }
            if (delay < MinHoverDelaySeconds)
            {
                delay = MinHoverDelaySeconds;
            }
            if (delay > MaxHoverDelaySeconds)
            {
                delay = MaxHoverDelaySeconds;
            }
            _hoverTimer.Interval = TimeSpan.FromSeconds(delay);
        }

        private void OnCellMouseEnter(object sender, MouseEventArgs e)
        {
            Border element = sender as Border;
            if (element == null)
            {
                return;
            }
            DayCellVm cell = element.DataContext as DayCellVm;
            if (cell == null)
            {
                return;
            }

            _hoverCell = cell;
            _hoverElement = element;

            // 该日无日程 → 不弹出预览浮层
            if (!cell.HasSchedule)
            {
                _hoverTimer.Stop();
                return;
            }

            ApplyHoverDelay();
            _hoverTimer.Stop();
            _hoverTimer.Start();
        }

        private void OnCellMouseLeave(object sender, MouseEventArgs e)
        {
            _hoverTimer.Stop();
            _hoverCell = null;
            _hoverElement = null;
            HidePreview();
        }

        private void OnHoverTimerTick(object sender, EventArgs e)
        {
            _hoverTimer.Stop();
            if (_hoverCell == null || _hoverElement == null || !_hoverCell.HasSchedule)
            {
                return;
            }

            // 单元格左上角在屏幕上的位置（DIP，与 ScreenHelper.GetWorkArea 同一坐标系）
            Point origin = _hoverElement.TransformToVisual(this).Transform(new Point(0, 0));
            double left = Left + origin.X;
            double top = Top + origin.Y;

            if (_preview == null)
            {
                _preview = new HoverPreviewWindow();
            }
            _preview.ShowPreview(_hoverCell, left, top, _hoverElement.ActualWidth, _hoverElement.ActualHeight);
        }

        private void HidePreview()
        {
            if (_preview != null)
            {
                _preview.HidePreview();
            }
        }

        // ---------------- 单元格交互 ----------------

        /// <summary>
        /// 单元格鼠标按下：
        /// - 单击：切换「选中态」——选中该日并用焦点边框色常驻高亮；再次单击同一天则取消。
        /// - 双击：打开该日编辑窗（单击那一下会顺带选中该日，无妨）。
        /// 说明：格子是 Border（无 MouseDoubleClick 事件），且 WPF 的 ClickCount 按「实际命中的
        /// 最里层元素」计数——有日程的格子内部嵌了条目元素，两次单击若命中不同子元素，
        /// ClickCount 会归 1，导致有日程的日子双击触发不了开窗。
        /// 因此在单元格层面手动判定双击（同一天 300ms 内再次按下即开窗）。
        /// </summary>
        private void OnCellMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DayCellVm cell = GetCell(sender);
            if (cell == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            bool isDouble = _lastClickDate.HasValue
                            && _lastClickDate.Value == cell.Date
                            && (now.Ticks - _lastClickTicks) <= TimeSpan.FromMilliseconds(300).Ticks;

            if (isDouble)
            {
                // 消费掉这次双击，避免三连击误判；双击只开窗，不切换选中态
                _lastClickDate = null;
                e.Handled = true;
                HidePreview();
                OpenEditWindow(cell.Date);
                return;
            }

            // 单击：记录候选双击，并执行选中态切换
            _lastClickDate = cell.Date;
            _lastClickTicks = now.Ticks;
            if (_selectedDate.HasValue && _selectedDate.Value == cell.Date)
            {
                ClearSelection();
            }
            else
            {
                _selectedDate = cell.Date;
                UpdateSelection();
            }
        }

        /// <summary>
        /// 右键单元格：**只选中、不取消**（OPT-014）——让右键与左键的预存路径收敛：
        /// 右击后该格立刻有选中高亮，随后右键菜单「新建日程」与左列「＋ 新建」结果一致。
        /// 双击判定只由左键维护（_lastClickDate），不受本处理器影响。
        /// </summary>
        private void OnCellMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DayCellVm cell = GetCell(sender);
            if (cell == null)
            {
                return;
            }
            _selectedDate = cell.Date;
            UpdateSelection();
        }

        /// <summary>根据 _selectedDate 刷新 42 个单元格的 IsSelected（常驻高亮）。</summary>
        private void UpdateSelection()
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                _cells[i].IsSelected = _selectedDate.HasValue && _cells[i].Date == _selectedDate.Value;
            }
            UpdateWeekSelection();
            RefreshTodoPanel();
        }

        // ---------------- 周视图（宽松布局） ----------------

        private static DateTime StartOfWeek(DateTime date)
        {
            int offset = ((int)date.DayOfWeek + 6) % 7; // 周一 = 0
            return date.Date.AddDays(-offset);
        }

        private void OnToggleViewClick(object sender, RoutedEventArgs e)
        {
            SetViewMode(_viewMode == "week" ? "month" : "week", true);
        }

        private void SetViewMode(string mode, bool save)
        {
            _viewMode = string.Equals(mode, "week", StringComparison.OrdinalIgnoreCase) ? "week" : "month";
            ApplyViewVisibility();

            // 无选中：画面回到今天，但不选中今天；有选中：锚定选中日期并保留高亮
            DateTime anchor = _selectedDate ?? _todayCache;
            if (_viewMode == "week")
            {
                _weekStart = StartOfWeek(anchor);
                Dispatcher.BeginInvoke(new Action(RenderWeek), DispatcherPriority.Loaded);
            }
            else
            {
                _displayYear = anchor.Year;
                _displayMonth = anchor.Month;
                RefreshCalendar();
            }

            if (save && AppServices.Settings != null)
            {
                AppServices.Settings.ViewMode = _viewMode;
                SettingsStore.Save(AppServices.Settings);
            }
        }

        private void ApplyViewVisibility()
        {
            bool week = _viewMode == "week";
            if (MonthView != null)
            {
                MonthView.Visibility = week ? Visibility.Collapsed : Visibility.Visible;
            }
            if (WeekView != null)
            {
                WeekView.Visibility = week ? Visibility.Visible : Visibility.Collapsed;
            }
            if (LooseIconPath != null)
            {
                // 月视图显示「月→周」图标；周视图显示「周→月」图标
                LooseIconPath.Data = (Geometry)FindResource(week ? "IconMonthLayout" : "IconLooseLayout");
            }
        }

        private void OnPrevWeekClick(object sender, RoutedEventArgs e)
        {
            DateTime next = _weekStart.AddDays(-7);
            if (next < DateUtil.MinSupportedDate)
            {
                return;
            }
            _weekStart = next;
            UpdateSelection();
            RenderWeek();
        }

        private void OnNextWeekClick(object sender, RoutedEventArgs e)
        {
            DateTime next = _weekStart.AddDays(7);
            if (next.AddDays(6) > DateUtil.MaxSupportedDate)
            {
                return;
            }
            _weekStart = next;
            UpdateSelection();
            RenderWeek();
        }

        private void OnWeekPrevMonthClick(object sender, RoutedEventArgs e)
        {
            JumpWeekMonth(-1);
        }

        private void OnWeekNextMonthClick(object sender, RoutedEventArgs e)
        {
            JumpWeekMonth(1);
        }

        /// <summary>月份箭头 = 当前周 ±28 天；不重算月初，可能仍落在同一个月。</summary>
        private void JumpWeekMonth(int delta)
        {
            DateTime target = _weekStart.AddDays(delta * 28);
            if (target < DateUtil.MinSupportedDate || target > DateUtil.MaxSupportedDate)
            {
                return;
            }

            _weekStart = target;
            UpdateSelection();
            RenderWeek();
        }

        /// <summary>周视图：按 _weekStart 逐日取 7 天，动态纵轴 + Canvas 绝对定位日程条。</summary>
        private void RenderWeek()
        {
            if (WeekBody == null || WeekAxis == null)
            {
                return;
            }

            _weekTodayOverlays.Clear();

            // 日期头固定 44px，剩余高度给日程区；不滚动
            double availableHeight = WeekBody.ActualHeight > 44 ? WeekBody.ActualHeight - 44 : 400;
            DateTime weekStart = _weekStart;
            DateTime weekEnd = weekStart.AddDays(6);

            WeekMonthText.Text = weekStart.Month == weekEnd.Month && weekStart.Year == weekEnd.Year
                ? weekStart.Month + " 月"
                : weekStart.Month + " 月 / " + weekEnd.Month + " 月";
            WeekRangeText.Text = weekStart.Day + "日 – " + weekEnd.Day + "日";
            PrevWeekButton.IsEnabled = weekStart > DateUtil.MinSupportedDate;
            NextWeekButton.IsEnabled = weekEnd < DateUtil.MaxSupportedDate;

            // 月份箭头 = 当前周 ±28 天；超出支持范围时禁用
            WeekPrevMonthButton.IsEnabled = weekStart.AddDays(-28) >= DateUtil.MinSupportedDate;
            WeekNextMonthButton.IsEnabled = weekStart.AddDays(28) <= DateUtil.MaxSupportedDate;

            List<List<ScheduleOccurrence>> dayData = new List<List<ScheduleOccurrence>>();
            List<DayCellVm> days = new List<DayCellVm>();
            int minMinutes = 24 * 60;
            int maxMinutes = -1;

            for (int i = 0; i < 7; i++)
            {
                DateTime date = weekStart.AddDays(i);
                List<ScheduleOccurrence> occurrences = AppServices.Schedules != null
                    ? AppServices.Schedules.GetOccurrences(date)
                    : new List<ScheduleOccurrence>();
                dayData.Add(occurrences);

                DayCellVm day = new DayCellVm();
                day.Date = date;
                day.SubText = AppServices.Calendar != null ? AppServices.Calendar.GetSubLabel(date) : string.Empty;
                day.IsToday = date == _todayCache;
                day.IsSelected = _selectedDate.HasValue && _selectedDate.Value == date;
                day.TodoPendingCount = CountPendingTodos(date);
                days.Add(day);

                for (int j = 0; j < occurrences.Count; j++)
                {
                    Schedule schedule = occurrences[j].Schedule;
                    if (schedule == null)
                    {
                        continue;
                    }
                    int minutes = schedule.TimeValue.Hours * 60 + schedule.TimeValue.Minutes;
                    if (minutes < minMinutes)
                    {
                        minMinutes = minutes;
                    }
                    if (minutes > maxMinutes)
                    {
                        maxMinutes = minutes;
                    }
                }
            }

            _weekDays = days;

            int axisStart;
            int axisEnd;
            if (minMinutes > maxMinutes)
            {
                // 空周：固定 7:00–19:00
                axisStart = 7;
                axisEnd = 19;
            }
            else if (minMinutes >= 7 * 60 && maxMinutes <= 19 * 60)
            {
                // 日程全部落在默认窗口内：优先 7:00–19:00，不无脑扩到 24:00
                axisStart = 7;
                axisEnd = 19;
            }
            else
            {
                axisStart = Math.Max(0, minMinutes / 60 - 1);
                axisEnd = Math.Min(24, maxMinutes / 60 + 1);

                // 不足 12 小时：拉满 12 小时。
                // 先后扩；后扩到 24:00 仍不足时再前扩。两个候选都有效时，选更接近 7–19 的窗口。
                if (axisEnd - axisStart < 12)
                {
                    int forwardStart = axisStart;
                    int forwardEnd = Math.Min(24, forwardStart + 12);
                    int backwardEnd = axisEnd;
                    int backwardStart = Math.Max(0, backwardEnd - 12);
                    bool forwardValid = forwardEnd - forwardStart >= 12;
                    bool backwardValid = backwardEnd - backwardStart >= 12;

                    if (forwardValid && backwardValid)
                    {
                        int forwardScore = Math.Abs(forwardStart - 7) + Math.Abs(forwardEnd - 19);
                        int backwardScore = Math.Abs(backwardStart - 7) + Math.Abs(backwardEnd - 19);
                        if (backwardScore < forwardScore)
                        {
                            axisStart = backwardStart;
                            axisEnd = backwardEnd;
                        }
                        else
                        {
                            axisStart = forwardStart;
                            axisEnd = forwardEnd;
                        }
                    }
                    else if (forwardValid)
                    {
                        axisStart = forwardStart;
                        axisEnd = forwardEnd;
                    }
                    else if (backwardValid)
                    {
                        axisStart = backwardStart;
                        axisEnd = backwardEnd;
                    }
                    else
                    {
                        axisStart = 0;
                        axisEnd = 12;
                    }
                }
            }
            _weekAxisStart = axisStart;
            _weekAxisEnd = axisEnd;
            // 不滚动：按当前可用高度均分每小时；上下各留 16px，最早刻度不会被裁
            double hourHeight = Math.Max(12, (availableHeight - 32) / (axisEnd - axisStart + 1));
            double totalHeight = availableHeight;

            WeekBody.Children.Clear();
            WeekAxis.Children.Clear();
            WeekBody.Children.Add(WeekAxis);
            Grid.SetRow(WeekAxis, 1);
            Grid.SetColumn(WeekAxis, 0);
            WeekAxis.Height = totalHeight;

            // 7 条真实 1px 分隔列：时间/周一 + 周一/周二 ... 周六/周日
            Brush separatorBrush = (Brush)FindResource("BorderWeakBrush");
            for (int i = 0; i < 7; i++)
            {
                Border separator = new Border();
                separator.SetValue(Grid.ColumnProperty, i * 2 + 1);
                separator.SetValue(Grid.RowSpanProperty, 2);
                separator.Background = separatorBrush;
                separator.Opacity = 0.4;
                separator.SnapsToDevicePixels = true;
                separator.IsHitTestVisible = false;
                WeekBody.Children.Add(separator);
            }

            for (int hour = axisStart; hour <= axisEnd; hour += 2)
            {
                AddWeekAxisLabel(hour, axisStart, hourHeight);
            }
            if ((axisEnd - axisStart) % 2 != 0)
            {
                AddWeekAxisLabel(axisEnd, axisStart, hourHeight);
            }

            ScheduleBgBrushConverter bgConverter = new ScheduleBgBrushConverter();
            ScheduleFontBrushConverter fontConverter = new ScheduleFontBrushConverter();

            for (int i = 0; i < 7; i++)
            {
                DateTime date = weekStart.AddDays(i);
                Border column = new Border();
                column.SetValue(Grid.ColumnProperty, i * 2 + 2);
                column.SetValue(Grid.RowSpanProperty, 2);
                column.Tag = date;
                column.Background = Brushes.Transparent;
                column.BorderBrush = (Brush)FindResource("BorderWeakBrush");
                // 未选中/非今天不画列边框；普通竖线由 1px 分隔列负责
                column.BorderThickness = new Thickness(0);
                column.ClipToBounds = false;
                column.MouseLeftButtonDown += OnWeekColumnMouseLeftButtonDown;
                column.MouseRightButtonDown += OnWeekColumnMouseRightButtonDown;
                column.MouseEnter += OnWeekColumnMouseEnter;
                column.MouseLeave += OnCellMouseLeave;
                column.ContextMenu = CreateWeekContextMenu();

                if (date == _todayCache)
                {
                    column.Background = (Brush)FindResource("WeekTodayGlowBrush");
                    column.BorderBrush = (Brush)FindResource("BorderWeakBrush");
                    // 今日边框不放在这个承担布局的 Border 上，避免 2px 边框挤压内容高度。
                    column.BorderThickness = new Thickness(0);
                }
                if (_selectedDate.HasValue && _selectedDate.Value == date)
                {
                    column.BorderBrush = (Brush)FindResource("FocusBorderBrush");
                    column.BorderThickness = new Thickness(2);
                }

                Grid columnContent = new Grid();
                columnContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
                columnContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                ContentControl header = new ContentControl();
                header.Content = days[i];
                header.ContentTemplate = (DataTemplate)FindResource("WeekDayHeaderTemplate");
                header.ClipToBounds = false;
                Grid.SetRow(header, 0);
                columnContent.Children.Add(header);

                // 12:00 虚线：只画在日期列内，位于日程条下层；1px + 低透明度 + 像素对齐
                Canvas noonCanvas = new Canvas();
                noonCanvas.ClipToBounds = true;
                noonCanvas.IsHitTestVisible = false;
                Grid.SetRow(noonCanvas, 1);
                columnContent.Children.Add(noonCanvas);

                if (axisStart <= 12 && axisEnd >= 12)
                {
                    double noonY = Math.Round(16 + (12 * 60 - axisStart * 60.0) / 60.0 * hourHeight) + 0.5;
                    Line noonLine = new Line();
                    noonLine.X1 = 0;
                    noonLine.X2 = 100;
                    noonLine.Y1 = noonY;
                    noonLine.Y2 = noonY;
                    noonLine.Stroke = (Brush)FindResource("BorderWeakBrush");
                    noonLine.StrokeThickness = 1;
                    noonLine.Opacity = 0.2;
                    noonLine.StrokeDashArray = new DoubleCollection { 2, 4 };
                    noonLine.SnapsToDevicePixels = true;
                    noonLine.IsHitTestVisible = false;
                    noonCanvas.Children.Add(noonLine);
                    column.SizeChanged += delegate
                    {
                        noonLine.X2 = Math.Max(0, column.ActualWidth);
                        noonCanvas.Width = column.ActualWidth;
                    };
                }

                Canvas lane = new Canvas();
                lane.Height = totalHeight;
                lane.ClipToBounds = true;
                Grid.SetRow(lane, 1);
                columnContent.Children.Add(lane);

                if (date == _todayCache)
                {
                    Border todayBorder = new Border();
                    todayBorder.BorderBrush = (Brush)FindResource("TodayBorderBrush");
                    todayBorder.BorderThickness = new Thickness(2);
                    todayBorder.Background = Brushes.Transparent;
                    todayBorder.IsHitTestVisible = false;
                    todayBorder.SnapsToDevicePixels = true;
                    todayBorder.Visibility = _selectedDate.HasValue && _selectedDate.Value == date
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                    Grid.SetRowSpan(todayBorder, 2);
                    columnContent.Children.Add(todayBorder);
                    _weekTodayOverlays[date] = todayBorder;
                }

                column.Child = columnContent;

                List<ScheduleOccurrence> occurrences = dayData[i];
                TimeSpan lastTime = TimeSpan.MinValue;
                int stack = 0;
                for (int j = 0; j < occurrences.Count; j++)
                {
                    Schedule schedule = occurrences[j].Schedule;
                    if (schedule == null)
                    {
                        continue;
                    }

                    if (schedule.TimeValue == lastTime)
                    {
                        stack++;
                    }
                    else
                    {
                        stack = 0;
                        lastTime = schedule.TimeValue;
                    }

                    double top = 16 + (schedule.TimeValue.TotalMinutes - axisStart * 60.0)
                                 / 60.0 * hourHeight - 7 + stack * 15;

                    Border bar = new Border();
                    bar.Height = 14;
                    bar.CornerRadius = new CornerRadius(4);
                    bar.Padding = new Thickness(3, 0, 3, 0);
                    bar.Width = 100;
                    // 周视图过期日程暂不透明；保留旧方案，后续若要恢复直接取消下一行注释。
                    // bar.Opacity = occurrences[j].IsPast ? 0.5 : 1.0;
                    bar.Opacity = 1.0;
                    bar.Background = bgConverter.Convert(schedule.BgColor, typeof(Brush), null,
                        System.Globalization.CultureInfo.CurrentCulture) as Brush ?? Brushes.Transparent;

                    TextBlock text = new TextBlock();
                    text.Text = DateUtil.FormatTime(schedule.TimeValue) + " " + schedule.Title;
                    text.TextTrimming = TextTrimming.CharacterEllipsis;
                    text.VerticalAlignment = VerticalAlignment.Center;
                    text.Foreground = fontConverter.Convert(schedule.FontColor, typeof(Brush), null,
                        System.Globalization.CultureInfo.CurrentCulture) as Brush
                        ?? (Brush)FindResource("TextPrimaryBrush");
                    text.SetResourceReference(TextBlock.FontSizeProperty, "FontBodyS");
                    text.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");

                    bar.Child = text;
                    Canvas.SetLeft(bar, 3);
                    Canvas.SetTop(bar, top);
                    lane.Children.Add(bar);
                }

                column.SizeChanged += delegate
                {
                    double width = Math.Max(20, column.ActualWidth - 6);
                    for (int k = 0; k < lane.Children.Count; k++)
                    {
                        Border bar = lane.Children[k] as Border;
                        if (bar != null)
                        {
                            bar.Width = width;
                        }
                    }
                };

                WeekBody.Children.Add(column);
            }

        }

        /// <summary>窗口/面板/界面缩放导致宽或高变化时重建周视图，避免绝对坐标残留。</summary>
        private void OnWeekSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewMode == "week" && e.NewSize.Width > 0 && e.NewSize.Height > 0
                && (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1
                    || Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 1))
            {
                RenderWeek();
            }
        }

        private void AddWeekAxisLabel(int hour, int axisStart, double hourHeight)
        {
            TextBlock label = new TextBlock();
            label.Text = hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + ":00";
            label.SetResourceReference(TextBlock.FontSizeProperty, "FontBodyS");
            label.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");
            label.Foreground = (Brush)FindResource("TextSecondaryBrush");
            if (hour == 12)
            {
                label.FontWeight = FontWeights.SemiBold;
            }
            Canvas.SetLeft(label, 2);
            Canvas.SetTop(label, 16 + (hour - axisStart) * hourHeight - 8);
            WeekAxis.Children.Add(label);
        }

        private ContextMenu CreateWeekContextMenu()
        {
            ContextMenu menu = new ContextMenu();
            MenuItem create = new MenuItem();
            create.Header = "新建日程";
            create.Click += OnWeekCreateClick;
            MenuItem open = new MenuItem();
            open.Header = "打开该日";
            open.Click += OnWeekOpenClick;
            menu.Items.Add(create);
            menu.Items.Add(open);
            return menu;
        }

        private void OnWeekCreateClick(object sender, RoutedEventArgs e)
        {
            HidePreview();
            CreateWindow window = new CreateWindow(_selectedDate);
            window.Show();
        }

        private void OnWeekOpenClick(object sender, RoutedEventArgs e)
        {
            if (_selectedDate.HasValue)
            {
                HidePreview();
                OpenEditWindow(_selectedDate.Value);
            }
        }

        private void OnWeekDayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            HandleWeekDayClick(sender, e);
        }

        private void OnWeekColumnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            HandleWeekDayClick(sender, e);
        }

        private void HandleWeekDayClick(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            if (element == null || !(element.Tag is DateTime))
            {
                return;
            }

            DateTime date = (DateTime)element.Tag;
            DateTime now = DateTime.UtcNow;
            bool isDouble = _lastClickDate.HasValue
                            && _lastClickDate.Value == date
                            && (now.Ticks - _lastClickTicks) <= TimeSpan.FromMilliseconds(300).Ticks;

            if (isDouble)
            {
                _lastClickDate = null;
                e.Handled = true;
                HidePreview();
                OpenEditWindow(date);
                return;
            }

            _lastClickDate = date;
            _lastClickTicks = now.Ticks;
            if (_selectedDate.HasValue && _selectedDate.Value == date)
            {
                ClearSelection();
            }
            else
            {
                _selectedDate = date;
                UpdateSelection();
            }
            e.Handled = true;
        }

        private void OnWeekColumnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            if (element != null && element.Tag is DateTime)
            {
                _selectedDate = (DateTime)element.Tag;
                UpdateSelection();
            }
        }

        /// <summary>悬停周列整列：复用现有 HoverPreviewWindow，不新增预览窗体。</summary>
        private void OnWeekColumnMouseEnter(object sender, MouseEventArgs e)
        {
            Border column = sender as Border;
            if (column == null || !(column.Tag is DateTime))
            {
                return;
            }
            DateTime date = (DateTime)column.Tag;
            List<ScheduleOccurrence> occurrences = AppServices.Schedules != null
                ? AppServices.Schedules.GetOccurrences(date)
                : new List<ScheduleOccurrence>();
            if (occurrences.Count == 0)
            {
                _hoverTimer.Stop();
                return;
            }

            DayCellVm cell = new DayCellVm();
            cell.Date = date;
            cell.AllItems = new List<ScheduleItemVm>();
            for (int i = 0; i < occurrences.Count; i++)
            {
                Schedule schedule = occurrences[i].Schedule;
                if (schedule == null)
                {
                    continue;
                }
                ScheduleItemVm item = new ScheduleItemVm();
                item.TimeText = DateUtil.FormatTime(schedule.TimeValue);
                item.TitleText = schedule.Title;
                item.BgColor = schedule.BgColor;
                item.FontColor = schedule.FontColor;
                cell.AllItems.Add(item);
            }
            if (cell.AllItems.Count == 0)
            {
                _hoverTimer.Stop();
                return;
            }

            _hoverCell = cell;
            _hoverElement = column;
            ApplyHoverDelay();
            _hoverTimer.Stop();
            _hoverTimer.Start();
        }

        private void UpdateWeekSelection()
        {
            if (_weekDays != null)
            {
                for (int i = 0; i < _weekDays.Count; i++)
                {
                    _weekDays[i].IsSelected = _selectedDate.HasValue
                                             && _weekDays[i].Date == _selectedDate.Value;
                }
            }

            // 周列本身也要随单击立即更新边框，才能和月格「整格高亮」一致
            if (WeekBody == null)
            {
                return;
            }
            for (int i = 0; i < WeekBody.Children.Count; i++)
            {
                Border column = WeekBody.Children[i] as Border;
                if (column == null || !(column.Tag is DateTime))
                {
                    continue;
                }
                DateTime date = (DateTime)column.Tag;
                bool selected = _selectedDate.HasValue && _selectedDate.Value == date;
                bool today = date == _todayCache;

                Border todayBorder;
                if (_weekTodayOverlays.TryGetValue(date, out todayBorder))
                {
                    todayBorder.Visibility = today && !selected
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                // 选中态仍使用外层 Border 的 2px 边框，允许内容缩进；
                // 今日未选中时边框完全由覆盖层绘制，不参与布局。
                column.BorderBrush = selected
                    ? (Brush)FindResource("FocusBorderBrush")
                    : (Brush)FindResource("BorderWeakBrush");
                column.BorderThickness = selected
                    ? new Thickness(2)
                    : new Thickness(0);
            }
        }

        /// <summary>字段级变化（勾选完成 / 旗帜）：只刷角标与右侧面板，不重建整月。</summary>
        private void OnScheduleFieldChanged(object sender, EventArgs e)
        {
            RefreshTodoBadges();
            if (_weekDays != null)
            {
                for (int i = 0; i < _weekDays.Count; i++)
                {
                    _weekDays[i].TodoPendingCount = CountPendingTodos(_weekDays[i].Date);
                }
            }
            RefreshTodoPanel();
        }

        /// <summary>重算 42 格的未完成待办角标（仅过去日期）。</summary>
        private void RefreshTodoBadges()
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                DayCellVm cell = _cells[i];
                cell.TodoPendingCount = CountPendingTodos(cell.Date);
            }
        }

        private int CountPendingTodos(DateTime date)
        {
            if (date >= _todayCache || AppServices.Schedules == null)
            {
                return 0;
            }
            int count = 0;
            List<ScheduleOccurrence> occurrences = AppServices.Schedules.GetOccurrences(date);
            for (int i = 0; i < occurrences.Count; i++)
            {
                Schedule schedule = occurrences[i].Schedule;
                if (schedule != null && schedule.IsTodo && !schedule.IsDone(date))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>刷新右侧「当日待办事项」面板：显示选中日（未选中回退今天）。</summary>
        private void RefreshTodoPanel()
        {
            if (TodoItems == null || ScheduleItems == null)
            {
                return;
            }
            DateTime date = _selectedDate ?? _todayCache;
            List<TodoItemVm> todoRows = new List<TodoItemVm>();
            List<TodoItemVm> scheduleRows = new List<TodoItemVm>();
            if (AppServices.Schedules != null)
            {
                List<ScheduleOccurrence> occurrences = AppServices.Schedules.GetOccurrences(date);
                for (int i = 0; i < occurrences.Count; i++)
                {
                    Schedule schedule = occurrences[i].Schedule;
                    if (schedule == null)
                    {
                        continue;
                    }
                    TodoItemVm row = new TodoItemVm();
                    row.Schedule = schedule;
                    row.OccurrenceDate = date;
                    row.TimeText = DateUtil.FormatTime(schedule.TimeValue);
                    row.TitleText = schedule.Title;
                    row.BgColor = schedule.BgColor;
                    row.FontColor = schedule.FontColor;
                    row.CheckBrush = ThemeService.ScheduleCheckBrush(schedule.BgColor);
                    row.HoverBrush = ThemeService.ScheduleHoverBrush(schedule.BgColor);
                    row.IsDone = schedule.IsDone(date);
                    if (schedule.IsTodo)
                    {
                        todoRows.Add(row);
                    }
                    else
                    {
                        scheduleRows.Add(row);
                    }
                }
            }

            if (PanelDateText != null)
            {
                PanelDateText.Text = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }
            if (PanelSubText != null)
            {
                PanelSubText.Text = AppServices.Calendar != null
                    ? AppServices.Calendar.GetPanelSubLabel(date)
                    : string.Empty;
            }

            TodoItems.ItemsSource = todoRows;
            ScheduleItems.ItemsSource = scheduleRows;
            if (TodoCountText != null)
            {
                TodoCountText.Text = BuildCountText(todoRows);
            }
            if (ScheduleCountText != null)
            {
                ScheduleCountText.Text = BuildCountText(scheduleRows);
            }
            if (TodoEmptyHint != null)
            {
                TodoEmptyHint.Visibility = todoRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (ScheduleEmptyHint != null)
            {
                ScheduleEmptyHint.Visibility = scheduleRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static string BuildCountText(List<TodoItemVm> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return string.Empty;
            }
            int done = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsDone)
                {
                    done++;
                }
            }
            if (done == rows.Count)
            {
                return "全部完成";
            }
            return done > 0 ? "共 " + rows.Count + " 项 · 已完成 " + done : "共 " + rows.Count + " 项";
        }

        private void OnTodoChecked(object sender, RoutedEventArgs e)
        {
            SetTodoDone(sender, true);
        }

        private void OnTodoUnchecked(object sender, RoutedEventArgs e)
        {
            SetTodoDone(sender, false);
        }

        private void SetTodoDone(object sender, bool done)
        {
            CheckBox box = sender as CheckBox;
            TodoItemVm row = box != null ? box.DataContext as TodoItemVm : null;
            if (row == null || AppServices.Schedules == null)
            {
                return;
            }
            SaveOutcome outcome = AppServices.Schedules.SetDone(
                row.Schedule, row.OccurrenceDate, done);
            if (outcome != SaveOutcome.Ok)
            {
                DialogService.ShowSaveFailure(outcome, "schedules.json");
            }
        }

        private void OnPanelToggleClick(object sender, RoutedEventArgs e)
        {
            bool collapse = TodoPanelColumn.Width.Value > 0;
            ApplyPanelState(collapse);
            AppSettings settings = AppServices.Settings;
            if (settings != null)
            {
                settings.RightPanelCollapsed = collapse;
                SettingsStore.Save(settings);
            }
        }

        private void ApplyPanelState(bool collapsed)
        {
            if (TodoPanelColumn == null || PanelToggleIcon == null)
            {
                return;
            }
            TodoPanelColumn.Width = collapsed
                ? new GridLength(0)
                : new GridLength(0.32, GridUnitType.Star);
            if (TodoPanel != null)
            {
                TodoPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            }
            PanelToggleIcon.Data = (Geometry)FindResource(collapsed ? "IconPanelCollapse" : "IconPanelExpand");
            if (PanelToggleButton != null)
            {
                PanelToggleButton.ToolTip = collapsed ? "展开右侧栏" : "收起右侧栏";
            }
        }

        /// <summary>取消单元格选中态。</summary>
        private void ClearSelection()
        {
            if (!_selectedDate.HasValue)
            {
                return;
            }
            _selectedDate = null;
            UpdateSelection();
        }

        /// <summary>点击日历主体内「非单元格」区域（左列按钮 / 月份导航 / 星期表头 / 空白）时取消选中。</summary>
        private void OnBodyMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (WeekView != null && WeekView.IsMouseOver)
            {
                return;
            }
            DependencyObject obj = e.OriginalSource as DependencyObject;
            while (obj != null)
            {
                FrameworkElement fe = obj as FrameworkElement;
                if (fe != null && fe.DataContext is DayCellVm)
                {
                    return; // 点在单元格内，选中逻辑由单元格处理器负责，这里不取消
                }
                obj = VisualTreeHelper.GetParent(obj);
            }
            ClearSelection();
        }

        private void OnCellCreateClick(object sender, RoutedEventArgs e)
        {
            DayCellVm cell = GetCellFromMenu(sender);
            if (cell == null)
            {
                return;
            }
            HidePreview();
            CreateWindow window = new CreateWindow(cell.Date);
            window.Show();
        }

        private void OnCellOpenClick(object sender, RoutedEventArgs e)
        {
            DayCellVm cell = GetCellFromMenu(sender);
            if (cell == null)
            {
                return;
            }
            HidePreview();
            OpenEditWindow(cell.Date);
        }

        private static DayCellVm GetCell(object sender)
        {
            FrameworkElement element = sender as FrameworkElement;
            if (element == null)
            {
                return null;
            }
            return element.DataContext as DayCellVm;
        }

        private static DayCellVm GetCellFromMenu(object sender)
        {
            MenuItem item = sender as MenuItem;
            if (item == null)
            {
                return null;
            }
            return item.DataContext as DayCellVm;
        }

        /// <summary>打开某日的日程编辑窗（同一日期复用一个非模态实例）。</summary>
        public void OpenEditWindow(DateTime date)
        {
            EditWindow window;
            if (_editWindows.TryGetValue(date, out window) && window != null)
            {
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }
                window.Activate();
                return;
            }

            // 全局只允许存在一个编辑窗：打开新日期之前，先关掉已经开着的。
            CloseAllEditWindows();

            window = new EditWindow(date);
            _editWindows[date] = window;
            _editDates[window] = date;
            window.Closed += OnEditWindowClosed;
            window.Show();
        }

        /// <summary>
        /// 关闭所有已打开的编辑窗（用于「全局只允许一个编辑窗」）。
        /// 先取消 Closed 事件订阅再关闭，避免关闭过程中字典被两处同时改动。
        /// </summary>
        private void CloseAllEditWindows()
        {
            if (_editWindows.Count == 0)
            {
                return;
            }
            List<EditWindow> opened = new List<EditWindow>(_editWindows.Values);
            for (int i = 0; i < opened.Count; i++)
            {
                EditWindow window = opened[i];
                if (window == null)
                {
                    continue;
                }
                window.Closed -= OnEditWindowClosed;
                window.Close();
            }
            _editWindows.Clear();
            _editDates.Clear();
        }

        private void OnEditWindowClosed(object sender, EventArgs e)
        {
            EditWindow window = sender as EditWindow;
            if (window == null)
            {
                return;
            }
            DateTime date;
            if (_editDates.TryGetValue(window, out date))
            {
                _editWindows.Remove(date);
                _editDates.Remove(window);
            }
            window.Closed -= OnEditWindowClosed;
        }

        // ---------------- 年 / 月切换 ----------------

        // ---------------- 标题栏搜索 ----------------

        public sealed class SearchHit
        {
            public DateTime At { get; set; }
            public string Title { get; set; }
            public string Repeat { get; set; }

            public string DateText
            {
                get { return At.Month + " 月 " + At.Day + " 日"; }
            }

            public Visibility RepeatVisibility
            {
                get
                {
                    return string.IsNullOrEmpty(Repeat)
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }
            }
        }

        private void OnSearchToggleClick(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Visibility == Visibility.Visible)
            {
                CollapseSearch();
            }
            else
            {
                ExpandSearch();
            }
        }

        private void ExpandSearch()
        {
            SearchBox.Visibility = Visibility.Visible;
            SearchHost.Background = ThemeService.SearchFillBrush(false);
            SearchBox.Focus();
        }

        private void CollapseSearch()
        {
            _suppressSearch = true;
            try
            {
                SearchPanel.IsOpen = false;
                SearchUpcoming.ItemsSource = null;
                SearchExpired.ItemsSource = null;
                SearchBox.Text = string.Empty;
                SearchBox.Visibility = Visibility.Collapsed;
                SearchHost.Background = null;
            }
            finally
            {
                _suppressSearch = false;
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSearch)
            {
                return;
            }
            RunSearch();
        }

        private void RunSearch()
        {
            string keyword = SearchBox.Text != null ? SearchBox.Text.Trim() : string.Empty;
            if (keyword.Length == 0)
            {
                SearchPanel.IsOpen = false;
                SearchUpcoming.ItemsSource = null;
                SearchExpired.ItemsSource = null;
                SearchDivider.Visibility = Visibility.Collapsed;
                SearchEmpty.Visibility = Visibility.Collapsed;
                return;
            }

            List<SearchHit> upcoming;
            List<SearchHit> expired;
            BuildSearchHits(keyword, out upcoming, out expired);
            SearchUpcoming.ItemsSource = upcoming;
            SearchExpired.ItemsSource = expired;
            SearchDivider.Visibility = expired.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            SearchEmpty.Visibility = upcoming.Count == 0 && expired.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            SearchPanel.IsOpen = true;
        }

        private static void BuildSearchHits(
            string keyword, out List<SearchHit> upcoming, out List<SearchHit> expired)
        {
            upcoming = new List<SearchHit>();
            expired = new List<SearchHit>();
            if (AppServices.Repository == null || string.IsNullOrEmpty(keyword))
            {
                return;
            }

            DateTime now = DateTime.Now;
            DateTime today = now.Date;
            List<Schedule> all = AppServices.Repository.All;
            for (int i = 0; i < all.Count; i++)
            {
                Schedule schedule = all[i];
                if (schedule == null || schedule.Title == null
                    || schedule.Title.IndexOf(keyword,
                        StringComparison.CurrentCultureIgnoreCase) < 0)
                {
                    continue;
                }

                bool isExpired = ScheduleService.IsExpired(schedule, now);
                if (schedule.IsRecurring)
                {
                    DateTime occurrence;
                    if (!isExpired)
                    {
                        List<DateTime> next = RecurrenceService.EnumerateOccurrences(
                            schedule, today, 400, 1);
                        if (next.Count == 0)
                        {
                            continue;
                        }
                        occurrence = next[0];
                    }
                    else
                    {
                        if (schedule.Recurrence == null
                            || string.IsNullOrEmpty(schedule.Recurrence.Until))
                        {
                            continue;
                        }
                        DateTime cursor;
                        if (!DateUtil.TryParseDateText(schedule.Recurrence.Until, out cursor))
                        {
                            continue;
                        }
                        occurrence = DateTime.MinValue;
                        for (int d = 0; d < 400; d++)
                        {
                            if (RecurrenceService.IsHit(schedule, cursor))
                            {
                                occurrence = cursor;
                                break;
                            }
                            cursor = cursor.AddDays(-1);
                        }
                        if (occurrence == DateTime.MinValue)
                        {
                            continue;
                        }
                    }
                    SearchHit hit = new SearchHit();
                    hit.At = occurrence.Date.Add(schedule.TimeValue);
                    hit.Title = schedule.Title;
                    hit.Repeat = RecurrenceService.Describe(schedule);
                    if (isExpired)
                    {
                        expired.Add(hit);
                    }
                    else
                    {
                        upcoming.Add(hit);
                    }
                }
                else
                {
                    SearchHit hit = new SearchHit();
                    hit.At = schedule.DateValue.Add(schedule.TimeValue);
                    hit.Title = schedule.Title;
                    if (isExpired)
                    {
                        expired.Add(hit);
                    }
                    else
                    {
                        upcoming.Add(hit);
                    }
                }
            }

            upcoming.Sort(delegate(SearchHit a, SearchHit b) { return a.At.CompareTo(b.At); });
            expired.Sort(delegate(SearchHit a, SearchHit b) { return b.At.CompareTo(a.At); });
        }

        private void OnSearchBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (_suppressSearch)
            {
                return;
            }
            SearchHost.Background = ThemeService.SearchFillBrush(true);
        }

        private void OnSearchBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (_suppressSearch)
            {
                return;
            }
            SearchHost.Background = ThemeService.SearchFillBrush(false);
            if (SearchHost.IsKeyboardFocusWithin
                || (SearchPanel.Child != null && SearchPanel.Child.IsKeyboardFocusWithin))
            {
                return;
            }
            CollapseSearch();
        }

        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CollapseSearch();
                e.Handled = true;
            }
        }

        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (SearchBox.Visibility != Visibility.Visible || SearchHost.IsMouseOver)
            {
                return;
            }
            CollapseSearch();
        }

        private void OnSearchResultClick(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            SearchHit hit = element != null ? element.DataContext as SearchHit : null;
            if (hit != null)
            {
                JumpToDate(hit.At.Date);
            }
            CollapseSearch();
        }

        private void JumpToDate(DateTime date)
        {
            _selectedDate = date;
            _displayYear = date.Year;
            _displayMonth = date.Month;
            if (_viewMode == "week")
            {
                _weekStart = StartOfWeek(date);
            }
            RefreshCalendar();
            if (_viewMode == "week")
            {
                RenderWeek();
            }
        }

        private void OnPrevYearClick(object sender, RoutedEventArgs e)
        {
            if (_displayYear <= MinYear)
            {
                return;
            }
            _displayYear--;
            RefreshCalendar();
        }

        private void OnNextYearClick(object sender, RoutedEventArgs e)
        {
            if (_displayYear >= MaxYear)
            {
                return;
            }
            _displayYear++;
            RefreshCalendar();
        }

        private void OnPrevMonthClick(object sender, RoutedEventArgs e)
        {
            if (_displayYear == MinYear && _displayMonth == 1)
            {
                return;
            }
            _displayMonth--;
            if (_displayMonth < 1)
            {
                _displayMonth = 12;
                _displayYear--;
            }
            RefreshCalendar();
        }

        private void OnNextMonthClick(object sender, RoutedEventArgs e)
        {
            if (_displayYear == MaxYear && _displayMonth == 12)
            {
                return;
            }
            _displayMonth++;
            if (_displayMonth > 12)
            {
                _displayMonth = 1;
                _displayYear++;
            }
            RefreshCalendar();
        }

        // ---------------- 标题栏 / 左列按键 ----------------

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 双击标题栏 = 最大化 / 还原（OPT-012）；与右上角按钮共用 ToggleMaximize()，行为必然一致。
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                e.Handled = true;
                return;
            }
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            // 只更新内存（写盘留到关闭 / 退出时做一次）。最小化前的尺寸由 WPF 的 RestoreBounds 兜住，不会丢。
            UpdateBoundsToSettings();
            WindowState = WindowState.Minimized;
        }

        /// <summary>最大化 / 还原的**唯一**切换逻辑：右上角按钮与标题栏双击都走这里（OPT-012 收敛为一条路径）。</summary>
        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            UpdateMaximizeButtonContent();
            if (WindowState == WindowState.Normal && _isBoundsRestored)
            {
                // 只更新内存（写盘留到关闭 / 退出时做一次）。
                UpdateBoundsToSettings();
            }
        }

        /// <summary>
        /// 只切换按钮状态标记；最大化 / 还原两个图标都在 XAML 里，由 DataTrigger 依据 Tag 决定显示哪一个（ADR-009）。
        /// 好处：改颜色、改缩放档、拖拽窗口都只影响 XAML，不会因代码重建图标而出错。
        /// </summary>
        private void UpdateMaximizeButtonContent()
        {
            if (MaximizeButton == null)
            {
                return;
            }
            MaximizeButton.Tag = WindowState == WindowState.Maximized ? "Maximized" : "Normal";
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            // 关闭 = 隐藏到托盘，真正退出由左列「⏻」或托盘菜单触发
            Close();
        }

        /// <summary>统计数据：打开只读统计窗口（单实例由窗口自身保证）。</summary>
        private void OnStatisticsClick(object sender, RoutedEventArgs e)
        {
            StatisticsWindow.Open();
        }

        /// <summary>小工具集合按钮：按下时只处理“关闭”，打开留给 Click，避免开在 MouseDown 后被 Popup 立刻收掉。</summary>
        private void OnToolBoxPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ToolFlyout.IsOpen)
            {
                ToolFlyout.ClosePanel();
                // 已打开时拦截，防止 Click 再把它打开（形成关不掉的抖动）。
                e.Handled = true;
            }
        }

        private void OnToolBoxClick(object sender, RoutedEventArgs e)
        {
            if (!ToolFlyout.IsOpen)
            {
                ToolFlyout.ShowFor(ToolBoxButton);
            }
        }

        private void OnCreateClick(object sender, RoutedEventArgs e)
        {
            if (!ClickGuard.TryPassCreate())
            {
                return;
            }
            // 「＋」优先用当前选中日（OPT-014）；未选中任何日时 CreateWindow 内部回退今天。
            CreateWindow window = new CreateWindow(_selectedDate);
            window.Show();
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            // 模态显示（ShowDialog）：打开期间禁用本程序其它窗口，必须先处理完设置窗。
            // 设置 Owner：让设置窗始终位于主窗之上，点击任务栏主窗时系统会把对话框带回前台。
            SettingsWindow window = new SettingsWindow();
            window.Owner = this;
            // 出现在主窗当前位置（含被拖动后的位置）；窗口尺寸由自身 XAML 决定
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = this.Left + 25;
            window.Top = this.Top + 25;
            window.ShowDialog();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            RequestExit();
        }

        /// <summary>二次确认后真正退出进程。</summary>
        public void RequestExit()
        {
            bool ok = DialogService.Confirm("退出程序",
                "确定要退出日程提醒吗？\r\n退出后将不再提醒，直到再次启动本程序。", "退出", true);
            if (!ok)
            {
                return;
            }
            _isExiting = true;
            SaveBounds();
            Application.Current.Shutdown();
        }

        // ---------------- 缩放 grip ----------------

        private void OnResizeGripMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero)
            {
                return;
            }
            // 交给系统进入右下角拖拽缩放（WindowChrome 处理 WM_NCLBUTTONDOWN）
            SendMessage(helper.Handle, WmNcLButtonDown, (IntPtr)HtBottomRight, IntPtr.Zero);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // ---------------- 显示 / 隐藏 ----------------

        /// <summary>从托盘打开：定位到当天所在月份并激活（P0-1 ⑤）。</summary>
        public void ShowFromTray()
        {
            // 从托盘唤起：重建日历绑定；若期间释放过背景图位图，这里重建背景（重新解码图片）
            CalendarItems.ItemsSource = _cells;
            RestoreBackgroundIfReleased();

            DateTime now = DateTime.Now;
            _todayCache = now.Date;
            _displayYear = now.Year;
            _displayMonth = now.Month;
            RefreshCalendar();
            if (_viewMode == "week")
            {
                _weekStart = StartOfWeek(_todayCache);
                RenderWeek();
            }

            if (!IsVisible)
            {
                Show();
            }
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            SaveBounds();
            if (_isExiting)
            {
                return;
            }
            // 关闭主窗 = 隐藏到托盘，不退出进程
            e.Cancel = true;
            Hide();
            HidePreview();
            ReleaseHiddenResources();
            MemoryHelper.Trim();
        }

        /// <summary>
        /// 内存方案 B：隐藏到托盘后卸载重资源——日历可视化树（42 格及其绑定）+ 背景图位图。
        /// 唤起时由 ShowFromTray 重建，用户侧感知不到。
        /// </summary>
        private void ReleaseHiddenResources()
        {
            try
            {
                CalendarItems.ItemsSource = null;
                _selectedDate = null;
                TryReleaseBackground();
            }
            catch (Exception ex)
            {
                LogWriter.Write("隐藏到托盘时释放资源失败", ex);
            }
        }

        /// <summary>
        /// 缩到托盘时释放背景图位图（省内存），但**只有在其它窗口全部关闭（或隐藏）后**才释放：
        /// 释放动作会把全局背景画笔换成纯色，若还有别的窗口在用（含最小化的），
        /// 它们——尤其是最小化后又被还原时——的背景会变成纯色。
        /// 若此刻还有别的窗口在用 → 订阅它们的关闭事件，等最后一个关掉时再补释放。
        /// </summary>
        private void TryReleaseBackground()
        {
            if (ThemeService.ReleaseBackgroundCacheIfIdle(this))
            {
                _bgReleasedWhileHidden = true;
                return;
            }
            // 还有别的窗口开着：先不释放，等它们都关了再说
            HookOtherWindowsClosed();
        }

        /// <summary>
        /// 订阅当前其它「正在使用中」窗口的关闭事件（等最后一个关闭时补释放背景图）。
        /// 口径与释放判断完全一致：可见的、以及**最小化的**窗口都算在用，都要订阅到，
        /// 否则最小化的窗口被关闭时不会通知我们，就永远等不到"最后一个关闭"这一刻。
        /// </summary>
        private void HookOtherWindowsClosed()
        {
            if (Application.Current == null)
            {
                return;
            }
            foreach (Window window in Application.Current.Windows)
            {
                if (window == null || window == this)
                {
                    continue;
                }
                if (!ThemeService.IsWindowInUse(window))
                {
                    continue;
                }
                if (_bgReleaseHooked.Contains(window))
                {
                    continue;
                }
                window.Closed += OnOtherWindowClosed;
                _bgReleaseHooked.Add(window);
            }
        }

        private void UnhookOtherWindowsClosed()
        {
            for (int i = 0; i < _bgReleaseHooked.Count; i++)
            {
                _bgReleaseHooked[i].Closed -= OnOtherWindowClosed;
            }
            _bgReleaseHooked.Clear();
        }

        /// <summary>
        /// 其它窗口关掉了：退订后重试一次释放——若此刻已经没有别的可见窗口（且主窗仍在托盘），就补释放。
        /// </summary>
        private void OnOtherWindowClosed(object sender, EventArgs e)
        {
            Window closed = sender as Window;
            if (closed != null)
            {
                closed.Closed -= OnOtherWindowClosed;
                _bgReleaseHooked.Remove(closed);
            }
            if (IsVisible)
            {
                // 主窗已经显示出来了，背景正在使用中，不能再释放
                UnhookOtherWindowsClosed();
                return;
            }
            if (ThemeService.ReleaseBackgroundCacheIfIdle(this))
            {
                _bgReleasedWhileHidden = true;
                UnhookOtherWindowsClosed();
            }
        }

        /// <summary>
        /// 从托盘唤起时：若期间释放过背景图位图，重新应用一次主题以重建背景。
        /// 只在"确实释放过"时才重建，避免每次唤起都白白解码一遍图片。
        /// </summary>
        private void RestoreBackgroundIfReleased()
        {
            if (!_bgReleasedWhileHidden)
            {
                return;
            }
            _bgReleasedWhileHidden = false;
            UnhookOtherWindowsClosed();
            ThemeService.ApplySettings();
        }

        private void OnDataChanged(object sender, EventArgs e)
        {
            RefreshCurrentScheduleView();
        }

        /// <summary>
        /// 主动从磁盘重读日程和标签，再刷新当前界面。
        /// 不重读 settings，不切换月/周视图，不改变当前显示月份或周起始。
        /// </summary>
        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (!ClickGuard.TryPassRefresh())
            {
                return;
            }
            string tagError = null;
            string scheduleError = null;
            bool tagsOk = AppServices.Tags == null
                || AppServices.Tags.Reload(out tagError);
            bool schedulesOk = AppServices.Repository == null
                || AppServices.Repository.Reload(out scheduleError);

            if (tagsOk && schedulesOk)
            {
                AiCommandExecutor.ProcessIfEnabled();
            }

            // 与新建/修改后的通知链一致：刷新所有已打开窗口、待办面板和提醒队列，
            // 但不改变当前月/周视图或显示位置。
            AppServices.RaiseDataChanged();

            // JSON 损坏时 Load 已经弹过恢复结果；这里只提示暂时 IO 失败。
            if (!tagsOk && AppServices.Tags != null && !AppServices.Tags.SaveBlocked)
            {
                DialogService.Alert("刷新标签失败", tagError);
            }
            if (!schedulesOk && AppServices.Repository != null
                && !AppServices.Repository.SaveBlocked)
            {
                DialogService.Alert("刷新日程失败", scheduleError);
            }
        }

        private void RefreshCurrentScheduleView()
        {
            RefreshCalendar();
            if (_viewMode == "week")
            {
                RenderWeek();
            }
        }

        // ---------------- 窗口位置记忆 ----------------

        /// <summary>把当前位置与「基准尺寸」更新到设置对象（仅内存，不落盘）。</summary>
        private void UpdateBoundsToSettings()
        {
            AppSettings settings = AppServices.Settings;
            if (settings == null)
            {
                return;
            }
            if (double.IsNaN(Left) || double.IsNaN(Top) || double.IsNaN(Width) || double.IsNaN(Height))
            {
                return;
            }
            if (WindowState == WindowState.Normal)
            {
                settings.MainLeft = Left;
                settings.MainTop = Top;
                // 存「基准（100%）尺寸」= 当前实际尺寸 ÷ 缩放系数，避免下次加载时重复放大。
                double scale = ThemeService.UiScale;
                if (scale <= 0)
                {
                    scale = 1;
                }
                settings.MainWidth = Width / scale;
                settings.MainHeight = Height / scale;
                // 同步缩放登记表；最小尺寸基准同样取自 XAML，按当前缩放还原回 100%
                ThemeService.RegisterScaleBase(this, settings.MainWidth, settings.MainHeight,
                                               MinWidth / scale, MinHeight / scale);
            }
            else
            {
                // 注意：最小化 / 最大化状态下落盘时，
                // RestoreBounds 里存的是「实际尺寸」（= 基准 × 缩放系数），必须 ÷ 系数还原成基准。
                // 否则基准被实际尺寸顶掉，下次启动再乘一次系数 → 每经历一次
                // 「最小化/最大化状态下关闭」窗口就放大一圈，文字相对越来越小。
                double restoreScale = ThemeService.UiScale;
                if (restoreScale <= 0)
                {
                    restoreScale = 1;
                }
                settings.MainWidth = (RestoreBounds.Width > 0 ? RestoreBounds.Width : settings.MainWidth) / restoreScale;
                settings.MainHeight = (RestoreBounds.Height > 0 ? RestoreBounds.Height : settings.MainHeight) / restoreScale;
            }
        }

        /// <summary>正常关闭 / 退出时调用：更新内存值并写盘一次。</summary>
        private void SaveBounds()
        {
            UpdateBoundsToSettings();
            if (AppServices.Settings != null)
            {
                SettingsStore.Save(AppServices.Settings);
            }
        }

        /// <summary>
        /// 恢复上次记住的「基准尺寸」（未记过就保持 XAML 的设计尺寸）。
        /// 注意：不可命名为 RestoreBounds——会与基类 Window.RestoreBounds 属性重名（CS0119）。
        /// </summary>
        private void RestoreWindowSize()
        {
            AppSettings settings = AppServices.Settings;
            if (settings == null)
            {
                return;
            }
            if (settings.MainWidth >= MinWidth)
            {
                Width = settings.MainWidth;
            }
            if (settings.MainHeight >= MinHeight)
            {
                Height = settings.MainHeight;
            }
        }

        /// <summary>恢复上次位置；返回 true = 从未记过位置或位置已跑出工作区，需要居中。</summary>
        private bool RestoreWindowPosition()
        {
            AppSettings settings = AppServices.Settings;
            if (settings == null)
            {
                return true;
            }
            Rect area = SystemParameters.WorkArea;
            double left = settings.MainLeft;
            double top = settings.MainTop;
            if (left == 0 && top == 0)
            {
                return true; // 从未记过位置
            }
            if (left < area.Left || left > area.Right - Width)
            {
                return true; // 记的位置已不在工作区内（如拔掉副屏）
            }
            if (top < area.Top || top > area.Bottom - Height)
            {
                return true;
            }
            Left = left;
            Top = top;
            return false;
        }

        /// <summary>摆到工作区中央。用已定死的 Width/Height——构造函数里 ActualWidth/ActualHeight 还是 0。</summary>
        private void CenterOnWorkArea()
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }
    }
}
