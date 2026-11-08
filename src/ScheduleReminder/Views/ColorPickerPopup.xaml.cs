using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScheduleReminder.Models;
using ScheduleReminder.Services;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 统一取色弹窗：预设色板（自动发现 App.xaml 的 PaletteC* 资源）+ 12 个自定义色框。
    /// 不提供「透明」选项（BUG-002：空值会击穿通用渲染路径导致崩溃；
    /// 「透明 / 跟随系统」语义由各窗口自己的专用小按钮承载，不进取色弹窗）。
    /// 空自定义框左键 → 弹系统 ColorDialog 取色并存入该框、即时应用；
    /// 已存自定义框左键 = 应用该色，右键 = 弹系统 ColorDialog 替换该框颜色（不应用、不关闭）。
    /// 自定义色持久化到 AppSettings.CustomColors（长度恒为 12，null = 空框）。
    /// </summary>
    public partial class ColorPickerPopup : Window
    {
        /// <summary>自定义色框数量（固定 12）。</summary>
        private const int CustomCount = 12;

        /// <summary>预设色板资源键前缀（PaletteC1 ~ PaletteC23，自动发现）。</summary>
        private const string PaletteKeyPrefix = "PaletteC";

        private readonly List<Button> _customButtons = new List<Button>();
        private readonly List<string> _customColors = new List<string>();

        private string _result;
        private string _currentColor;

        /// <summary>棋盘格空态画笔：引用 App.xaml 命名资源 CheckerBrush（全工程唯一棋盘格定义，见 OPT-005）。</summary>
        private Brush _checker;

        private ColorPickerPopup(string currentColor)
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();

            // 先赋值后建色块：当前色高亮（ApplyCurrentMark）依赖 _currentColor，
            // 旧实现在构造后才赋值导致高亮永不生效。
            _currentColor = currentColor;
            _checker = Application.Current != null
                ? Application.Current.TryFindResource("CheckerBrush") as Brush
                : null;

            BuildPresetSwatches();
            LoadCustomColors();
            BuildCustomSwatches();
        }

        // ================= 静态入口 =================

        /// <summary>
        /// 弹出取色窗。选定返回 "#RRGGBB"；取消/关闭返回 null。
        /// 不再返回空字符串 ""（「透明」选项已删除，空值语义由调用方专用按钮承载）。
        /// </summary>
        public static string Pick(string currentColor)
        {
            ColorPickerPopup popup = new ColorPickerPopup(currentColor);
            Window owner = FindOwnerWindow();
            if (owner != null)
            {
                popup.Owner = owner;
            }
            else
            {
                popup.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            popup.ShowDialog();
            return popup._result;
        }

        /// <summary>从后往前找第一个可见窗口作为 Owner（Owner 必须已 Show 过，否则赋值会抛异常）。</summary>
        internal static Window FindOwnerWindow()
        {
            if (Application.Current == null)
            {
                return null;
            }
            for (int i = Application.Current.Windows.Count - 1; i >= 0; i--)
            {
                Window window = Application.Current.Windows[i];
                if (window != null && window.IsVisible && !(window is ColorPickerPopup))
                {
                    return window;
                }
            }
            return null;
        }

        // ================= 预设色 =================

        /// <summary>按资源里所有 PaletteC* 自动生成预设色块（无「透明」块，见 BUG-002）。</summary>
        private void BuildPresetSwatches()
        {
            PresetPanel.Children.Clear();
            foreach (string key in DiscoverPaletteKeys())
            {
                SolidColorBrush brush = LookupBrush(key);
                string hex = brush != null ? ToHex(brush.Color) : "#C0C0C8";

                Button swatch = new Button();
                swatch.Style = (Style)FindResource("SwatchButtonStyle");
                if (brush != null)
                {
                    swatch.Background = new SolidColorBrush(brush.Color);
                }
                swatch.Tag = hex;
                swatch.ToolTip = hex;
                swatch.Click += OnPresetClick;
                ApplyCurrentMark(swatch, hex);
                PresetPanel.Children.Add(swatch);
            }
        }

        /// <summary>自动发现资源里所有 PaletteC* 画笔键，按末尾数字排序（新增预设色只需在 App.xaml 追加 PaletteC 资源）。</summary>
        private static List<string> DiscoverPaletteKeys()
        {
            List<string> keys = new List<string>();
            if (Application.Current != null)
            {
                foreach (var item in Application.Current.Resources.Keys)
                {
                    string key = item as string;
                    if (key != null && key.StartsWith(PaletteKeyPrefix, StringComparison.OrdinalIgnoreCase)
                        && key.Length > PaletteKeyPrefix.Length)
                    {
                        keys.Add(key);
                    }
                }
                keys.Sort((x, y) => ExtractPaletteIndex(x).CompareTo(ExtractPaletteIndex(y)));
            }
            return keys;
        }

        /// <summary>取 PaletteC 键末尾的数字序号（不能解析的排到最后）。</summary>
        private static int ExtractPaletteIndex(string key)
        {
            string num = key.Substring(PaletteKeyPrefix.Length);
            int value;
            if (int.TryParse(num, out value))
            {
                return value;
            }
            return int.MaxValue;
        }

        /// <summary>当前色命中时描边高亮（仅视觉提示，不影响取值）。</summary>
        private void ApplyCurrentMark(Button swatch, string hex)
        {
            if (string.IsNullOrEmpty(_currentColor))
            {
                return;
            }
            if (!string.Equals(hex, _currentColor, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            swatch.BorderBrush = FindBrush("AccentBrush");
            swatch.BorderThickness = new Thickness(2);
        }

        private static SolidColorBrush LookupBrush(string key)
        {
            if (Application.Current == null)
            {
                return null;
            }
            object resource = Application.Current.Resources[key];
            SolidColorBrush brush = resource as SolidColorBrush;
            if (brush != null)
            {
                return brush;
            }
            try
            {
                resource = Application.Current.TryFindResource(key);
                brush = resource as SolidColorBrush;
            }
            catch (Exception)
            {
                brush = null;
            }
            return brush;
        }

        private void OnPresetClick(object sender, RoutedEventArgs e)
        {
            Button swatch = sender as Button;
            if (swatch == null)
            {
                return;
            }
            _result = swatch.Tag as string;
            Close();
        }

        // ================= 自定义色 =================

        /// <summary>从 AppSettings.CustomColors 读入（不足 12 位补 null）。</summary>
        private void LoadCustomColors()
        {
            _customColors.Clear();
            AppSettings settings = AppServices.Settings;
            List<string> stored = settings != null ? settings.CustomColors : null;
            for (int i = 0; i < CustomCount; i++)
            {
                string value = null;
                if (stored != null && i < stored.Count)
                {
                    value = stored[i];
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = null;
                }
                _customColors.Add(value);
            }
        }

        /// <summary>生成 12 个自定义色框（空态 = 棋盘格）。</summary>
        private void BuildCustomSwatches()
        {
            CustomPanel.Children.Clear();
            _customButtons.Clear();
            for (int i = 0; i < CustomCount; i++)
            {
                Button swatch = new Button();
                swatch.Style = (Style)FindResource("SwatchButtonStyle");
                swatch.Tag = i;
                swatch.Click += OnCustomClick;
                swatch.PreviewMouseRightButtonUp += OnCustomRightClick;
                RefreshCustomButton(swatch, i);
                _customButtons.Add(swatch);
                CustomPanel.Children.Add(swatch);
            }
        }

        /// <summary>刷新第 index 个自定义色框的外观与提示。</summary>
        private void RefreshCustomButton(Button swatch, int index)
        {
            string color = _customColors[index];
            if (string.IsNullOrEmpty(color))
            {
                // 空态棋盘格：引用命名资源 CheckerBrush（与 App.xaml 权威定义一致，见 OPT-005）
                swatch.Background = _checker ?? Brushes.Transparent;
                swatch.ToolTip = "空（左键取色）";
            }
            else
            {
                SolidColorBrush brush = new SolidColorBrush(ThemeService.ParseColor(color));
                swatch.Background = brush;
                swatch.ToolTip = color;
            }
        }

        /// <summary>左键：空框 → 取色 + 存入 + 应用 + 关闭；已存框 → 应用 + 关闭。</summary>
        private void OnCustomClick(object sender, RoutedEventArgs e)
        {
            Button swatch = sender as Button;
            if (swatch == null || !(swatch.Tag is int))
            {
                return;
            }
            int index = (int)swatch.Tag;
            if (string.IsNullOrEmpty(_customColors[index]))
            {
                // 空框无旧色可预置 → 用当前色作为系统取色器起始色（推荐路径，见 BUG-003）
                string picked = PickSystemColor(_currentColor);
                if (picked == null)
                {
                    return;
                }
                SetCustomColor(index, picked);
                _result = picked;
                Close();
                return;
            }
            _result = _customColors[index];
            Close();
        }

        /// <summary>右键：弹系统取色器替换该框颜色（不应用、不关闭）。</summary>
        private void OnCustomRightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Button swatch = sender as Button;
            if (swatch == null || !(swatch.Tag is int))
            {
                return;
            }
            int index = (int)swatch.Tag;
            // 右键替换：以该框自身颜色作为系统取色器起始色（BUG-003）
            string picked = PickSystemColor(_customColors[index]);
            if (picked == null)
            {
                return;
            }
            SetCustomColor(index, picked);
        }

        /// <summary>写入内存 + 刷新外观 + 落盘（失败由 AppServices.SaveSettings 内部告警）。</summary>
        private void SetCustomColor(int index, string color)
        {
            if (index < 0 || index >= _customColors.Count)
            {
                return;
            }
            _customColors[index] = color;

            AppSettings settings = AppServices.Settings;
            if (settings != null)
            {
                if (settings.CustomColors == null)
                {
                    settings.CustomColors = new List<string>();
                }
                while (settings.CustomColors.Count < CustomCount)
                {
                    settings.CustomColors.Add(null);
                }
                if (settings.CustomColors.Count > CustomCount)
                {
                    settings.CustomColors.RemoveRange(CustomCount, settings.CustomColors.Count - CustomCount);
                }
                settings.CustomColors[index] = color;
                AppServices.SaveSettings();
            }

            if (index < _customButtons.Count)
            {
                RefreshCustomButton(_customButtons[index], index);
            }
        }

        /// <summary>
        /// 打开系统取色器；返回 "#RRGGBB"，取消返回 null。
        /// initialColor：预置到系统取色器的起始色（如右键框自身颜色）；空/非法回退默认蓝紫。
        /// </summary>
        private static string PickSystemColor(string initialColor = null)
        {
            try
            {
                System.Windows.Forms.ColorDialog dialog = new System.Windows.Forms.ColorDialog();
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                dialog.SolidColorOnly = true;
                dialog.AllowFullOpen = true;
                dialog.Color = ResolveInitialColor(initialColor);

                System.Windows.Forms.DialogResult dialogResult = dialog.ShowDialog();
                if (dialogResult != System.Windows.Forms.DialogResult.OK)
                {
                    return null;
                }
                System.Drawing.Color color = dialog.Color;
                return ToHex(color.R, color.G, color.B);
            }
            catch (Exception ex)
            {
                LogWriter.Write("打开系统取色器失败", ex);
                return null;
            }
        }

        /// <summary>系统取色器起始色：合法色串 → 解析预置；否则默认蓝紫 #FF6C8CFF。</summary>
        private static System.Drawing.Color ResolveInitialColor(string initialColor)
        {
            if (!string.IsNullOrEmpty(initialColor))
            {
                try
                {
                    Color parsed = ThemeService.ParseColor(initialColor);
                    return System.Drawing.Color.FromArgb(parsed.A, parsed.R, parsed.G, parsed.B);
                }
                catch (Exception)
                {
                    // 非法色串（损坏的存档等）→ 回退默认蓝紫
                }
            }
            return System.Drawing.Color.FromArgb(255, 108, 140, 255);
        }

        // ================= 通用工具 =================

        /// <summary>Color → "#RRGGBB"（含 alpha 时按 RGB 输出）。</summary>
        internal static string ToHex(Color color)
        {
            return ToHex(color.R, color.G, color.B);
        }

        private static string ToHex(byte r, byte g, byte b)
        {
            return "#" + r.ToString("X2", CultureInfo.InvariantCulture)
                       + g.ToString("X2", CultureInfo.InvariantCulture)
                       + b.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>按资源键取画笔；取不到返回 null（不抛异常）。</summary>
        private Brush FindBrush(string key)
        {
            object resource = TryFindResource(key);
            return resource as Brush;
        }

        // ================= 标题栏 / 关闭 =================

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _result = null;
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            _result = null;
            Close();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _result = null;
                Close();
                e.Handled = true;
            }
        }
    }
}
