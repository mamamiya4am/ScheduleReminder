using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ScheduleReminder.Domain;
using ScheduleReminder.Models;
using ScheduleReminder.Services;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 标签（预设模板）编辑窗：新建 / 编辑（删除由设置窗列表负责）。
    ///
    /// 标签只是一份「套用到日程上的样式预设」：本窗改的是模板本身，
    /// 一行日程都不会被碰到；日程的样式在套用的那一刻就被拷贝走。
    /// 因此这里保存 / 删除模板都不需要考虑「是否仍被日程使用」。
    ///
    /// 必须以 ShowDialog() 打开（保存 / 删除通过 DialogResult 回传）。
    /// </summary>
    public partial class TagTemplateWindow : Window
    {
        /// <summary>标签名最大字数：超出部分由输入框自动截断（含粘贴）。</summary>
        private const int MaxNameLength = 6;

        private readonly TagTemplate _editing;      // 非 null = 编辑态（副本，不动原对象）
        private readonly SoundPicker _soundPicker;

        private string _bgColor;
        private string _fontColor;

        public TagTemplateWindow(TagTemplate source)
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();

            _editing = source == null ? null : source.Clone();
            _bgColor = _editing != null ? _editing.BgColor : null;
            _fontColor = _editing != null ? _editing.FontColor : null;

            bool isEdit = _editing != null;
            TitleText.Text = isEdit ? "编辑标签" : "新建标签";

            // 参与界面缩放（与「新建日程」一致）；关窗必须注销，否则窗体无法回收
            ThemeService.ApplyScale(this, Width, Height, MinWidth, MinHeight);

            // 音效下拉：编辑态回显已存铃声；新建态继承设置里的默认铃声（与新建日程窗一致）
            string initialSound = _editing != null
                ? _editing.Sound
                : (AppServices.Settings != null ? AppServices.Settings.SoundFilePath : null);
            _soundPicker = SoundPicker.Load(SoundCombo, initialSound);

            SetSwatches();
            if (isEdit)
            {
                NameBox.Text = _editing.Name;
            }
            UpdateNameCounter();

            Closed += OnClosed;
        }

        /// <summary>保存成功后有效：本次编辑得到的模板（新建 / 编辑都从这里取）。</summary>
        public TagTemplate Result { get; private set; }

        /// <summary>取当前标签模板列表；标签已独立存放在 data\tags.json。</summary>
        public static List<TagTemplate> Templates()
        {
            return AppServices.Tags != null
                ? AppServices.Tags.All
                : new List<TagTemplate>();
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

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
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

        // ---------------- 标签名 ----------------

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
            // 取色弹窗无「透明」选项（BUG-002），null 表示取消 → 保持原值
            string picked = ColorPickerPopup.Pick(_bgColor);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }
            _bgColor = picked;
            SetSwatches();
        }

        private void OnBgTransparentClick(object sender, RoutedEventArgs e)
        {
            _bgColor = null;
            SetSwatches();
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
        }

        private void OnFontFollowSystemClick(object sender, RoutedEventArgs e)
        {
            _fontColor = null;
            SetSwatches();
        }

        private void SetSwatches()
        {
            SetSwatch(BgRect, _bgColor);
            SetFontSwatch();
        }

        /// <summary>
        /// 字体颜色色块：与「新建日程」的 SetFontSwatch 完全一致。
        /// 已单独设置 → 显示该颜色；未设置（跟随系统文字色）→ 直接引用 TextPrimaryBrush 动态画笔，
        /// 这样改「系统文字色」时色块也实时同步（不再落成棋盘格空态）。
        /// </summary>
        private void SetFontSwatch()
        {
            if (!string.IsNullOrEmpty(_fontColor))
            {
                Color parsed;
                if (TryParseColor(_fontColor, out parsed))
                {
                    SolidColorBrush brush = new SolidColorBrush(parsed);
                    brush.Freeze();
                    FontRect.Fill = brush;
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
            SolidColorBrush brush = new SolidColorBrush(parsed);
            brush.Freeze();
            rect.Fill = brush;
        }

        // ---------------- 保存 / 删除 ----------------

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            string name = (NameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return;
            }
            if (name.Length > MaxNameLength)
            {
                return;
            }
            string sound = _soundPicker.SelectedName();
            if (string.Equals(sound, SoundLibrary.NoneOption, StringComparison.Ordinal))
            {
                sound = null;
            }

            Result = new TagTemplate(name, _bgColor, _fontColor, sound);
            DialogResult = true;
        }

        // ---------------- 颜色解析 ----------------

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