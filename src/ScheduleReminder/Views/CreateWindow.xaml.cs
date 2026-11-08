using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using ScheduleReminder.Models;
using ScheduleReminder.Services;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 新建 / 详细编辑日程窗（P0-4）。728×960 双列布局（内容区整体可纵向滚动，缩窗口不会遮内容）。
    /// 编辑模式下若原日程已过期，保存按钮禁用；其余控件仍可编辑，修改作为本地草稿，关闭即丢弃。
    /// </summary>
    public partial class CreateWindow : Window
    {
        private readonly Schedule _source;
        private readonly bool _isEditMode;
        private readonly bool _isReadOnly;

        private string _bgColor;
        private string _fontColor;
        private bool _suppressTagApply;

        // 提醒铃：全程序共用的 SoundPicker 组件（列表 / 试听 / 展开刷新都交给它）
        private SoundPicker _soundPicker;

        /// <summary>新建模式。presetDate 为空时默认今天。</summary>
        public CreateWindow(DateTime? presetDate)
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();
            Closed += OnClosed;
            ApplyWindowSize();
            // 只在"用户拖完窗口"时记录尺寸：程序自己改尺寸（缩放等）不会触发
            WindowSizeTracker.Track(this, SaveSize);

            _source = null;
            _isEditMode = false;
            _isReadOnly = false;

            ApplyCalendarRange();
            PopulateTags();
            BuildTimeWheels();
            InitializeDefaults(presetDate.HasValue ? presetDate.Value : DateTime.Today);
        }

        /// <summary>编辑模式（由编辑窗进入）。</summary>
        public CreateWindow(Schedule schedule, DateTime occurrenceDate)
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();
            Closed += OnClosed;
            ApplyWindowSize();
            // 只在"用户拖完窗口"时记录尺寸：程序自己改尺寸（缩放等）不会触发
            WindowSizeTracker.Track(this, SaveSize);

            _source = schedule;
            _isEditMode = true;
            DateTime now = DateTime.Now;
            _isReadOnly = ScheduleService.IsExpired(schedule, now);

            ApplyCalendarRange();
            PopulateTags();
            BuildTimeWheels();

            DateTime presetDate = occurrenceDate;
            if (schedule != null && schedule.IsRecurring)
            {
                if (ScheduleService.HasTakenEffect(schedule, now))
                {
                    presetDate = schedule.DateValue > DateTime.Today
                        ? schedule.DateValue
                        : DateTime.Today;
                }
                else
                {
                    presetDate = schedule.DateValue;
                }
            }
            FillFrom(schedule, presetDate);

            string title = _isReadOnly ? "查看过期日程（只读）" : "编辑日程";
            TitleText.Text = title;
            Title = title;
            if (_isReadOnly)
            {
                ApplyReadOnlyState();
            }
        }

        /// <summary>
        /// 在首次渲染前把窗口最终尺寸定死（由两个构造函数调用）。
        /// 设计尺寸只维护在 XAML（Width/Height/MinWidth/MinHeight）：这里读它作为 100% 缩放基准；
        /// 若用户此前拉伸过本窗口，则用 AppSettings 里记忆的基准尺寸（存时已 ÷ 缩放系数，不会重复放大）。
        /// </summary>
        private void ApplyWindowSize()
        {
            double baseW = Width;
            double baseH = Height;
            double baseMinW = MinWidth;
            double baseMinH = MinHeight;
            AppSettings settings = AppServices.Settings;
            if (settings != null)
            {
                // 用户真正拉伸过才覆盖 XAML；0 = 用 XAML 默认值。
                if (settings.CreateWidth > 0)
                {
                    baseW = settings.CreateWidth;
                }
                if (settings.CreateHeight > 0)
                {
                    baseH = settings.CreateHeight;
                }
            }
            ThemeService.ApplyScale(this, baseW, baseH, baseMinW, baseMinH);
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
            settings.CreateWidth = Width / scale;
            settings.CreateHeight = Height / scale;
            // 同步缩放登记表：改缩放档位时按新基准重算，不会把拉伸结果算丢。
            ThemeService.RegisterScaleBase(this, settings.CreateWidth, settings.CreateHeight,
                                           MinWidth / scale, MinHeight / scale);
        }

        /// <summary>正常关闭本窗口 → 写盘一次（拉伸过程中的值已在内存里）。强制终止 / 断电不会走到这里，符合预期。</summary>
        private void OnClosed(object sender, EventArgs e)
        {
            // 关窗即静音：试听中的声音不该在窗口关闭后继续响
            if (_soundPicker != null)
            {
                _soundPicker.Dispose();
            }
            if (AppServices.Sound != null)
            {
                AppServices.Sound.StopPreview(); // 只停试听，不打扰正在响的提醒
            }
            if (AppServices.Settings != null)
            {
                SettingsStore.Save(AppServices.Settings);
            }
            ThemeService.UnregisterScale(this);
        }

        /// <summary>
        /// 两个日期选择控件统一限制在「支持年份范围」内（与月历可翻阅范围、节气表覆盖一致）。
        /// 这样用户翻不出去；万一手工输入了超范围日期，保存时还有第二道校验拦下。
        /// </summary>
        private void ApplyCalendarRange()
        {
            DateCalendar.DisplayDateStart = DateUtil.MinSupportedDate;
            DateCalendar.DisplayDateEnd = DateUtil.MaxSupportedDate;
            UntilCalendar.DisplayDateStart = DateUtil.MinSupportedDate;
            UntilCalendar.DisplayDateEnd = DateUtil.MaxSupportedDate;
        }

        // ---------------- 初始化 ----------------

        /// <summary>提前提醒框的「上一次合法值」：非法输入回退时用它（而不是最小值 0）。</summary>
        private int _lastValidAdvance;

        /// <summary>
        /// 数字输入过滤：只允许「数字 + 最多一个小数点」
        /// （提前提醒最终取整数，允许小数是为了输入 40.6 这类值，由 TryParseAdvanceText 四舍五入）。
        /// </summary>
        private void OnAdvancePreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsNumericInput(AdvanceBox, e.Text);
        }

        /// <summary>拦截粘贴，防止非数字内容绕过输入过滤。</summary>
        private void OnAdvancePasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject == null)
            {
                e.CancelCommand();
                return;
            }
            object data = null;
            try
            {
                data = e.DataObject.GetData(DataFormats.Text);
            }
            catch (Exception)
            {
                data = null;
            }
            if (!IsNumericInput(AdvanceBox, data as string))
            {
                e.CancelCommand();
            }
        }

        private static bool IsNumericInput(System.Windows.Controls.TextBox box, string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }
            string next = box != null ? box.Text ?? string.Empty : string.Empty;
            foreach (char c in input)
            {
                if (c >= '0' && c <= '9')
                {
                    next += c;
                    continue;
                }
                if (c == '.')
                {
                    if (next.IndexOf('.') >= 0)
                    {
                        return false;
                    }
                    next += c;
                    continue;
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// 提前提醒框失焦：先解析，合法才更新；**非法则回退到上一次合法值**（不是 0，也不是 60）。
        /// </summary>
        private void OnAdvanceLostFocus(object sender, RoutedEventArgs e)
        {
            int minutes;
            if (ScheduleService.TryParseAdvanceText(AdvanceBox.Text, out minutes))
            {
                _lastValidAdvance = minutes;
            }
            AdvanceBox.Text = _lastValidAdvance.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>起始日期失焦：解析成功就按标准格式回写，失败保持原样（错误留给保存时统一提示）。</summary>
        private void OnDateLostFocus(object sender, RoutedEventArgs e)
        {
            DateTime date;
            if (DateUtil.TryParseDateText(DateBox.Text, out date))
            {
                DateBox.Text = DateUtil.FormatDate(date.Date);
            }
        }

        /// <summary>正点时间失焦：解析成功就按标准格式回写，失败保持原样。</summary>
        private void OnTimeLostFocus(object sender, RoutedEventArgs e)
        {
            TimeSpan time;
            if (ScheduleService.TryParseTimeText(TimeBox.Text, out time))
            {
                TimeBox.Text = DateUtil.FormatTime(time);
            }
        }

        /// <summary>截止日期失焦：空值放行（永久生效）；非空解析成功就回写标准格式，失败保持原样。</summary>
        private void OnUntilLostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UntilBox.Text))
            {
                return;
            }
            DateTime date;
            if (DateUtil.TryParseDateText(UntilBox.Text, out date))
            {
                UntilBox.Text = DateUtil.FormatDate(date.Date);
            }
        }

        private void InitializeDefaults(DateTime date)
        {
            DateBox.Text = DateUtil.FormatDate(date);
            DateTime next = DateTime.Now.AddHours(1);
            TimeBox.Text = DateUtil.FormatTime(new TimeSpan(next.Hour, 0, 0));
            AdvanceBox.Text = "0";
            _lastValidAdvance = 0;
            TitleBox.Text = string.Empty;
            NoteBox.Text = string.Empty;
            RecurCheck.IsChecked = false;
            RemindCheck.IsChecked = true; // 新建默认「开启提醒」（与「重复提醒」默认关闭不同）
            TodoCheck.IsChecked = false;  // 新建默认不是待办
            LastNCombo.SelectedIndex = 0;
            LastWdCombo.SelectedIndex = 0;
            // 默认选中「系统设置当前音效」；若未设置则落在「无」
            _soundPicker = SoundPicker.Load(SoundCombo, AppServices.Settings != null ? AppServices.Settings.SoundFilePath : SoundLibrary.NoneOption);
            SetSwatch(BgRect, null);
            SetFontSwatch();
            UpdateCounters();
        }

        private void FillFrom(Schedule schedule, DateTime occurrenceDate)
        {
            TitleBox.Text = schedule.Title;
            DateBox.Text = DateUtil.FormatDate(occurrenceDate);
            TimeBox.Text = DateUtil.FormatTime(schedule.TimeValue);
            AdvanceBox.Text = schedule.AdvanceMinutes.ToString(CultureInfo.InvariantCulture);
            _lastValidAdvance = schedule.AdvanceMinutes;
            RemindCheck.IsChecked = schedule.RemindEnabled;
            TodoCheck.IsChecked = schedule.IsTodo;
            NoteBox.Text = schedule.Note;
            // 标签按 TagId 回填；模板被删时临时补一项，只用于显示/回写，不加入模板列表
            SelectTagForSchedule(schedule);

            _bgColor = schedule.BgColor;
            _fontColor = schedule.FontColor;
            SetSwatch(BgRect, _bgColor);
            SetFontSwatch();

            // 回填：日程自己的快照；null/空 = 静音（显示为「-」），不再回落设置默认
            string wantSound = schedule.Sound != null && !string.IsNullOrEmpty(schedule.Sound.Preset)
                ? schedule.Sound.Preset
                : SoundLibrary.NoneOption;
            _soundPicker = SoundPicker.Load(SoundCombo, wantSound);

            RecurrenceRule rule = schedule.Recurrence;
            if (rule != null && rule.Enabled)
            {
                RecurCheck.IsChecked = true;

                if (rule.Weekly != null && rule.Weekly.Weekdays != null && rule.Weekly.Weekdays.Count > 0)
                {
                    // 无「每周重复」勾选框（规格中为小标题）：直接回显七个星期勾选
                    WdMon.IsChecked = rule.Weekly.Weekdays.Contains(1);
                    WdTue.IsChecked = rule.Weekly.Weekdays.Contains(2);
                    WdWed.IsChecked = rule.Weekly.Weekdays.Contains(3);
                    WdThu.IsChecked = rule.Weekly.Weekdays.Contains(4);
                    WdFri.IsChecked = rule.Weekly.Weekdays.Contains(5);
                    WdSat.IsChecked = rule.Weekly.Weekdays.Contains(6);
                    WdSun.IsChecked = rule.Weekly.Weekdays.Contains(0);
                    DailyCheck.IsChecked = rule.Weekly.Weekdays.Count == 7;
                }

                if (rule.Yearly != null && rule.Yearly.Month > 0)
                {
                    YearlyCheck.IsChecked = true;
                }
                if (rule.Monthly != null && rule.Monthly.Day > 0)
                {
                    MonthlyCheck.IsChecked = true;
                }
                // 无勾选框：直接用下拉框回显；无该规则时回到第 0 项（"-"）
                if (rule.MonthlyLastN != null && rule.MonthlyLastN.N >= 1)
                {
                    SelectComboByTag(LastNCombo, rule.MonthlyLastN.N);
                }
                else
                {
                    LastNCombo.SelectedIndex = 0;
                }
                if (rule.MonthlyLastWeekday != null)
                {
                    SelectComboByTag(LastWdCombo, rule.MonthlyLastWeekday.Weekday);
                }
                else
                {
                    LastWdCombo.SelectedIndex = 0;
                }
                UntilBox.Text = rule.Until != null ? rule.Until : string.Empty;
            }
            else
            {
                LastNCombo.SelectedIndex = 0;
                LastWdCombo.SelectedIndex = 0;
            }

            UpdateCounters();
        }

        /// <summary>
        /// 标签候选：第一项为「-」（无标签，Tag=null），其余下拉项直接携带 TagTemplate 对象。
        /// 只允许选择，不允许手输；同名标签靠 TagId 区分。
        /// </summary>
        private void PopulateTags()
        {
            _suppressTagApply = true;
            try
            {
                TagBox.Items.Clear();
                ComboBoxItem none = new ComboBoxItem();
                none.Content = "-";
                none.Tag = null;
                TagBox.Items.Add(none);

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
                    TagBox.Items.Add(item);
                }
                TagBox.SelectedIndex = 0;
            }
            finally
            {
                _suppressTagApply = false;
            }
        }

        /// <summary>回填：按 TagId 选中；模板已删时临时补一项，仅用于显示和回写。</summary>
        private void SelectTagForSchedule(Schedule schedule)
        {
            _suppressTagApply = true;
            try
            {
                ComboBoxItem match = null;
                for (int i = 0; i < TagBox.Items.Count; i++)
                {
                    ComboBoxItem item = TagBox.Items[i] as ComboBoxItem;
                    TagTemplate t = item != null ? item.Tag as TagTemplate : null;
                    if (t != null && !string.IsNullOrEmpty(t.Id)
                        && string.Equals(t.Id, schedule.TagId, StringComparison.Ordinal))
                    {
                        match = item;
                        break;
                    }
                }

                if (match == null && !string.IsNullOrEmpty(schedule.Tag))
                {
                    TagTemplate orphan = new TagTemplate(
                        schedule.Tag,
                        schedule.BgColor,
                        schedule.FontColor,
                        null)
                    {
                        Id = schedule.TagId
                    };
                    ComboBoxItem item = new ComboBoxItem();
                    item.Content = schedule.Tag;
                    item.Tag = orphan;
                    TagBox.Items.Add(item);
                    match = item;
                }

                if (match != null)
                {
                    TagBox.SelectedItem = match;
                }
                else
                {
                    TagBox.SelectedIndex = 0;
                }
            }
            finally
            {
                _suppressTagApply = false;
            }
        }

        private void OnTagSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressTagApply)
            {
                return;
            }
            ComboBoxItem item = TagBox.SelectedItem as ComboBoxItem;
            TagTemplate t = item != null ? item.Tag as TagTemplate : null;
            if (t != null)
            {
                ApplyTagPreset(t);
            }
        }

        /// <summary>
        /// 把模板样式套用到窗口控件：背景色板、字体色板、铃声下拉。
        /// 空值按约定处理：BgColor 空 = 透明；FontColor 空 = 跟随系统文字色；Sound 空 = 静音。
        /// </summary>
        private void ApplyTagPreset(TagTemplate t)
        {
            _bgColor = string.IsNullOrEmpty(t.BgColor) ? null : t.BgColor;
            SetSwatch(BgRect, _bgColor);

            _fontColor = string.IsNullOrEmpty(t.FontColor) ? null : t.FontColor;
            SetFontSwatch();

            string sound = string.IsNullOrEmpty(t.Sound) ? SoundLibrary.NoneOption : t.Sound;
            if (_soundPicker != null)
            {
                _soundPicker.Select(sound);
            }
        }

        private void ApplyReadOnlyState()
        {
            // 过期日程只关闭保存路径；其余控件保持可交互，修改仅作为本地草稿，关闭即丢弃。
            SaveBtn.IsEnabled = false;
        }

        private DateTime GetSeriesMinStart(DateTime fallback)
        {
            if (_source == null || string.IsNullOrEmpty(_source.SeriesId)
                || AppServices.Schedules == null)
            {
                return fallback.Date;
            }
            return AppServices.Schedules.GetSeriesMinStart(_source.SeriesId, fallback);
        }

        // ---------------- 保存 ----------------

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly)
            {
                return;
            }
            ErrorText.Text = string.Empty;

            string title = TitleBox.Text != null ? TitleBox.Text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(title))
            {
                Flash(TitleBox);
                ErrorText.Text = "标题不能为空";
                return;
            }

            DateTime date;
            if (!DateUtil.TryParseDateText(DateBox.Text, out date))
            {
                Flash(DateBox);
                ErrorText.Text = "日期格式应为：YYYY-MM-DD";
                return;
            }
            date = date.Date;

            TimeSpan time;
            if (!ScheduleService.TryParseTimeText(TimeBox.Text, out time))
            {
                Flash(TimeBox);
                ErrorText.Text = "时间格式应为 HH:MM（24 小时制）";
                return;
            }

            // 「开启提醒」未勾选时：下方提醒设置区被禁用，提前量不参与「时间已过」校验
            // （不提醒就不存在提前提醒超时）；输入框仍照常解析，值原样保留，便于重新勾选后继续使用。
            bool remindOn = RemindCheck.IsChecked == true;

            int advance;
            if (!ScheduleService.TryParseAdvanceText(AdvanceBox.Text, out advance))
            {
                Flash(AdvanceBox);
                ErrorText.Text = "提前提醒时间不得多于60分钟";
                return;
            }

            if (date < DateTime.Today)
            {
                Flash(DateBox);
                ErrorText.Text = "日期不得早于今天";
                return;
            }

            // 超范围与「日期填成过去」同等处理：闪一下输入框 + 给出文字原因 + 不予保存
            if (date < DateUtil.MinSupportedDate || date > DateUtil.MaxSupportedDate)
            {
                Flash(DateBox);
                ErrorText.Text = "日期超出支持范围（"
                    + DateUtil.MinSupportedYear + "-01-01 ~ " + DateUtil.MaxSupportedYear + "-12-31）";
                return;
            }

            if (!ScheduleService.CheckBoundary(date, time, remindOn ? advance : 0))
            {
                Flash(TimeBox);
                Flash(AdvanceBox);
                ErrorText.Text = "当前时间已超过日程提醒或提前提醒";
                return;
            }

            string untilText = UntilBox.Text != null ? UntilBox.Text.Trim() : string.Empty;
            if (untilText.Length > 0)
            {
                DateTime untilDate;
                if (!DateUtil.TryParseDateText(untilText, out untilDate))
                {
                    Flash(UntilBox);
                    ErrorText.Text = "截止日期格式应为：YYYY-MM-DD";
                    return;
                }
                // 归一化为标准格式落盘，重复规则里永远存 yyyy-MM-dd（后续读取 / 比对不再依赖宽松解析）
                untilText = DateUtil.FormatDate(untilDate.Date);
                DateTime minStart = GetSeriesMinStart(date);
                if (untilDate.Date < minStart)
                {
                    Flash(UntilBox);
                    ErrorText.Text = "截止日期不能早于起始日期";
                    return;
                }
                if (untilDate > DateUtil.MaxSupportedDate)
                {
                    Flash(UntilBox);
                    ErrorText.Text = "截止日期超出支持范围（最晚 "
                        + DateUtil.MaxSupportedYear + "-12-31）";
                    return;
                }
            }

            RecurrenceRule rule = BuildRecurrence(untilText);
            if (rule.Enabled && !rule.HasAnyRule())
            {
                ErrorText.Text = "尚未选择重复规则";
                return;
            }

            Schedule draft = new Schedule();
            draft.Title = title;
            draft.Date = DateUtil.FormatDate(date);
            draft.Time = DateUtil.FormatTime(time);
            draft.AdvanceMinutes = advance;
            draft.Note = NoteBox.Text;
            ComboBoxItem selectedTagItem = TagBox.SelectedItem as ComboBoxItem;
            TagTemplate selectedTag = selectedTagItem != null
                ? selectedTagItem.Tag as TagTemplate
                : null;
            draft.TagId = selectedTag != null ? selectedTag.Id : null;
            draft.Tag = selectedTag != null ? selectedTag.Name : null;
            draft.BgColor = _bgColor;
            draft.FontColor = _fontColor;
            draft.RemindEnabled = remindOn;
            draft.Sound = new ScheduleSound();
            draft.Sound.Preset = SelectedSoundName();
            draft.Recurrence = rule;
            draft.IsTodo = TodoCheck.IsChecked == true;

            // 编辑模式：完成记录不由本窗修改，原样保留
            if (_isEditMode && _source != null)
            {
                draft.DoneDates = _source.DoneDates != null
                    ? new List<string>(_source.DoneDates)
                    : new List<string>();
            }

            // 与 AI 执行器共用同一套字段校验，避免两条保存路径分叉。
            string sharedError = ScheduleValidator.Validate(
                draft, GetSeriesMinStart(date), true);
            if (sharedError != null)
            {
                ErrorText.Text = sharedError;
                return;
            }

            if (_isEditMode && !_isReadOnly && _source != null
                && ScheduleService.IsUnchanged(_source, draft))
            {
                Close();
                return;
            }

            SaveOutcome outcome = !_isEditMode
                ? AppServices.Schedules.Create(draft)
                : AppServices.Schedules.Update(_source, draft);
            if (outcome != SaveOutcome.Ok)
            {
                DialogService.ShowSaveFailure(outcome, "schedules.json");
                return;
            }
            Close();
        }

        private RecurrenceRule BuildRecurrence(string untilText)
        {
            RecurrenceRule rule = new RecurrenceRule();
            rule.Enabled = RecurCheck.IsChecked == true;
            if (!rule.Enabled)
            {
                // 未勾选重复提醒：其余子字段一律保存为默认值 / 空值
                rule.Until = null;
                rule.Weekly = new WeeklyRule();
                rule.Weekly.Weekdays = new List<int>();
                return rule;
            }

            rule.Until = string.IsNullOrEmpty(untilText) ? null : untilText;

            // 「每周重复」在素材图规格中只是小标题，无独立勾选框：
            // 只要勾选了任意星期（或经「每天」快捷全选）即视为启用。
            rule.Weekly = new WeeklyRule();
            rule.Weekly.Weekdays = new List<int>();
            if (WdMon.IsChecked == true)
            {
                rule.Weekly.Weekdays.Add(1);
            }
            if (WdTue.IsChecked == true)
            {
                rule.Weekly.Weekdays.Add(2);
            }
            if (WdWed.IsChecked == true)
            {
                rule.Weekly.Weekdays.Add(3);
            }
            if (WdThu.IsChecked == true)
            {
                rule.Weekly.Weekdays.Add(4);
            }
            if (WdFri.IsChecked == true)
            {
                rule.Weekly.Weekdays.Add(5);
            }
            if (WdSat.IsChecked == true)
            {
                rule.Weekly.Weekdays.Add(6);
            }
            if (WdSun.IsChecked == true)
            {
                rule.Weekly.Weekdays.Add(0);
            }

            DateTime baseDate = DateUtil.ParseDate(DateBox.Text);
            if (YearlyCheck.IsChecked == true)
            {
                rule.Yearly = new YearlyRule();
                rule.Yearly.Month = baseDate.Month;
                rule.Yearly.Day = baseDate.Day;
            }
            if (MonthlyCheck.IsChecked == true)
            {
                rule.Monthly = new MonthlyRule();
                rule.Monthly.Day = baseDate.Day;
            }
            // 「每月倒数第 N 天」/「每月最后周 X」无勾选框：下拉选 "-" 即表示不启用
            int lastN = GetComboTag(LastNCombo, -1);
            if (lastN >= 1 && lastN <= 7)
            {
                rule.MonthlyLastN = new MonthlyLastNRule();
                rule.MonthlyLastN.N = lastN;
            }
            int lastWd = GetComboTag(LastWdCombo, -1);
            if (lastWd >= 0 && lastWd <= 6)
            {
                rule.MonthlyLastWeekday = new MonthlyLastWeekdayRule();
                rule.MonthlyLastWeekday.Weekday = lastWd;
            }
            return rule;
        }

        /// <summary>取当前选中的音效名；「无」选项落盘为 null（静音），不留 "-" 哨兵。</summary>
        private string SelectedSoundName()
        {
            string name = _soundPicker != null ? _soundPicker.SelectedName() : null;
            return string.Equals(name, SoundLibrary.NoneOption, StringComparison.Ordinal) ? null : name;
        }

        private static void SelectComboByTag(ComboBox combo, int tag)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                ComboBoxItem item = combo.Items[i] as ComboBoxItem;
                if (item != null && item.Tag != null && item.Tag.ToString() == tag.ToString(CultureInfo.InvariantCulture))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            combo.SelectedIndex = 0;
        }

        private static int GetComboTag(ComboBox combo, int fallback)
        {
            ComboBoxItem item = combo.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag != null)
            {
                int value;
                if (int.TryParse(item.Tag.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    return value;
                }
            }
            return fallback;
        }

        // ---------------- 颜色 ----------------

        private void OnBgColorClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }
            string picked = PickColor(_bgColor);
            if (picked != _bgColor)
            {
                _bgColor = picked;
                SetSwatch(BgRect, _bgColor);
            }
        }

        private void OnFontColorClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }
            string picked = PickColor(_fontColor);
            if (picked != _fontColor)
            {
                _fontColor = picked;
                SetFontSwatch();
            }
        }

        /// <summary>
        /// 取色统一入口：ColorPickerPopup.Pick 居中于父窗。
        /// 返回值只可能是 "#RRGGBB"（选定）或 null（取消 → 保持原值）；
        /// 取色弹窗已不再提供「透明」选项（BUG-002），空值由本窗的「透明」/「跟随」按钮承载。
        /// </summary>
        private static string PickColor(string current)
        {
            string picked = ColorPickerPopup.Pick(current);
            return picked ?? current;
        }

        /// <summary>
        /// 「透明」：日程背景设为透明（清空 BgColor）→ 色块显示棋盘格空态。
        /// 取色弹窗不提供透明选项（BUG-002：空值会击穿通用渲染路径），透明语义统一由此按钮承载。
        /// </summary>
        private void OnBgTransparentClick(object sender, RoutedEventArgs e)
        {
            _bgColor = null;
            SetSwatch(BgRect, null);
        }

        /// <summary>
        /// 「跟随」：字体颜色恢复为「未单独设置」→ 跟随系统文字颜色（色块显示系统文字色）。
        /// 与日程背景的「透明」同源：取色弹窗不再产出空值，复位语义由此按钮承载。
        /// </summary>
        private void OnFontFollowSystemClick(object sender, RoutedEventArgs e)
        {
            _fontColor = null;
            SetFontSwatch();
        }

        private void SetSwatch(System.Windows.Shapes.Rectangle rect, string color)
        {
            if (string.IsNullOrEmpty(color))
            {
                rect.Fill = (Brush)FindResource("CheckerBrush");
            }
            else
            {
                rect.Fill = new SolidColorBrush(ThemeService.ParseColor(color));
            }
        }

        /// <summary>
        /// 文字颜色色块显示「实际值」：已单独设置 → 显示该颜色；未设置（跟随系统）→ 直接引用 TextPrimaryBrush。
        /// 直接引用而非新建画笔，可随“系统文字色”设置实时同步（ThemeService 就地更新该画笔的 Color）。
        /// 这与 ReminderWindow / ScheduleFontBrushConverter 对“空字体色=跟随系统”的处理保持一致。
        /// </summary>
        private void SetFontSwatch()
        {
            if (!string.IsNullOrEmpty(_fontColor))
            {
                FontRect.Fill = new SolidColorBrush(ThemeService.ParseColor(_fontColor));
            }
            else
            {
                FontRect.SetResourceReference(System.Windows.Shapes.Rectangle.FillProperty, "TextPrimaryBrush");
            }
        }

        // ---------------- 重复规则联动 ----------------

        private void OnDailyClick(object sender, RoutedEventArgs e)
        {
            bool all = DailyCheck.IsChecked == true;
            WdMon.IsChecked = all;
            WdTue.IsChecked = all;
            WdWed.IsChecked = all;
            WdThu.IsChecked = all;
            WdFri.IsChecked = all;
            WdSat.IsChecked = all;
            WdSun.IsChecked = all;
        }

        private void OnWeekdayClick(object sender, RoutedEventArgs e)
        {
            bool all = WdMon.IsChecked == true && WdTue.IsChecked == true && WdWed.IsChecked == true
                       && WdThu.IsChecked == true && WdFri.IsChecked == true && WdSat.IsChecked == true
                       && WdSun.IsChecked == true;
            DailyCheck.IsChecked = all;
        }

        private void OnRecurCheckChanged(object sender, RoutedEventArgs e)
        {
            // RulePanel 的 IsEnabled 已绑定到 RecurCheck.IsChecked，无需额外处理
        }

        private void OnRemindCheckChanged(object sender, RoutedEventArgs e)
        {
            // RemindPanel 的 IsEnabled 已绑定到 RemindCheck.IsChecked，无需额外处理
        }

        // ---------------- 日期选择 ----------------

        private void OnPickDateClick(object sender, RoutedEventArgs e)
        {
            // 弹层打开前先定位日历：此时 DatePopup.IsOpen=false，选择事件会自行忽略程序赋值
            DateTime current = DateUtil.ParseDate(DateBox.Text);
            DateCalendar.SelectedDate = current;
            DateCalendar.DisplayDate = current;
            DatePopup.IsOpen = true;
        }

        private void OnDateCalendarSelected(object sender, SelectionChangedEventArgs e)
        {
            // 只有弹层开着（用户正在选日期）才处理；程序自己赋值时弹层未打开，天然被排除
            if (!DatePopup.IsOpen)
            {
                return;
            }
            if (DateCalendar.SelectedDate.HasValue)
            {
                DateBox.Text = DateUtil.FormatDate(DateCalendar.SelectedDate.Value);
                DatePopup.IsOpen = false;
            }
        }

        private void OnPickUntilClick(object sender, RoutedEventArgs e)
        {
            DateTime current = string.IsNullOrWhiteSpace(UntilBox.Text)
                ? DateUtil.ParseDate(DateBox.Text)
                : DateUtil.ParseDate(UntilBox.Text);
            // 弹层打开前定位（此刻 UntilPopup 未打开，事件会忽略这次程序赋值）
            UntilCalendar.SelectedDate = current;
            UntilCalendar.DisplayDate = current;
            UntilPopup.IsOpen = true;
        }

        private void OnUntilCalendarSelected(object sender, SelectionChangedEventArgs e)
        {
            // 同上：只在弹层打开（用户操作）时响应
            if (!UntilPopup.IsOpen)
            {
                return;
            }
            if (UntilCalendar.SelectedDate.HasValue)
            {
                UntilBox.Text = DateUtil.FormatDate(UntilCalendar.SelectedDate.Value);
                UntilPopup.IsOpen = false;
            }
        }

        private void OnClearUntilClick(object sender, RoutedEventArgs e)
        {
            UntilBox.Text = string.Empty;
        }

        // ---------------- 时间滚轮（OPT-016①） ----------------

        /// <summary>填充「时 / 分」两列滚轮：00–23 / 00–59。</summary>
        private void BuildTimeWheels()
        {
            for (int i = 0; i < 24; i++)
            {
                HourList.Items.Add(i.ToString("00", CultureInfo.InvariantCulture));
            }
            for (int i = 0; i < 60; i++)
            {
                MinuteList.Items.Add(i.ToString("00", CultureInfo.InvariantCulture));
            }
        }

        /// <summary>打开浮层前的时间文本：按 Esc 放弃改动时用它还原。</summary>
        private string _timeBeforePopup;

        /// <summary>点时钟图标 → 滚轮定位到当前时间并弹出浮层（浮层内无按钮、无提示文字）。</summary>
        private void OnPickTimeClick(object sender, RoutedEventArgs e)
        {
            _timeBeforePopup = TimeBox.Text;
            TimeSpan current;
            if (!ScheduleService.TryParseTimeText(TimeBox.Text, out current))
            {
                current = new TimeSpan(DateTime.Now.Hour, 0, 0);
            }
            HourList.SelectedIndex = current.Hours;
            MinuteList.SelectedIndex = current.Minutes;
            // 此刻浮层仍未打开：上面两次「程序化选中」会被 OnTimePartSelected 的 IsOpen 守卫忽略。
            // 居中不在这里做——浮层未布局时量不到尺寸，改由 OnTimePopupOpened 在打开后定位。
            TimePopup.IsOpen = true;
        }

        /// <summary>
        /// 浮层打开后定位：先给两列内容上下留白（让首项 00 与末项 23 / 59 也能停在中间高亮带里），
        /// 再把「时 / 分」的选中项滚到正中间，使灰框恰好落在输入框当前时间上。
        /// </summary>
        private void OnTimePopupOpened(object sender, EventArgs e)
        {
            ApplyWheelPadding(HourList);
            ApplyWheelPadding(MinuteList);
            HourList.UpdateLayout();
            MinuteList.UpdateLayout();
            // 留白会改动一次布局，第一遍居中可能落在旧尺寸上，故再做一遍（幂等）
            for (int pass = 0; pass < 2; pass++)
            {
                CenterWheel(HourList);
                CenterWheel(MinuteList);
                HourList.UpdateLayout();
                MinuteList.UpdateLayout();
            }
        }

        /// <summary>
        /// 给滚轮内容上下各留「(可视行数−1)/2」行空白。
        /// 留白与行高都来自**实测**（不写死像素）：改行高或改整体缩放档位都不会错位。
        /// </summary>
        private static void ApplyWheelPadding(ListBox list)
        {
            if (list == null)
            {
                return;
            }
            ItemsPresenter presenter = FindChild<ItemsPresenter>(list);
            ScrollViewer viewer = FindChildScrollViewer(list);
            ListBoxItem container = list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
            if (presenter == null || viewer == null || container == null || container.ActualHeight <= 0)
            {
                return; // 量不到就不留白：宁可首尾两项待在边上，也不写死数字
            }
            double rowHeight = container.ActualHeight;
            // 可视行数向上取整：视口可能比整行略小（实测 148 / 30 → 5 行），
            // 若用 floor 会算成 4 行、只留 1 行空白，首尾两项就居中不了。
            double rows = Math.Ceiling(viewer.ViewportHeight / rowHeight);
            double pad = Math.Floor((rows - 1) / 2) * rowHeight;          // 上下各留 2 行 = 60
            if (pad <= 0)
            {
                return;
            }
            Thickness margin = new Thickness(0, pad, 0, pad);
            if (presenter.Margin != margin)
            {
                presenter.Margin = margin;
            }
        }

        /// <summary>
        /// 滚轮：一次滚动事件只移动**一格**（并让该格停在中间高亮带里）。
        /// 不再走系统默认的多行滚动——默认行为只挪视口、不改选中值，灰框与真实值会不一致。
        /// </summary>
        private void OnTimeWheel(object sender, MouseWheelEventArgs e)
        {
            ListBox list = sender as ListBox;
            if (list == null || list.Items.Count == 0)
            {
                return;
            }
            e.Handled = true;
            int next = list.SelectedIndex + (e.Delta > 0 ? -3 : 3);
            // 越界时"夹到边界"（而不是原地不动）：否则停在 01 时往上滚到不了 00，
            // 且末端的 22 / 23（分列 58 / 59）永远滚不到——首尾必须能停在中间高亮带里。
            if (next < 0)
            {
                next = 0;
            }
            if (next > list.Items.Count - 1)
            {
                next = list.Items.Count - 1;
            }
            list.SelectedIndex = next; // 触发 OnTimePartSelected → 回填 TimeBox + 重新居中
        }

        /// <summary>
        /// 点中任一滚轮项 → 与另一列的当前值合成 HH:mm 回填 TimeBox，**浮层保持打开**
        /// （可以接着改另一列；点浮层外面、按 Enter 或 Esc 才关闭）。
        /// 只回填输入框，保存仍由底栏「保存」按钮统一处理（校验走 TryParseTimeText）。
        /// </summary>
        private void OnTimePartSelected(object sender, SelectionChangedEventArgs e)
        {
            if (!TimePopup.IsOpen)
            {
                return; // 打开浮层前的程序定位不算用户选择
            }
            TimeSpan current;
            if (!ScheduleService.TryParseTimeText(TimeBox.Text, out current))
            {
                current = new TimeSpan(0, 0, 0);
            }
            int hour = HourList.SelectedIndex >= 0 ? HourList.SelectedIndex : current.Hours;
            int minute = MinuteList.SelectedIndex >= 0 ? MinuteList.SelectedIndex : current.Minutes;
            TimeBox.Text = DateUtil.FormatTime(new TimeSpan(hour, minute, 0));
            // 选中的那一格滚到中间：灰框即"当前生效的时间"，滚轮与鼠标点击都保持这一对应关系
            CenterWheel(sender as ListBox);
            // 不再自动关闭：方便继续调整另一列（时 / 分）
        }

        /// <summary>
        /// 滚轮浮层的键盘操作：Enter = 确认并关闭；Esc = 放弃本次改动并关闭（还原成打开前的时间）。
        /// 点浮层外部关闭由 Popup 的 StaysOpen=False 自动处理。
        /// </summary>
        private void OnTimePopupKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TimePopup.IsOpen = false;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                TimeBox.Text = _timeBeforePopup ?? TimeBox.Text;
                TimePopup.IsOpen = false;
                e.Handled = true;
            }
        }

        /// <summary>
        /// 把选中项滚到滚轮正中（对齐居中高亮带）。
        /// 全程只用**量出来的真实高度**，代码里不出现任何行高数字——
        /// 行高只在界面文件（WheelItemStyle 的 Height）里定义一处，改多少都不会滚歪。
        /// </summary>
        private static void CenterWheel(ListBox list)
        {
            if (list == null || list.SelectedIndex < 0)
            {
                return;
            }
            // 先让系统把选中项滚进可视范围：这一步之后，该项的容器才真正生成（虚拟化时尤其必要）
            list.ScrollIntoView(list.SelectedItem);
            list.UpdateLayout();

            ScrollViewer viewer = FindChildScrollViewer(list);
            ListBoxItem container = list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) as ListBoxItem;
            if (viewer == null || container == null || container.ActualHeight <= 0)
            {
                return; // 量不到就保持系统滚好的位置，绝不靠写死的数字去算
            }

            // 用选中项相对滚动区的真实位置，算出还需再挪多少才能居中
            Point topLeft = container.TransformToVisual(viewer).Transform(new Point(0, 0));
            double delta = topLeft.Y - (viewer.ViewportHeight - container.ActualHeight) / 2;
            viewer.ScrollToVerticalOffset(Math.Max(0, viewer.VerticalOffset + delta));
        }

        /// <summary>在可视树里按类型找第一个子元素（滚轮留白要拿 ItemsPresenter）。</summary>
        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                T typed = child as T;
                if (typed != null)
                {
                    return typed;
                }
                T found = FindChild<T>(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static ScrollViewer FindChildScrollViewer(DependencyObject parent)
        {
            if (parent == null)
            {
                return null;
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                ScrollViewer viewer = child as ScrollViewer;
                if (viewer != null)
                {
                    return viewer;
                }
                ScrollViewer found = FindChildScrollViewer(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        // ---------------- 其他 ----------------

        private void OnTitleTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCounters();
        }

        private void OnNoteTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCounters();
        }

        private void UpdateCounters()
        {
            TitleCounter.Text = (TitleBox.Text != null ? TitleBox.Text.Length : 0) + "/12";
            NoteCounter.Text = (NoteBox.Text != null ? NoteBox.Text.Length : 0) + "/500";
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnTitleBarMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void Flash(TextBox box)
        {
            box.BorderBrush = (Brush)FindResource("TextDangerBrush");
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(600);
            timer.Tick += delegate
            {
                box.SetResourceReference(TextBox.BorderBrushProperty, "BorderWeakBrush");
                timer.Stop();
            };
            timer.Start();
        }
    }
}
