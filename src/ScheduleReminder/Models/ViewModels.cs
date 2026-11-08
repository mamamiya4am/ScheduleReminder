using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Models
{
    /// <summary>极简属性变更通知基类。</summary>
    public abstract class NotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    /// <summary>月历单元格里的一条日程显示项。</summary>
    public class ScheduleItemVm
    {
        public string TimeText { get; set; }

        public string TitleText { get; set; }

        public string BgColor { get; set; }

        public string FontColor { get; set; }

        public bool HasColor
        {
            get { return !string.IsNullOrEmpty(BgColor); }
        }

        /// <summary>月历格内的显示文本：「HH:MM 标题」。</summary>
        public string DisplayText
        {
            get { return TimeText + " " + TitleText; }
        }
    }

    /// <summary>悬停预览浮层的一行（纯展示，无操作按钮）。</summary>
    public class PreviewRowVm
    {
        public string TimeText { get; set; }

        public string TitleText { get; set; }

        public string AdvanceText { get; set; }

        public string NoteText { get; set; }

        public string TagText { get; set; }

        /// <summary>是否有标签（无标签时显示“-”但不画边框）。</summary>
        public bool HasTag { get; set; }

        /// <summary>标签背景：跟随该日程背景色的反色块；无标签时透明。</summary>
        public Brush TagBackgroundBrush { get; set; }

        public string BgColor { get; set; }

        public string FontColor { get; set; }

        /// <summary>是否开启提醒（悬停预览只读状态图标）。</summary>
        public bool RemindEnabled { get; set; }

        /// <summary>是否待办（悬停预览只读状态图标）。</summary>
        public bool IsTodo { get; set; }

        /// <summary>alarmTime &lt; now：整行暗灰，但停留原时间位不沉底。</summary>
        public bool IsPast { get; set; }
    }

    /// <summary>月历单元格视图模型（42 格）。</summary>
    public class DayCellVm : NotifyBase
    {
        private bool _isHovered;
        private bool _isSelected;

        public DateTime Date { get; set; }

        public bool IsCurrentMonth { get; set; }

        public bool IsToday { get; set; }

        public bool IsPast { get; set; }

        /// <summary>日期号：仅日、无前导零（.NET 自定义格式 %d）。</summary>
        public string DayText
        {
            get { return Date.Day.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        }

        /// <summary>右上角小字：节假日 &gt; 节气 &gt; 农历（仅「日」）。</summary>
        public string SubText { get; set; }

        /// <summary>该日**全部**日程（渲染子集与隐藏条数都由它派生，不随显示条数变化）。</summary>
        public List<ScheduleItemVm> AllItems { get; set; }

        private List<ScheduleItemVm> _items = new List<ScheduleItemVm>();
        private int _hiddenCount;

        /// <summary>
        /// 当前**实际渲染**的日程（按格子可用高度动态截取，OPT-023）。
        /// 带变更通知：替换整个列表即可让界面重新生成行。
        /// </summary>
        public List<ScheduleItemVm> Items
        {
            get { return _items; }
            set
            {
                _items = value ?? new List<ScheduleItemVm>();
                RaisePropertyChanged("Items");
            }
        }

        /// <summary>该日日程总条数（不受显示截断影响）。</summary>
        public int TotalCount
        {
            get { return AllItems != null ? AllItems.Count : 0; }
        }

        /// <summary>因格子高度放不下而未显示的条数（0 = 全部显示）。</summary>
        public int HiddenCount
        {
            get { return _hiddenCount; }
            set
            {
                if (_hiddenCount != value)
                {
                    _hiddenCount = value;
                    RaisePropertyChanged("HiddenCount");
                    RaisePropertyChanged("HasMore");
                    RaisePropertyChanged("MoreText");
                }
            }
        }

        /// <summary>底部溢出行文案「+N」；没有隐藏时为空（该行不显示）。</summary>
        public string MoreText
        {
            get { return _hiddenCount > 0 ? "+" + _hiddenCount.ToString(CultureInfo.InvariantCulture) : string.Empty; }
        }

        /// <summary>是否还有未显示完的日程（随格子高度动态变化，OPT-023）。</summary>
        public bool HasMore
        {
            get { return _hiddenCount > 0; }
        }

        public bool IsHovered
        {
            get { return _isHovered; }
            set
            {
                if (_isHovered != value)
                {
                    _isHovered = value;
                    RaisePropertyChanged("IsHovered");
                }
            }
        }

        /// <summary>是否被单击选中（选中态用「焦点边框颜色」常驻高亮，提示用户当前选中的是哪一天）。</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    RaisePropertyChanged("IsSelected");
                }
            }
        }

        /// <summary>
        /// 该日是否有日程（无日程则不弹悬停预览）。
        /// 必须按**总条数**判断：条目被格子高度隐藏时，仍然要能弹预览（OPT-023）。
        /// </summary>
        public bool HasSchedule
        {
            get { return TotalCount > 0; }
        }

        private int _todoPendingCount;

        /// <summary>过去日期中未完成的待办数（月格角标）。</summary>
        public int TodoPendingCount
        {
            get { return _todoPendingCount; }
            set
            {
                if (_todoPendingCount != value)
                {
                    _todoPendingCount = value;
                    RaisePropertyChanged("TodoPendingCount");
                    RaisePropertyChanged("TodoBadgeText");
                    RaisePropertyChanged("HasTodoPending");
                }
            }
        }

        public string TodoBadgeText
        {
            get { return _todoPendingCount.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        }

        public bool HasTodoPending
        {
            get { return _todoPendingCount > 0; }
        }

        public DayCellVm()
        {
            SubText = string.Empty;
            AllItems = new List<ScheduleItemVm>();
            Items = new List<ScheduleItemVm>();
        }
    }

    /// <summary>周视图顶部日期行的一项。</summary>
    public class WeekDayVm : NotifyBase
    {
        private bool _isSelected;

        public DateTime Date { get; set; }

        public string DayText
        {
            get { return Date.Day.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        }

        public string SubText { get; set; }

        public bool IsToday { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    RaisePropertyChanged("IsSelected");
                }
            }
        }
    }

    /// <summary>右侧「当日待办事项」面板的一行。</summary>
    public class TodoItemVm : NotifyBase
    {
        private bool _isDone;

        public Schedule Schedule { get; set; }

        public DateTime OccurrenceDate { get; set; }

        public string TimeText { get; set; }

        public string TitleText { get; set; }

        /// <summary>日程背景色 / 字体色：右侧待办面板沿用日程色彩设置。</summary>
        public string BgColor { get; set; }

        public string FontColor { get; set; }

        /// <summary>完成勾选框勾色/边框：跟随该日程背景色的反色。</summary>
        public Brush CheckBrush { get; set; }

        /// <summary>勾选框悬停底色：同一日程背景色的低透明度反色块。</summary>
        public Brush HoverBrush { get; set; }

        public bool IsDone
        {
            get { return _isDone; }
            set
            {
                if (_isDone != value)
                {
                    _isDone = value;
                    RaisePropertyChanged("IsDone");
                }
            }
        }
    }

    /// <summary>日程编辑窗的一行（含重复日程展开命中项）。</summary>
    public class ScheduleRowVm : NotifyBase
    {
        private bool _remindEnabled;
        private bool _hasError;

        public Schedule Schedule { get; set; }

        public DateTime OccurrenceDate { get; set; }

        public DateTime AlarmTime { get; set; }

        private bool _isPast;
        public bool IsPast
        {
            get { return _isPast; }
            set
            {
                if (_isPast != value)
                {
                    _isPast = value;
                    RaisePropertyChanged("IsPast");
                    RaisePropertyChanged("CanToggleRemind");
                    RaisePropertyChanged("CanToggleTodo");
                }
            }
        }

        public bool CanToggleRemind
        {
            get { return !_isPast; }
        }

        public bool CanToggleTodo
        {
            get { return true; }
        }

        public bool RemindEnabled
        {
            get { return _remindEnabled; }
            set
            {
                if (_remindEnabled != value)
                {
                    _remindEnabled = value;
                    RaisePropertyChanged("RemindEnabled");
                }
            }
        }

        private bool _isDone;
        public bool IsDone
        {
            get { return _isDone; }
            set
            {
                if (_isDone != value)
                {
                    _isDone = value;
                    RaisePropertyChanged("IsDone");
                }
            }
        }

        private bool _isTodo;
        public bool IsTodo
        {
            get { return _isTodo; }
            set
            {
                if (_isTodo != value)
                {
                    _isTodo = value;
                    RaisePropertyChanged("IsTodo");
                }
            }
        }

        // 这几个显示属性需要通知：单元格编辑「切走丢弃」时回写原值以刷新 UI（否则自动属性不触发更新）。
        private string _timeText;
        public string TimeText
        {
            get { return _timeText; }
            set { if (_timeText != value) { _timeText = value; RaisePropertyChanged("TimeText"); } }
        }

        private string _titleText;
        public string TitleText
        {
            get { return _titleText; }
            set { if (_titleText != value) { _titleText = value; RaisePropertyChanged("TitleText"); } }
        }

        private string _advanceText;
        public string AdvanceText
        {
            get { return _advanceText; }
            set { if (_advanceText != value) { _advanceText = value; RaisePropertyChanged("AdvanceText"); } }
        }

        private string _noteText;
        public string NoteText
        {
            get { return _noteText; }
            set { if (_noteText != value) { _noteText = value; RaisePropertyChanged("NoteText"); } }
        }

        public string TagText { get; set; }

        /// <summary>是否有标签（无标签时显示“-”但不画边框）。</summary>
        public bool HasTag { get; set; }

        /// <summary>标签背景：跟随该日程背景色的反色块；无标签时透明。</summary>
        public Brush TagBackgroundBrush { get; set; }

        /// <summary>行内按钮悬停背景：跟随该日程背景色的反色块。</summary>
        public Brush HoverBrush { get; set; }

        /// <summary>完成勾选框勾色：同一反色判定，实色。</summary>
        public Brush CheckBrush { get; set; }

        public string RecurrenceText { get; set; }

        public string BgColor { get; set; }

        public string FontColor { get; set; }

        /// <summary>内联编辑校验失败 / 边界秒不满足时短暂置 true（整行淡红，不弹窗）。</summary>
        public bool HasError
        {
            get { return _hasError; }
            set
            {
                if (_hasError != value)
                {
                    _hasError = value;
                    RaisePropertyChanged("HasError");
                }
            }
        }


    }

    /// <summary>提醒队列条目（提前提醒 / 正点提醒）。</summary>
    public class ReminderEntry
    {
        public string ScheduleId { get; set; }

        public string SeriesId { get; set; }

        public DateTime OccurrenceDate { get; set; }

        public DateTime FireTime { get; set; }

        /// <summary>0 = 提前提醒，1 = 正点提醒。</summary>
        public int Kind { get; set; }

        public string Title { get; set; }

        public string TimeText { get; set; }

        public string Note { get; set; }

        public string Tag { get; set; }

        public string BgColor { get; set; }

        public string FontColor { get; set; }

        /// <summary>
        /// 本条日程自己选的音效名（取自 Schedule.Sound.Preset）。
        /// null / 空 ＝ 静音（只弹窗、不响），不再回落设置默认。
        /// </summary>
        public string SoundName { get; set; }

        public int AdvanceMinutes { get; set; }

        /// <summary>提前提醒剩余分钟数（正点提醒为 0 且不显示）。</summary>
        public int RemainingMinutes { get; set; }

        public bool IsAdvance
        {
            get { return Kind == 0; }
        }

        public string Key
        {
            get { return ScheduleId + "|" + OccurrenceDate.ToString("yyyy-MM-dd") + "|" + Kind; }
        }
    }

    /// <summary>删除操作的执行计划（用于二次确认文案与批量清理）。</summary>
    public class DeletePlan
    {
        public DeleteKind Kind { get; set; }

        public List<Schedule> Targets { get; set; }

        public Schedule Source { get; set; }

        public string Title { get; set; }

        public DeletePlan()
        {
            Kind = DeleteKind.RemoveOne;
            Targets = new List<Schedule>();
        }
    }

    public enum DeleteKind
    {
        /// <summary>删除本条（一次性日程 / 尚未生效的重复段）。</summary>
        RemoveOne = 0,

        /// <summary>软删除：收敛 until 停止未来重复，保留过往记录。</summary>
        StopFuture = 1,

        /// <summary>批量清除同一 seriesId 的全部已过期记录。</summary>
        PurgeExpired = 2
    }
}
