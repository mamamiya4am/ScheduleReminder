using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ScheduleReminder.Services;
using ScheduleReminder.Tools.WorldClock;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 世界时钟窗（小工具）：非模态、单实例、可拖拽移动、可拖拽边框缩放、无最大化按钮。
    /// 城市列表（含顺序）是四个小工具里唯一需要持久化的数据，落在独立 data\tools.json 的 worldClock 属性；
    /// 只存城市名，时区偏移与国家名每次由内置城市库与系统时区规则实时计算。
    /// 刷新对齐整分：到分边界刷新一次，其后每六十秒一次，只改文本、不重建列表。
    /// </summary>
    public partial class WorldClockWindow : Window
    {
        private static WorldClockWindow _instance;

        private readonly ObservableCollection<CityRow> _rows = new ObservableCollection<CityRow>();
        private readonly HashSet<string> _reportedTimeZoneFailures =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly DispatcherTimer _timer = new DispatcherTimer();

        /// <summary>城市行集合：行对象常驻，刷新时只改文本，滚动位置与悬停状态不受影响。</summary>
        public ObservableCollection<CityRow> Rows
        {
            get { return _rows; }
        }

        public WorldClockWindow()
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();
            DataContext = this;

            BuildPicker();
            LoadRows();

            _timer.Tick += OnTimerTick;
            ScheduleNextTick();

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

            try
            {
                WorldClockWindow window = new WorldClockWindow();
                _instance = window;
                window.Closed += OnInstanceClosed;
                window.Show();
            }
            catch (Exception ex)
            {
                // 构造或首次 Show 失败时不要让整个程序直接退出，留下可诊断的堆栈。
                LogWriter.Write("世界时钟打开失败", ex);
                DialogService.Alert("世界时钟", "打开世界时钟失败：" + ex.Message);
            }
        }

        private static void OnInstanceClosed(object sender, EventArgs e)
        {
            _instance = null;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _timer.Stop();
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

        /// <summary>顶部下拉：默认项「＋ 添加城市」，其后 39 城。</summary>
        private void BuildPicker()
        {
            PickerBox.Items.Add("＋ 添加城市");
            IList<CityDefinition> cities = WorldClockCatalog.Cities;
            for (int i = 0; i < cities.Count; i++)
            {
                PickerBox.Items.Add(cities[i].Name);
            }
            PickerBox.SelectedIndex = 0;
        }

        private void LoadRows()
        {
            try
            {
                List<string> names = ToolsDataStore.LoadCities();
                if (names == null)
                {
                    names = new List<string>(WorldClockCatalog.DefaultCityNames);
                }
                for (int i = 0; i < names.Count; i++)
                {
                    CityDefinition city = WorldClockCatalog.FindByName(names[i]);
                    if (city == null)
                    {
                        continue;
                    }
                    _rows.Add(new CityRow { Name = city.Name, Country = city.Country });
                }
            }
            catch (Exception ex)
            {
                LogWriter.Write("世界时钟读取城市失败", ex);
            }

            UpdateRowStates();
            RefreshTimes();
        }

        /// <summary>选中城市即追加到末尾并持久化，下拉复位回默认项；已存在的城市不重复添加。</summary>
        private void OnPickerSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PickerBox.SelectedIndex <= 0)
            {
                return;
            }
            string name = PickerBox.SelectedItem as string;
            PickerBox.SelectedIndex = 0;
            if (string.IsNullOrEmpty(name) || ContainsCity(name))
            {
                return;
            }

            CityDefinition city = WorldClockCatalog.FindByName(name);
            if (city == null)
            {
                return;
            }

            CityRow row = new CityRow { Name = city.Name, Country = city.Country };
            _rows.Add(row);
            UpdateRowStates();
            RefreshTimes();
            ToolsDataStore.SaveCities(CurrentNames());
            // 容器此刻尚未由 ItemContainerGenerator 生成，同步调用 ScrollIntoView 会静默失效，故延后到 Loaded 优先级
            Dispatcher.BeginInvoke(new Action(() => CityList.ScrollIntoView(row)),
                                   System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            CityRow row = (sender as FrameworkElement).DataContext as CityRow;
            if (row == null)
            {
                return;
            }
            _rows.Remove(row);
            _reportedTimeZoneFailures.Remove(row.Name ?? string.Empty);
            UpdateRowStates();
            ToolsDataStore.SaveCities(CurrentNames());
        }

        private void OnMoveUpClick(object sender, RoutedEventArgs e)
        {
            Move((sender as FrameworkElement).DataContext as CityRow, -1);
        }

        private void OnMoveDownClick(object sender, RoutedEventArgs e)
        {
            Move((sender as FrameworkElement).DataContext as CityRow, 1);
        }

        private void Move(CityRow row, int delta)
        {
            if (row == null)
            {
                return;
            }
            int index = _rows.IndexOf(row);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= _rows.Count)
            {
                return;
            }
            _rows.Move(index, target);
            UpdateRowStates();
            ToolsDataStore.SaveCities(CurrentNames());
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _timer.Stop();
            // 先停再刷新：刷新失败只写日志、不弹框（世界时钟是后台常驻的小工具，
            // 每分钟弹一次错误框比不刷新更扰人）；finally 保证下一次刷新仍然排上，
            // 避免「一次刷新失败 → 定时器停住 → 时钟永久停在旧时间且无提示」。
            try
            {
                RefreshTimes();
            }
            catch (Exception ex)
            {
                LogWriter.Write("世界时钟刷新失败", ex);
            }
            finally
            {
                ScheduleNextTick();
            }
        }

        /// <summary>刷新对齐整分：先等到下一个整分，之后每六十秒一次。</summary>
        private void ScheduleNextTick()
        {
            DateTime now = DateTime.Now;
            double wait = 60.0 - (now.Second + now.Millisecond / 1000.0) + 0.03;
            if (wait < 0.05)
            {
                wait = 60.0;
            }
            _timer.Interval = TimeSpan.FromSeconds(wait);
            _timer.Start();
        }

        /// <summary>重新计算每行的时刻、日期与时差；只改文本，不重建列表。</summary>
        private void RefreshTimes()
        {
            DateTime utcNow = DateTime.UtcNow;
            for (int i = 0; i < _rows.Count; i++)
            {
                CityRow row = _rows[i];
                try
                {
                    CityDefinition city = WorldClockCatalog.FindByName(row.Name);
                    if (city == null)
                    {
                        MarkTimeZoneUnavailable(row, null);
                        continue;
                    }

                    TimeZoneInfo zone = WorldClockCatalog.Resolve(city);
                    if (zone == null)
                    {
                        MarkTimeZoneUnavailable(row, null);
                        continue;
                    }

                    row.TimeText = WorldClockCatalog.FormatTime(zone, utcNow);
                    row.DateText = WorldClockCatalog.FormatDate(zone, utcNow);
                    row.DifferenceText = WorldClockCatalog.FormatDifference(zone, utcNow);
                    _reportedTimeZoneFailures.Remove(row.Name ?? string.Empty);
                }
                catch (Exception ex)
                {
                    MarkTimeZoneUnavailable(row, ex);
                }
            }
        }

        /// <summary>
        /// 时区不可用时只降级该行，不回落本地时间。
        /// 同一城市连续失败只记一次日志；恢复成功后再次失败会重新记录。
        /// </summary>
        private void MarkTimeZoneUnavailable(CityRow row, Exception exception)
        {
            row.TimeText = "--:--";
            row.DateText = "时区不可用";
            row.DifferenceText = string.Empty;

            string cityName = string.IsNullOrEmpty(row.Name) ? "(未知城市)" : row.Name;
            if (_reportedTimeZoneFailures.Add(cityName))
            {
                if (exception != null)
                {
                    LogWriter.Write("世界时钟时区不可用：" + cityName, exception);
                }
                else
                {
                    LogWriter.Write("世界时钟时区不可用：" + cityName);
                }
            }
        }

        /// <summary>刷新首行 / 末行的箭头禁用态、末行分割线，以及空状态提示。</summary>
        private void UpdateRowStates()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].CanMoveUp = i > 0;
                _rows[i].CanMoveDown = i < _rows.Count - 1;
                _rows[i].IsLast = i == _rows.Count - 1;
            }
            EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool ContainsCity(string name)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Name == name)
                {
                    return true;
                }
            }
            return false;
        }

        private List<string> CurrentNames()
        {
            List<string> names = new List<string>();
            for (int i = 0; i < _rows.Count; i++)
            {
                names.Add(_rows[i].Name);
            }
            return names;
        }
    }
}
