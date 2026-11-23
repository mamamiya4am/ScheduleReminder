using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ScheduleReminder.Domain;
using ScheduleReminder.Models;
using ScheduleReminder.Services;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 标签管理窗：左栏编辑、右栏列表，新增 / 修改 / 删除都在本窗内完成。
    ///
    /// 标签只是一份「套用到日程上的样式预设」。修改保存时按 TagId 把新值推送到命中的日程，
    /// 写盘成功才写入标签数据；任一步失败都恢复标签字段并提示。
    ///
    /// 非模态、单实例：统一从 Open() 打开，已经开着就激活并前置，关闭后清空引用。
    /// </summary>
    public partial class TagManagerWindow : Window
    {
        private static TagManagerWindow _instance;

        /// <summary>标签名最大字数：超出部分由输入框自动截断（含粘贴）。</summary>
        private const int MaxNameLength = 6;

        private SoundPicker _soundPicker;
        private TagTemplate _editing;      // null = 新建态
        private string _bgColor;
        private string _fontColor;
        private bool _suppressSelection;

        public TagManagerWindow()
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();

            // 参与界面缩放（与其它窗口一致）；关窗必须注销
            ThemeService.ApplyScale(this, Width, Height, MinWidth, MinHeight);

            Closed += OnClosed;

            RefreshList(null);
            BeginCreate();
        }

        /// <summary>
        /// 统一打开入口：单实例——已经开着就刷新数据、激活并前置，否则新建。
        /// 入口（主界面菜单）只调这一个方法，避免开出多个标签窗。
        /// </summary>
        public static void Open()
        {
            if (_instance != null)
            {
                _instance.RefreshList(null);
                _instance.BeginCreate();
                if (_instance.WindowState == WindowState.Minimized)
                {
                    _instance.WindowState = WindowState.Normal;
                }
                _instance.Activate();
                return;
            }

            TagManagerWindow window = new TagManagerWindow();
            _instance = window;
            window.Closed += OnInstanceClosed;
            window.Show();
        }

        private static void OnInstanceClosed(object sender, EventArgs e)
        {
            _instance = null;
        }

        /// <summary>列表行：只承载标签名（列表不显示色块）。</summary>
        private sealed class TagRow
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }

        // ---------------- 标题栏 / 关闭 ----------------

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 双击标题栏等极端情况下 DragMove 会抛异常，忽略即可
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

        private void OnClosed(object sender, EventArgs e)
        {
            if (_soundPicker != null)
            {
                _soundPicker.Dispose();
            }
            AppServices.Sound.StopPreview();
            ThemeService.UnregisterScale(this);
        }

        // ---------------- 列表 ----------------

        private void RefreshList(string selectId)
        {
            _suppressSelection = true;
            try
            {
                List<TagRow> rows = new List<TagRow>();
                List<TagTemplate> all = TagTemplateWindow.Templates();
                for (int i = 0; i < all.Count; i++)
                {
                    TagTemplate t = all[i];
                    if (t == null || string.IsNullOrEmpty(t.Name))
                    {
                        continue;
                    }
                    rows.Add(new TagRow { Id = t.Id, Name = t.Name });
                }
                TagList.ItemsSource = rows;

                if (!string.IsNullOrEmpty(selectId))
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (string.Equals(rows[i].Id, selectId, StringComparison.Ordinal))
                        {
                            TagList.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _suppressSelection = false;
            }
        }

        private void OnTagListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection)
            {
                return;
            }
            TagRow row = TagList.SelectedItem as TagRow;
            if (row == null)
            {
                return;
            }
            TagTemplate tag = FindTemplateById(row.Id);
            // 兼容分支：仅 id 为空的异常标签；正常标签不会进入。
            if (tag == null && string.IsNullOrEmpty(row.Id))
            {
                tag = FindLegacyTemplateByName(row.Name);
            }
            if (tag != null)
            {
                LoadTag(tag);
            }
        }

        private void OnNewClick(object sender, RoutedEventArgs e)
        {
            BeginCreate();
        }

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            if (_editing == null)
            {
                return;
            }
            NameBox.Focus();
            NameBox.SelectAll();
        }

        // ---------------- 编辑态 ----------------

        private void BeginCreate()
        {
            _editing = null;
            EditTitleText.Text = "新建标签";
            NameBox.Text = string.Empty;
            _bgColor = null;
            _fontColor = null;
            SetSwatches();
            UpdatePreview();
            ReloadSound(AppServices.Settings != null ? AppServices.Settings.SoundFilePath : null);

            _suppressSelection = true;
            try
            {
                TagList.SelectedItem = null;
            }
            finally
            {
                _suppressSelection = false;
            }

            UpdateNameCounter();
            NameBox.Focus();
        }

        private void LoadTag(TagTemplate tag)
        {
            if (tag == null)
            {
                return;
            }
            _editing = tag;
            EditTitleText.Text = "修改标签";
            NameBox.Text = tag.Name;
            _bgColor = tag.BgColor;
            _fontColor = tag.FontColor;
            SetSwatches();
            UpdatePreview();
            ReloadSound(tag.Sound);
            UpdateNameCounter();
        }

        private void ReloadSound(string selected)
        {
            if (_soundPicker != null)
            {
                _soundPicker.Dispose();
            }
            _soundPicker = SoundPicker.Load(SoundCombo, selected);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            // 取消 = 放弃当前编辑，回到新建态；窗口本身保持打开
            BeginCreate();
        }

        private void OnNameTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateNameCounter();
        }

        private void UpdateNameCounter()
        {
            NameCounter.Text = ((NameBox.Text ?? string.Empty).Trim()).Length + "/" + MaxNameLength;
        }

        // ---------------- 颜色 ----------------

        private void OnBgColorClick(object sender, RoutedEventArgs e)
        {
            // 取色弹窗无「透明」选项，null 表示取消 → 保持原值
            string picked = ColorPickerPopup.Pick(_bgColor);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }
            _bgColor = picked;
            SetSwatches();
            UpdatePreview();
        }

        private void OnBgTransparentClick(object sender, RoutedEventArgs e)
        {
            _bgColor = null;
            SetSwatches();
            UpdatePreview();
        }

        private void OnFontColorClick(object sender, RoutedEventArgs e)
        {
            string picked = ColorPickerPopup.Pick(_fontColor);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }
            _fontColor = picked;
            SetSwatches();
            UpdatePreview();
        }

        private void OnFontFollowSystemClick(object sender, RoutedEventArgs e)
        {
            _fontColor = null;
            SetSwatches();
            UpdatePreview();
        }

        private void SetSwatches()
        {
            SetSwatch(BgRect, _bgColor);
            SetFontSwatch();
        }

        /// <summary>
        /// 字体颜色色块：未设置（跟随系统文字色）时直接引用 TextPrimaryBrush 动态画笔，
        /// 这样改「系统文字色」时色块也实时同步。
        /// </summary>
        private void SetFontSwatch()
        {
            if (!string.IsNullOrEmpty(_fontColor))
            {
                Color parsed;
                if (TryParseColor(_fontColor, out parsed))
                {
                    FontRect.Fill = FrozenBrush(parsed);
                    return;
                }
            }
            FontRect.SetResourceReference(Rectangle.FillProperty, "TextPrimaryBrush");
        }

        private void SetSwatch(Rectangle rect, string color)
        {
            if (string.IsNullOrEmpty(color))
            {
                rect.Fill = (Brush)FindResource("CheckerBrush");
                return;
            }
            Color parsed;
            if (!TryParseColor(color, out parsed))
            {
                rect.Fill = (Brush)FindResource("CheckerBrush");
                return;
            }
            rect.Fill = FrozenBrush(parsed);
        }

        private static Brush FrozenBrush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>预览条：背景取所设背景色（空则透明），文字取所设字体色（空则跟随系统）。</summary>
        private void UpdatePreview()
        {
            Brush bg = ParseBrush(_bgColor);
            PreviewBar.Background = bg ?? Brushes.Transparent;
            PreviewTag.Background = ThemeService.ScheduleHoverBrush(_bgColor);

            if (string.IsNullOrEmpty(_fontColor))
            {
                PreviewTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                PreviewTagText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            }
            else
            {
                Brush fg = ParseBrush(_fontColor) ?? (Brush)FindResource("TextPrimaryBrush");
                PreviewTitle.Foreground = fg;
                PreviewTagText.Foreground = fg;
            }
        }

        // ---------------- 保存 / 删除 ----------------

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            string name = (NameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                NameBox.Focus();
                return;
            }
            if (name.Length > MaxNameLength)
            {
                return;
            }

            string sound = _soundPicker != null ? _soundPicker.SelectedName() : null;
            if (string.Equals(sound, SoundLibrary.NoneOption, StringComparison.Ordinal))
            {
                sound = null;
            }

            if (_editing == null)
            {
                SaveNew(name, sound);
            }
            else
            {
                SaveEdit(name, sound);
            }
        }

        private void SaveNew(string name, string sound)
        {
            if (AppServices.Tags == null)
            {
                return;
            }
            TagTemplate created = new TagTemplate(name, _bgColor, _fontColor, sound);
            created.Id = IdGenerator.NewTagId();

            List<TagTemplate> candidate = new List<TagTemplate>(TagTemplateWindow.Templates());
            candidate.Add(created);
            SaveOutcome outcome = AppServices.Tags.TrySave(candidate);
            if (outcome != SaveOutcome.Ok)
            {
                DialogService.ShowSaveFailure(outcome, "tags.json");
                return;
            }

            AppServices.RaiseDataChanged();
            RefreshList(created.Id);
            LoadTag(created);
        }

        private void SaveEdit(string name, string sound)
        {
            TagTemplate target = _editing;
            string oldName = target.Name;
            string oldBg = target.BgColor;
            string oldFont = target.FontColor;
            string oldSound = target.Sound;

            target.Name = name;
            target.BgColor = _bgColor;
            target.FontColor = _fontColor;
            target.Sound = sound;

            // 先推日程、后写标签
            if (!PushTagToSchedules(target))
            {
                target.Name = oldName;
                target.BgColor = oldBg;
                target.FontColor = oldFont;
                target.Sound = oldSound;
                return;
            }

            SaveOutcome tagOutcome = AppServices.Tags == null
                ? SaveOutcome.Ok
                : AppServices.Tags.TrySave(new List<TagTemplate>(TagTemplateWindow.Templates()));
            if (tagOutcome != SaveOutcome.Ok)
            {
                target.Name = oldName;
                target.BgColor = oldBg;
                target.FontColor = oldFont;
                target.Sound = oldSound;
                DialogService.ShowSaveFailure(tagOutcome, "tags.json");
                // 日程侧已同步成功，必须刷新界面，避免日历仍显示旧颜色
                AppServices.RaiseDataChanged();
                RefreshList(target.Id);
                return;
            }

            AppServices.RaiseDataChanged();
            RefreshList(target.Id);
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_editing == null)
            {
                return;
            }
            string name = _editing.Name ?? string.Empty;
            bool ok = MessageDialog.ShowConfirm(
                "删除标签",
                "确定删除标签「" + name + "」吗？\n\n此操作不可撤销。",
                "删除",
                true);
            if (!ok)
            {
                return;
            }

            if (AppServices.Tags != null)
            {
                List<TagTemplate> candidate = new List<TagTemplate>(TagTemplateWindow.Templates());
                candidate.Remove(_editing);
                SaveOutcome outcome = AppServices.Tags.TrySave(candidate);
                if (outcome != SaveOutcome.Ok)
                {
                    DialogService.ShowSaveFailure(outcome, "tags.json");
                    return;
                }
            }

            AppServices.RaiseDataChanged();
            RefreshList(null);
            BeginCreate();
        }

        /// <summary>
        /// 标签修改后，按 TagId 把新名字/颜色/铃声推送到命中的日程。
        /// 返回 false 表示日程保存失败，调用方需要撤销模板字段改动。
        /// </summary>
        private bool PushTagToSchedules(TagTemplate target)
        {
            if (target == null || string.IsNullOrEmpty(target.Id)
                || AppServices.Repository == null)
            {
                return true;
            }

            SaveOutcome outcome = ScheduleTagService.PushToSchedules(target);
            if (outcome == SaveOutcome.Ok)
            {
                return true;
            }

            DialogService.ShowSaveFailure(outcome, "schedules.json");
            return false;
        }

        // ---------------- 查找 / 解析 ----------------

        private TagTemplate FindTemplateById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            List<TagTemplate> all = TagTemplateWindow.Templates();
            for (int i = 0; i < all.Count; i++)
            {
                TagTemplate t = all[i];
                if (t != null && string.Equals(t.Id, id, StringComparison.Ordinal))
                {
                    return t;
                }
            }
            return null;
        }

        /// <summary>
        /// 【兼容分支，正常数据不会进入】
        /// 仅当 tags.json 中存在「有 name、没有 id」的异常标签时，按名字兜底查找。
        /// </summary>
        private TagTemplate FindLegacyTemplateByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            List<TagTemplate> all = TagTemplateWindow.Templates();
            for (int i = 0; i < all.Count; i++)
            {
                TagTemplate t = all[i];
                if (t != null && string.IsNullOrEmpty(t.Id)
                    && string.Equals(t.Name, name, StringComparison.Ordinal))
                {
                    return t;
                }
            }
            return null;
        }

        private static Brush ParseBrush(string hex)
        {
            Color parsed;
            if (!TryParseColor(hex, out parsed))
            {
                return null;
            }
            return FrozenBrush(parsed);
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrEmpty(hex))
            {
                return false;
            }
            string s = hex.TrimStart('#');
            if (s.Length != 6)
            {
                return false;
            }
            try
            {
                color = Color.FromRgb(
                    byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
