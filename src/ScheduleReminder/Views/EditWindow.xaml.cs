using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScheduleReminder.Models;
using ScheduleReminder.Services;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Views
{
    /// <summary>单日日程编辑窗（P0-3）：每日期一个、非模态，支持单元格内联编辑。</summary>
    public partial class EditWindow : Window
    {
        private readonly ObservableCollection<ScheduleRowVm> _rows = new ObservableCollection<ScheduleRowVm>();
        private readonly DateTime _date;

        // ---------------- 动态高度模型 ----------------
        // 窗口高度 = ChromeHeight + 每条行高 × 当天条数（100% 缩放下的基准值），
        // 下限 = XAML MinHeight（≈2 条日程 + 表头），上限 = 屏幕工作区高 × 0.85。
        // 宽度仍沿用「用户手动缩放后记忆」的值；高度不记忆，每次按条数算。
        //
        // ChromeHeight 对应 EditWindow.xaml：标题栏 34 + 工具条 40 + 窗口 1px 边框×2 + 表头 30 = 106。
        private const double ChromeHeight = 106;
        // 每条日程额外占的间隙：行 Style 的 Margin="0,2"（上下合计 4）。
        private const double RowGapHeight = 4;
        // 高度上限系数：不超过屏幕工作区高度的 85%。
        private const double MaxHeightFactor = 0.85;

        // 最近一次「按当天条数算出」的基准尺寸（100% 缩放），供缩放档位变化时重算。
        private double _baseWidth;
        private double _baseHeight;
        private double _baseMinWidth;
        private double _baseMinHeight;

        public DateTime Date
        {
            get { return _date; }
        }

        public EditWindow(DateTime date)
        {
            _date = date.Date;
            InitializeComponent();
            Icon = IconHelper.GetImageSource();

            ScheduleGrid.ItemsSource = _rows;
            Closed += OnClosed;
            // 最大化 / 还原按钮图标同步（OPT-013）：按钮与双击两条路径都由它统一刷新
            StateChanged += OnStateChanged;
            AppServices.DataChanged += OnDataChanged;
            AppServices.ScheduleFieldChanged += OnScheduleFieldChanged;
            ApplyWindowSize();
            // 只在"用户拖完窗口"时记录尺寸：程序自己改尺寸（缩放等）不会触发
            WindowSizeTracker.Track(this, SaveSize);

            UpdateTitle();
            Refresh();
        }

        /// <summary>
        /// 首次渲染前定尺寸（由构造函数调用）：宽度用 XAML 值 / 用户记忆宽度，高度按「当天条数」动态算。
        /// 设计尺寸仍只维护在 XAML（读它作为 100% 缩放基准）；宽度会被用户拉伸记忆，高度不记忆。
        /// </summary>
        private void ApplyWindowSize()
        {
            _baseMinWidth = MinWidth;
            _baseMinHeight = MinHeight;

            double baseW = Width;
            AppSettings settings = AppServices.Settings;
            // settings 只保存用户拖拽后的宽度；0 表示未拖拽，回落到 XAML 基准。
            if (settings != null && settings.EditWidth > 0
                && settings.EditWidth >= _baseMinWidth)
            {
                baseW = settings.EditWidth;
            }
            _baseWidth = baseW;

            ApplyCurrentSize();
        }

        /// <summary>按「当天条数」重算并应用窗口高度（宽度 / 最小尺寸沿用已登记的基准）。</summary>
        private void ApplyCurrentSize()
        {
            _baseHeight = ComputeBaseHeight(CountTodayOccurrences());
            ThemeService.ApplyScale(this, _baseWidth, _baseHeight, _baseMinWidth, _baseMinHeight);
            ApplyMaxHeight();
        }

        /// <summary>
        /// 刷新窗口高度。只在下列时机调用：打开本窗、打开另一天的窗、窗内新增日程、窗内删除日程。
        /// 最大化 / 最小化时跳过（最大化状态下改 Height 会把窗口弹回还原态）。
        /// </summary>
        private void UpdateWindowHeight()
        {
            if (!IsLoaded || WindowState != WindowState.Normal)
            {
                return;
            }
            ApplyCurrentSize();
        }

        /// <summary>当天条数（与列表行数同源：重复日程按当天发生次数计）。</summary>
        private int CountTodayOccurrences()
        {
            if (AppServices.Schedules == null)
            {
                return 0;
            }
            List<ScheduleOccurrence> occurrences = AppServices.Schedules.GetOccurrences(_date);
            return occurrences != null ? occurrences.Count : 0;
        }

        /// <summary>
        /// 100% 缩放下的高度基准 = ChromeHeight + 每条行高 × 条数；
        /// 下限取 XAML MinHeight（2 条 + 表头），上限取「屏幕工作区高 × 0.85」换算回基准。
        /// </summary>
        private double ComputeBaseHeight(int rowCount)
        {
            double perRow = ScheduleGrid.RowHeight + RowGapHeight;
            double raw = ChromeHeight + perRow * rowCount;
            if (raw < _baseMinHeight)
            {
                raw = _baseMinHeight;
            }

            double scale = ThemeService.UiScale;
            if (scale <= 0)
            {
                scale = 1;
            }
            double capBase = SystemParameters.WorkArea.Height * MaxHeightFactor / scale;
            if (raw > capBase)
            {
                raw = capBase;
            }
            return raw;
        }

        /// <summary>
        /// 把上限同时施加到「自动计算的高度」与「用户手动拉伸」上：MaxHeight = 工作区高 × 0.85。
        /// 内容超出该上限时，改由 DataGrid 的 Auto 纵向滚动条承载（见 XAML ScrollViewer.VerticalScrollBarVisibility）。
        /// </summary>
        private void ApplyMaxHeight()
        {
            double cap = SystemParameters.WorkArea.Height * MaxHeightFactor;
            if (cap < MinHeight)
            {
                cap = MinHeight;   // 极端小屏保护：上限不低于下限
            }
            MaxHeight = cap;
        }

        /// <summary>
        /// 记录用户拉伸后的「基准（100%）尺寸」，只更新内存，写盘留到关闭时做一次。
        /// 只在用户真正拖完窗口（WM_EXITSIZEMOVE）时调用，程序自己改尺寸不会走到这里。
        /// </summary>
        private void SaveSize()
        {
            if (!IsLoaded || WindowState != WindowState.Normal)
            {
                return;
            }
            if (double.IsNaN(Width) || double.IsNaN(Height))
            {
                return;
            }
            AppSettings settings = AppServices.Settings;
            if (settings == null)
            {
                return;
            }
            double scale = ThemeService.UiScale;
            if (scale <= 0)
            {
                scale = 1;
            }
            // 只记忆宽度；高度不记忆，始终按「当天条数」动态计算。
            settings.EditWidth = Width / scale;
            // 同步缩放登记表：高度登记用动态算出的基准，改缩放档位时据此重算。
            ThemeService.RegisterScaleBase(this, settings.EditWidth, _baseHeight,
                                           MinWidth / scale, MinHeight / scale);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            AppServices.DataChanged -= OnDataChanged;
            AppServices.ScheduleFieldChanged -= OnScheduleFieldChanged;
            // 正常关闭 → 写盘一次（拉伸过程中的值已在内存里）。强制终止 / 断电不会走到这里，符合预期。
            if (AppServices.Settings != null)
            {
                SettingsStore.Save(AppServices.Settings);
            }
            ThemeService.UnregisterScale(this);
        }

        private void UpdateTitle()
        {
            string title = DateUtil.FormatDateTitle(_date);
            if (AppServices.Calendar != null)
            {
                string suffix = AppServices.Calendar.GetTitleSuffix(_date);
                if (!string.IsNullOrEmpty(suffix))
                {
                    title += " " + suffix;
                }
            }
            TitleText.Text = title;
        }

        /// <summary>重新加载该日日程（按时间升序，过期行停留原时间位）。</summary>
        public void Refresh()
        {
            // 重建前先退出可能存在的编辑态，避免编辑框引用已失效的行数据
            if (ScheduleGrid != null)
            {
                ScheduleGrid.CancelEdit();
                ScheduleGrid.CancelEdit();
            }
            _rows.Clear();

            if (AppServices.Schedules != null)
            {
                List<ScheduleOccurrence> occurrences = AppServices.Schedules.GetOccurrences(_date);
                for (int i = 0; i < occurrences.Count; i++)
                {
                    ScheduleOccurrence occurrence = occurrences[i];
                    Schedule schedule = occurrence.Schedule;
                    ScheduleRowVm row = new ScheduleRowVm();
                    row.Schedule = schedule;
                    row.OccurrenceDate = _date;
                    row.AlarmTime = occurrence.AlarmTime;
                    row.IsPast = occurrence.IsPast;
                    row.RemindEnabled = schedule.RemindEnabled;
                    row.IsTodo = schedule.IsTodo;
                    row.IsDone = schedule.IsDone(_date);
                    row.TimeText = DateUtil.FormatTime(schedule.TimeValue);
                    row.TitleText = schedule.Title;
                    row.AdvanceText = schedule.AdvanceMinutes == 0
                        ? "-"
                        : schedule.AdvanceMinutes.ToString(CultureInfo.InvariantCulture) + " 分钟";
                    row.NoteText = string.IsNullOrEmpty(schedule.Note) ? "-" : schedule.Note;
                    row.HasTag = !string.IsNullOrEmpty(schedule.Tag);
                    row.TagText = row.HasTag ? schedule.Tag : "-";
                    row.TagBackgroundBrush = row.HasTag
                        ? ThemeService.ScheduleHoverBrush(schedule.BgColor)
                        : Brushes.Transparent;
                    row.HoverBrush = ThemeService.ScheduleHoverBrush(schedule.BgColor);
                    row.CheckBrush = ThemeService.ScheduleCheckBrush(schedule.BgColor);
                    row.RecurrenceText = schedule.IsRecurring ? RecurrenceService.Describe(schedule) : "-";
                    row.BgColor = schedule.BgColor;
                    row.FontColor = schedule.FontColor;
                    _rows.Add(row);
                }
            }

            EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnDataChanged(object sender, EventArgs e)
        {
            Refresh();
        }

        /// <summary>
        /// 字段级变化（如勾选开关）：**就地**把每行的开关同步成数据里的值，不重建整表。
        /// 保持与全量刷新同等的"所有窗口都同步"，但不会清空列表——点勾选框不再整窗闪一下。
        /// （RemindEnabled 属性本身有变更通知，值没变时不会触发界面更新。）
        /// </summary>
        private void OnScheduleFieldChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                ScheduleRowVm row = _rows[i];
                if (row != null && row.Schedule != null)
                {
                    row.RemindEnabled = row.Schedule.RemindEnabled;
                    row.IsTodo = row.Schedule.IsTodo;
                    row.IsDone = row.Schedule.IsDone(row.OccurrenceDate);
                }
            }
        }

        // ---------------- 内联编辑 ----------------

        /// <summary>进入编辑态：整条已过期的日程禁止内联编辑（行级过去、系列仍有未来的行仍可编辑）。</summary>
        private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            ScheduleRowVm row = e.Row.DataContext as ScheduleRowVm;
            if (row == null || row.Schedule == null
                || ScheduleService.IsExpired(row.Schedule, DateTime.Now))
            {
                e.Cancel = true;
            }
        }

        /// <summary>Enter = 保存并退出编辑；备注格（Tag=NoteMultiLine）Ctrl/Shift+Enter = 格内换行不提交；Esc = 退出不保存（由 DataGrid 处理）。</summary>
        private void OnEditElementKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }
            bool withModifier = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift
                                || (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            TextBox box = sender as TextBox;
            if (withModifier)
            {
                // 仅备注格允许格内换行（备注框已关 AcceptsReturn，此处手动插入）；其余单行格忽略
                if (box != null && string.Equals(box.Tag as string, "NoteMultiLine", StringComparison.Ordinal))
                {
                    int caret = box.CaretIndex;
                    box.Text = box.Text.Insert(caret, "\r\n");
                    box.CaretIndex = caret + 2;
                    e.Handled = true;
                }
                return;
            }
            // 无修饰键 Enter = 保存并退出：在这里一次做完（读文本 → 校验落盘 → 主动退出编辑），
            // 不再"先记一个标记、再等编辑结束事件来判断"，避免结论取决于两个机制谁先执行。
            e.Handled = true;
            CommitEditingBox(box);
        }

        /// <summary>回车提交：读编辑框文本 → 校验并落盘；失败时保留编辑态。</summary>
        private void CommitEditingBox(TextBox box)
        {
            if (box == null)
            {
                return;
            }
            ScheduleRowVm row = box.DataContext as ScheduleRowVm;
            DataGridColumn column = ScheduleGrid.CurrentColumn;
            string field = column != null ? column.SortMemberPath : null;
            if (row == null || field == null)
            {
                ScheduleGrid.CancelEdit();
                return;
            }
            if (ScheduleService.IsExpired(row.Schedule, DateTime.Now))
            {
                // 整条已过期兜底：即使进入了编辑也不允许落盘
                ScheduleGrid.CancelEdit();
                return;
            }
            FieldCommitResult result = CommitField(row, field, box.Text);
            if (result == FieldCommitResult.Saved || result == FieldCommitResult.NoChange)
            {
                // 落盘后主动结束编辑态：不再依赖"保存引发的全局刷新"来顺手关掉编辑框。
                ScheduleGrid.CancelEdit();
            }
        }

        private enum FieldCommitResult
        {
            Saved,
            Invalid,
            NoChange,
            SaveFailed
        }

        private bool _suppressTagEditSave;

        private void OnTagEditBoxLoaded(object sender, RoutedEventArgs e)
        {
            ComboBox box = sender as ComboBox;
            ScheduleRowVm row = box != null ? box.DataContext as ScheduleRowVm : null;
            if (row == null || row.Schedule == null)
            {
                return;
            }
            _suppressTagEditSave = true;
            try
            {
                BuildTagEditItems(box, row.Schedule);
                ComboBoxItem match = null;
                for (int i = 0; i < box.Items.Count; i++)
                {
                    ComboBoxItem item = box.Items[i] as ComboBoxItem;
                    TagTemplate t = item != null ? item.Tag as TagTemplate : null;
                    if (t != null && string.Equals(t.Id, row.Schedule.TagId,
                            StringComparison.Ordinal))
                    {
                        match = item;
                        break;
                    }
                }
                box.SelectedItem = match;
                if (match == null)
                {
                    box.SelectedIndex = 0;
                }
            }
            finally
            {
                _suppressTagEditSave = false;
            }
            box.IsDropDownOpen = true;
        }

        private static void BuildTagEditItems(ComboBox box, Schedule schedule)
        {
            box.Items.Clear();
            ComboBoxItem none = new ComboBoxItem();
            none.Content = "-";
            none.Tag = null;
            box.Items.Add(none);

            List<TagTemplate> all = TagTemplateWindow.Templates();
            for (int i = 0; i < all.Count; i++)
            {
                TagTemplate t = all[i];
                if (t == null || string.IsNullOrEmpty(t.Name))
                {
                    continue;
                }
                ComboBoxItem item = new ComboBoxItem();
                item.Content = t.Name;
                item.Tag = t;
                box.Items.Add(item);
            }

            bool hasOrphan = !string.IsNullOrEmpty(schedule.TagId)
                && !string.IsNullOrEmpty(schedule.Tag);
            if (hasOrphan)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    TagTemplate t = all[i];
                    if (t != null && string.Equals(t.Id, schedule.TagId,
                            StringComparison.Ordinal))
                    {
                        hasOrphan = false;
                        break;
                    }
                }
            }
            if (!hasOrphan)
            {
                return;
            }

            TagTemplate orphan = new TagTemplate(
                schedule.Tag, schedule.BgColor, schedule.FontColor, null)
            {
                Id = schedule.TagId
            };
            ComboBoxItem orphanItem = new ComboBoxItem();
            orphanItem.Content = schedule.Tag + "（已删除）";
            orphanItem.Tag = orphan;
            orphanItem.Uid = "Orphan";
            box.Items.Add(orphanItem);
        }

        private void OnTagEditSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressTagEditSave)
            {
                return;
            }
            ComboBox box = sender as ComboBox;
            ScheduleRowVm row = box != null ? box.DataContext as ScheduleRowVm : null;
            Schedule schedule = row != null ? row.Schedule : null;
            if (schedule == null)
            {
                return;
            }

            ComboBoxItem item = box.SelectedItem as ComboBoxItem;
            TagTemplate tag = item != null ? item.Tag as TagTemplate : null;
            bool isOrphan = item != null
                && string.Equals(item.Uid, "Orphan", StringComparison.Ordinal);
            string currentId = schedule.TagId ?? string.Empty;
            string nextId = tag != null ? (tag.Id ?? string.Empty) : string.Empty;
            if (string.Equals(currentId, nextId, StringComparison.Ordinal))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (AppServices.Schedules == null)
                {
                    return;
                }
                Schedule draft = schedule.Clone();
                draft.TagId = tag != null ? tag.Id : null;
                draft.Tag = tag != null ? tag.Name : null;
                if (tag != null && !isOrphan)
                {
                    draft.BgColor = string.IsNullOrEmpty(tag.BgColor) ? null : tag.BgColor;
                    draft.FontColor = string.IsNullOrEmpty(tag.FontColor) ? null : tag.FontColor;
                    if (draft.Sound == null)
                    {
                        draft.Sound = new ScheduleSound();
                    }
                    draft.Sound.Preset = string.IsNullOrEmpty(tag.Sound) ? null : tag.Sound;
                }
                SaveOutcome outcome = AppServices.Schedules.Update(schedule, draft);
                ScheduleGrid.CancelEdit();
                Refresh();
                if (outcome != SaveOutcome.Ok)
                {
                    DialogService.ShowSaveFailure(outcome, "schedules.json");
                }
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// 把一次内联编辑落到 ScheduleService（重复日程在此触发「分段」）。
        /// field 取列的 SortMemberPath：TimeText / TitleText / AdvanceText / NoteText。
        /// </summary>
        private FieldCommitResult CommitField(ScheduleRowVm row, string field, string value)
        {
            Schedule schedule = row.Schedule;
            if (schedule == null || ScheduleService.IsExpired(schedule, DateTime.Now))
            {
                return FieldCommitResult.Invalid;
            }
            Schedule draft = schedule.Clone();

            if (field == "TimeText")
            {
                TimeSpan time;
                if (!ScheduleService.TryParseTimeText(value, out time))
                {
                    FlashRow(row);
                    return FieldCommitResult.Invalid;
                }
                draft.Time = DateUtil.FormatTime(time);
            }
            else if (field == "TitleText")
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    FlashRow(row);
                    return FieldCommitResult.Invalid;
                }
                draft.Title = value.Trim();
            }
            else if (field == "AdvanceText")
            {
                int minutes;
                if (!ScheduleService.TryParseAdvanceText(value, out minutes))
                {
                    FlashRow(row);
                    return FieldCommitResult.Invalid;
                }
                draft.AdvanceMinutes = minutes;
            }
            else if (field == "NoteText")
            {
                draft.Note = value;
            }
            else
            {
                return FieldCommitResult.Invalid;
            }

            bool changed = draft.Time != schedule.Time
                           || !string.Equals(draft.Title, schedule.Title, StringComparison.Ordinal)
                           || draft.AdvanceMinutes != schedule.AdvanceMinutes
                           || !string.Equals(draft.Note, schedule.Note, StringComparison.Ordinal);
            if (!changed)
            {
                return FieldCommitResult.NoChange;
            }

            if (!ScheduleService.CheckBoundary(_date, draft.TimeValue, draft.AdvanceMinutes))
            {
                // 边界秒不满足：保持编辑态，整行闪红
                FlashRow(row);
                return FieldCommitResult.Invalid;
            }

            SaveOutcome outcome = AppServices.Schedules.Update(schedule, draft);
            if (outcome != SaveOutcome.Ok)
            {
                // 写盘失败也保持编辑态，用户可直接重试
                DialogService.ShowSaveFailure(outcome, "schedules.json");
                return FieldCommitResult.SaveFailed;
            }

            Refresh();
            return FieldCommitResult.Saved;
        }

        /// <summary>校验失败 / 边界秒不满足：整行短暂淡红，不弹窗、不落盘。</summary>
        private void FlashRow(ScheduleRowVm row)
        {
            row.HasError = true;
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(600);
            timer.Tick += delegate
            {
                row.HasError = false;
                timer.Stop();
            };
            timer.Start();
        }

        // ---------------- 行按钮 ----------------

        private static ScheduleRowVm GetRow(object sender)
        {
            FrameworkElement element = sender as FrameworkElement;
            if (element == null)
            {
                return null;
            }
            return element.DataContext as ScheduleRowVm;
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            this.WindowState = System.Windows.WindowState.Minimized;
        }

        /// <summary>最大化 / 还原的**唯一**切换逻辑：右上角按钮与标题栏双击都走这里（OPT-012 收敛为一条路径）。</summary>
        private void ToggleMaximize()
        {
            this.WindowState = this.WindowState == System.Windows.WindowState.Maximized
                ? System.Windows.WindowState.Normal
                : System.Windows.WindowState.Maximized;
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        /// <summary>
        /// 最大化 / 还原按钮图标同步（OPT-013，与主界面同一套写法）：
        /// 两个 Path 都在 XAML 里，这里只改按钮的 Tag，由 DataTrigger 决定显示哪一个。
        /// </summary>
        private void OnStateChanged(object sender, EventArgs e)
        {
            if (MaximizeButton != null)
            {
                MaximizeButton.Tag = WindowState == System.Windows.WindowState.Maximized ? "Maximized" : "Normal";
            }
        }

        /// <summary>闹钟按钮：切换整条提醒开关；过期行由 IsEnabled 拦截。</summary>
        private void OnAlarmClick(object sender, RoutedEventArgs e)
        {
            ScheduleRowVm row = GetRow(sender);
            if (row == null || AppServices.Schedules == null)
            {
                return;
            }
            SaveOutcome outcome = AppServices.Schedules.SetRemindEnabled(
                row.Schedule, !row.Schedule.RemindEnabled);
            if (outcome != SaveOutcome.Ok)
            {
                DialogService.ShowSaveFailure(outcome, "schedules.json");
            }
        }

        /// <summary>旗帜按钮：切换待办标记；过期行由 IsEnabled 拦截。</summary>
        private void OnTodoClick(object sender, RoutedEventArgs e)
        {
            ScheduleRowVm row = GetRow(sender);
            if (row == null || AppServices.Schedules == null)
            {
                return;
            }
            SaveOutcome outcome = AppServices.Schedules.SetTodo(
                row.Schedule, !row.Schedule.IsTodo);
            if (outcome != SaveOutcome.Ok)
            {
                DialogService.ShowSaveFailure(outcome, "schedules.json");
            }
        }

        private void OnDoneChecked(object sender, RoutedEventArgs e)
        {
            SetDone(sender, true);
        }

        private void OnDoneUnchecked(object sender, RoutedEventArgs e)
        {
            SetDone(sender, false);
        }

        /// <summary>完成勾选框：只改 doneDates，不动 RemindEnabled；过期行也可补记。</summary>
        private void SetDone(object sender, bool done)
        {
            ScheduleRowVm row = GetRow(sender);
            if (row == null || AppServices.Schedules == null)
            {
                return;
            }
            // 守卫：绑定初始化也会触发 Checked/Unchecked；值未变化不提交
            if (row.Schedule.IsDone(row.OccurrenceDate) == done)
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

        private void OnEditRowClick(object sender, RoutedEventArgs e)
        {
            ScheduleRowVm row = GetRow(sender);
            if (row == null)
            {
                return;
            }
            CreateWindow window = new CreateWindow(row.Schedule, _date);
            window.Show();
        }

        private void OnDeleteRowClick(object sender, RoutedEventArgs e)
        {
            ScheduleRowVm row = GetRow(sender);
            if (row == null || AppServices.Schedules == null)
            {
                return;
            }

            DeletePlan plan = AppServices.Schedules.PlanDelete(row.Schedule);
            string message;
            if (plan.Kind == DeleteKind.PurgeExpired)
            {
                message = "确定删除「" + plan.Title + "」所属系列 " + plan.Targets.Count
                          + " 条过期记录吗？\n\n 此操作不可撤销。";
            }
            else if (plan.Kind == DeleteKind.StopFuture)
            {
                message = "将停止「" + plan.Title + "」以后的重复，过往记录保留。\r\n确定继续吗？";
            }
            else
            {
                message = "确定删除日程「" + plan.Title + "」吗？此操作不可撤销。";
            }

            if (!DialogService.Confirm("删除日程", message, "删除", true))
            {
                return;
            }
            SaveOutcome outcome = AppServices.Schedules.ExecuteDelete(plan);
            Refresh();
            // 删除后条数变化 → 刷新窗口高度
            UpdateWindowHeight();
            if (outcome != SaveOutcome.Ok)
            {
                DialogService.ShowSaveFailure(outcome, "schedules.json");
            }
        }

        // ---------------- 窗口操作 ----------------

        private void OnCreateClick(object sender, RoutedEventArgs e)
        {
            if (!ClickGuard.TryPassCreate())
            {
                return;
            }
            CreateWindow window = new CreateWindow(_date);
            // 新增在「新建日程」窗里完成；它一关闭（保存成功或取消）就重算一次高度。
            // 取消时条数不变 → 算出的高度与当前一致，WPF 设置相同值不会产生任何界面变化。
            window.Closed += delegate { UpdateWindowHeight(); };
            window.Show();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

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
    }
}
