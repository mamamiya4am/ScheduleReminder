using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using ScheduleReminder.Models;
using ScheduleReminder.Services;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 系统设置窗：悬停预览延迟 / 外观主题 / 提示音 / 开机自启 / AI 投递 / 数据管理 / 预设标签。
    /// 所有改动即时进行「设置对象 → 主题应用」的实时预览，关闭时统一落盘 + 通知刷新。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // 提醒铃：全程序共用的 SoundPicker 组件（列表 / 试听 / 展开刷新都交给它）
        private SoundPicker _soundPicker;

        public SettingsWindow()
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();
            // 尺寸在首次渲染前定死：设计尺寸只维护在 XAML，这里读它作为 100% 缩放基准（避免打开时跳一下）。
            ThemeService.ApplyScale(this, Width, Height, MinWidth, MinHeight);
            Closing += OnSettingsClosing;
            InitializeControls();
            RefreshTagList();
        }

        // ================= 初始化 =================

        private void InitializeControls()
        {
            AppSettings s = AppServices.Settings;
            if (s == null)
            {
                return;
            }

            HoverBox.Text = s.HoverDelaySeconds.ToString("0.##", CultureInfo.InvariantCulture);
            UpdateRect(TextRect, s.GlobalTextColor);
            UpdateRect(BorderRect, s.SystemBorderColor);
            UpdateRect(TodayRect, s.TodayBorderColor);
            UpdateRect(WindowBgRect, s.WindowBgColor);
            UpdateRect(FocusRect, s.FocusBorderColor);
            UpdateRect(ControlFillRect, s.ControlFillColor);
            ImagePathBox.Text = string.IsNullOrEmpty(s.WindowBgImage)
                ? string.Empty
                : System.IO.Path.GetFileName(s.WindowBgImage);
            ImagePathBox.ToolTip = BackgroundImageStore.ResolveFullPath(s.WindowBgImage)
                ?? "只显示文件名；如需更换请点「浏览」";
            SelectStretch(s.WindowBgStretch);
            OpacityBox.Text = s.WindowBgOpacity.ToString(CultureInfo.InvariantCulture);

            SelectScale(s.UiScalePercent);

            SoundLibrary.EnsureDirectory();
            _soundPicker = SoundPicker.Load(SoundCombo, s.SoundFilePath, OnSoundSelected);
            AutoStartCheck.IsChecked = AutoStartService.IsEnabled();
            AiIntegrationCheck.IsChecked = s.AiIntegrationEnabled;
        }

        // ================= 标题栏 / 关闭 =================

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            AppServices.SaveSettings();
            AppServices.RaiseDataChanged();
            Close();
        }

        private void OnSettingsClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 标题栏 X / Alt+F4 关闭时也落盘，避免缩放等改动丢失。
            AppServices.SaveSettings();
            // 关窗即静音：试听中的声音不该在窗口关闭后继续响
            StopSoundPreview();
            // 日程色块的底色是在绑定时由转换器算出的（非资源），关窗时触发一次数据刷新让它重算新的透明度。
            AppServices.RaiseDataChanged();
        }

        /// <summary>停止试听：停掉待播计时器，并停止正在播放的声音（含未播完的长音频）。</summary>
        private void StopSoundPreview()
        {
            if (_soundPicker != null)
            {
                _soundPicker.Dispose();
            }
            if (AppServices.Sound != null)
            {
                AppServices.Sound.StopPreview(); // 只停试听，不打扰正在响的提醒
            }
        }

        // ================= 悬停延迟 =================

        private void OnHoverBoxLostFocus(object sender, RoutedEventArgs e)
        {
            AppSettings s = AppServices.Settings;
            if (s == null)
            {
                return;
            }
            double value;
            if (double.TryParse(HoverBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.01)
            {
                s.HoverDelaySeconds = value;
            }
            else
            {
                // 非法输入时回退到「已保存的值」，而不是最小值
                // （例如已存 0.15，输入 "." 后仍显示 0.15）
            }
            HoverBox.Text = s.HoverDelaySeconds.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // ================= 外观颜色 =================

        private void OnPickTextColor(object sender, RoutedEventArgs e)
        {
            PickInto(() => AppServices.Settings.GlobalTextColor, v => AppServices.Settings.GlobalTextColor = v, TextRect);
        }

        private void OnResetTextColor(object sender, RoutedEventArgs e)
        {
            AppServices.Settings.GlobalTextColor = AppSettings.DefaultGlobalTextColor;
            UpdateRect(TextRect, AppSettings.DefaultGlobalTextColor);
            ThemeService.ApplySettings();
        }

        private void OnPickBorderColor(object sender, RoutedEventArgs e)
        {
            PickInto(() => AppServices.Settings.SystemBorderColor, v => AppServices.Settings.SystemBorderColor = v, BorderRect);
        }

        private void OnResetBorderColor(object sender, RoutedEventArgs e)
        {
            AppServices.Settings.SystemBorderColor = AppSettings.DefaultSystemBorderColor;
            UpdateRect(BorderRect, AppSettings.DefaultSystemBorderColor);
            ThemeService.ApplySettings();
        }

        private void OnPickTodayColor(object sender, RoutedEventArgs e)
        {
            PickInto(() => AppServices.Settings.TodayBorderColor, v => AppServices.Settings.TodayBorderColor = v, TodayRect);
        }

        private void OnResetTodayColor(object sender, RoutedEventArgs e)
        {
            AppServices.Settings.TodayBorderColor = AppSettings.DefaultTodayBorderColor;
            UpdateRect(TodayRect, AppSettings.DefaultTodayBorderColor);
            ThemeService.ApplySettings();
        }

        private void OnPickWindowBgColor(object sender, RoutedEventArgs e)
        {
            PickInto(() => AppServices.Settings.WindowBgColor, v => AppServices.Settings.WindowBgColor = v, WindowBgRect);
        }

        private void OnResetWindowBgColor(object sender, RoutedEventArgs e)
        {
            AppServices.Settings.WindowBgColor = AppSettings.DefaultWindowBgColor;
            UpdateRect(WindowBgRect, AppSettings.DefaultWindowBgColor);
            ThemeService.ApplySettings();
        }

        private void OnPickFocusColor(object sender, RoutedEventArgs e)
        {
            PickInto(() => AppServices.Settings.FocusBorderColor,
                     v => AppServices.Settings.FocusBorderColor = v, FocusRect);
        }

        private void OnResetFocusColor(object sender, RoutedEventArgs e)
        {
            AppServices.Settings.FocusBorderColor = AppSettings.DefaultFocusBorderColor;
            UpdateRect(FocusRect, AppSettings.DefaultFocusBorderColor);
            ThemeService.ApplySettings();
        }

        private void OnPickControlFillColor(object sender, RoutedEventArgs e)
        {
            PickInto(() => AppServices.Settings.ControlFillColor,
                     v => AppServices.Settings.ControlFillColor = v, ControlFillRect);
        }

        private void OnResetControlFillColor(object sender, RoutedEventArgs e)
        {
            AppServices.Settings.ControlFillColor = AppSettings.DefaultControlFillColor;
            UpdateRect(ControlFillRect, AppSettings.DefaultControlFillColor);
            ThemeService.ApplySettings();
        }

        /// <summary>
        /// 取色并写回设置项。
        /// 注意：不可写成 ref string——C# 不允许把属性用作 ref/out 实参（CS0206），故用 getter/setter 委托。
        /// null = 取消；空串 = 异常路径写入的空值（取色器已无「透明」选项，正常不会出现）——
        /// 一律不改设置（BUG-002：设置窗颜色字段不允许空值，空值兜底由 AppSettings.Normalize 统一恢复默认）。
        /// </summary>
        private void PickInto(Func<string> getter, Action<string> setter, Rectangle rect)
        {
            string picked = ColorPickerPopup.Pick(getter());
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }
            setter(picked);
            UpdateRect(rect, picked);
            ThemeService.ApplySettings();
        }

        /// <summary>
        /// 刷新色块外观：显示该设置项的当前颜色；颜色为空时显示棋盘格（表示未设置）。
        /// </summary>
        private static void UpdateRect(Rectangle rect, string color)
        {
            if (rect == null)
            {
                return;
            }
            rect.Fill = new SolidColorBrush(ThemeService.ParseColor(color));
        }

        // ================= 背景图片 =================

        private void OnBrowseImage(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
            dialog.Title = "选择背景图片";
            if (dialog.ShowDialog() == true)
            {
                ApplyBackgroundImage(dialog.FileName);
            }
        }

        /// <summary>
        /// 背景图片的业务动作（只由「浏览」/「无背景」按钮调用）：
        /// - 空 → 清除背景图：释放已解码位图，并删掉内部仓库里那张图
        /// - 非空 → 先复制进内部仓库（data\background，改名），设置里只存文件名并即时生效，
        ///          最后把仓库里旧的那张清掉（目录即镜像）
        /// </summary>
        private void ApplyBackgroundImage(string path)
        {
            AppSettings s = AppServices.Settings;
            if (s == null)
            {
                return;
            }

            string trimmed = (path ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                s.WindowBgImage = null;
                ImagePathBox.Text = string.Empty;
                ImagePathBox.ToolTip = "只显示文件名；如需更换请点「浏览」";
                ThemeService.ApplySettings();
                ThemeService.ReleaseBackgroundCache();    // 已解码的位图真正释放
                BackgroundImageStore.CleanupExcept(null); // 内部副本一并删除
                return;
            }

            // 只由「浏览」按钮调用，trimmed 必为对话框返回的绝对路径；Import 返回内部文件名
            string name = BackgroundImageStore.Import(trimmed);
            if (string.IsNullOrEmpty(name))
            {
                // 复制失败（目录不可写等）：不写设置、不删旧图，保持原样
                ImagePathBox.Text = string.IsNullOrEmpty(s.WindowBgImage)
                    ? string.Empty
                    : System.IO.Path.GetFileName(s.WindowBgImage);
                MessageDialog.ShowAlert("背景图片保存失败",
                    "无法把图片复制到程序数据目录，已恢复为原来的设置：\r\n" + trimmed);
                return;
            }

            s.WindowBgImage = name;
            ImagePathBox.Text = name;
            ImagePathBox.ToolTip = BackgroundImageStore.ResolveFullPath(name);
            ThemeService.ApplySettings();
            BackgroundImageStore.CleanupExcept(name); // 换图：旧的那张在这里被删掉
        }

        /// <summary>清除背景图片：回到纯色背景（输入框同步清空，已解码的位图一并释放）。</summary>
        private void OnClearBgImageClick(object sender, RoutedEventArgs e)
        {
            AppSettings s = AppServices.Settings;
            if (s == null)
            {
                return;
            }
            ApplyBackgroundImage(string.Empty); // 走统一的业务动作：清路径 + 回纯色 + 释放位图
        }

        private void OnStretchChanged(object sender, EventArgs e)
        {
            AppSettings s = AppServices.Settings;
            if (s == null || StretchCombo.SelectedItem == null)
            {
                return;
            }
            ComboBoxItem item = StretchCombo.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag is string)
            {
                s.WindowBgStretch = (string)item.Tag;
            }
            ThemeService.ApplySettings();
        }

        private void OnOpacityLostFocus(object sender, RoutedEventArgs e)
        {
            AppSettings s = AppServices.Settings;
            if (s == null)
            {
                return;
            }
            int value;
            // 合法范围 10–100（OPT-006）：低于 10 会让窗口背景 / 控件底色几乎完全透明，
            // 整个窗口无法拖拽与选中，处于不可用状态。
            if (int.TryParse(OpacityBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                && value >= AppSettings.MinWindowBgOpacity && value <= AppSettings.MaxWindowBgOpacity)
            {
                s.WindowBgOpacity = value;
                // 透明度与颜色同一条路径：只改画刷 alpha（背景 / 控件底色 / 日程底色），文字与边框保持不透明
                ThemeService.ApplySettings();
            }
            else
            {
                // 非法或超范围 → 回退到「已保存的值」（不改成 50 或 100）
            }
            OpacityBox.Text = s.WindowBgOpacity.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 设置文本框的「回车保存」（OPT-007）：背景透明度 / 悬停延迟两框
        /// 按下 Enter 即执行与失焦完全相同的提交逻辑（共用方法、幂等），提交后吞掉按键。
        /// 失焦保存保持不变，两种方式并存；背景图片框只读、无此需求，预设标签输入框属「输入→点按钮」动作框。
        /// </summary>
        private void OnSettingBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }
            TextBox box = sender as TextBox;
            if (box == null)
            {
                return;
            }
            if (box == OpacityBox)
            {
                OnOpacityLostFocus(box, e);
            }
            else if (box == HoverBox)
            {
                OnHoverBoxLostFocus(box, e);
            }
            else
            {
                return;
            }
            e.Handled = true;
        }

        /// <summary>
        /// 数字输入框的统一过滤：只允许「数字 + 最多一个小数点」。
        /// 说明：WPF 没有内置的数字输入框控件，这是必要的输入过滤；
        ///      仅靠它仍不能保证值合法（如 "." 或 "0.001"），所以失焦时还要再做一次解析校验。
        /// </summary>
        private void OnNumericPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsNumericInput(sender as TextBox, e.Text);
        }

        /// <summary>拦截粘贴，防止把非数字内容粘进来（绕过输入过滤）。</summary>
        private void OnNumericPasting(object sender, DataObjectPastingEventArgs e)
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
            if (!IsNumericInput(sender as TextBox, data as string))
            {
                e.CancelCommand();
            }
        }

        private static bool IsNumericInput(TextBox box, string input)
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
                    // 只允许一个小数点
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

        /// <summary>选中当前背景填充方式（取值来自本下拉的档位）。</summary>
        private void SelectStretch(string stretch)
        {
            for (int i = 0; i < StretchCombo.Items.Count; i++)
            {
                ComboBoxItem item = StretchCombo.Items[i] as ComboBoxItem;
                if (item != null && string.Equals(item.Tag as string, stretch, StringComparison.OrdinalIgnoreCase))
                {
                    StretchCombo.SelectedIndex = i;
                    return;
                }
            }
            StretchCombo.SelectedIndex = 0;
        }

        /// <summary>选中当前缩放档位（取值必为 AppSettings.ValidScalePercents 之一）。</summary>
        private void SelectScale(int percent)
        {
            for (int i = 0; i < ScaleCombo.Items.Count; i++)
            {
                ComboBoxItem item = ScaleCombo.Items[i] as ComboBoxItem;
                if (item != null && item.Tag is string
                    && int.Parse((string)item.Tag, CultureInfo.InvariantCulture) == percent)
                {
                    ScaleCombo.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>切换整体缩放档位：写入设置并让所有已打开窗口实时重算（含本设置窗、主窗、提醒浮层）。</summary>
        private void OnScaleChanged(object sender, EventArgs e)
        {
            AppSettings s = AppServices.Settings;
            if (s == null || ScaleCombo.SelectedItem == null)
            {
                return;
            }
            ComboBoxItem item = ScaleCombo.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag is string)
            {
                int percent;
                if (int.TryParse((string)item.Tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out percent))
                {
                    s.UiScalePercent = percent;
                }
            }
            ThemeService.ReapplyScaleToAllWindows();
        }

        // ================= 提示音 =================

        /// <summary>
        /// 用户在下拉展开态下改选铃声 → 即时存盘并让新铃声立即生效。
        /// 等价于原 OnSoundComboChanged 里的「存设置 + Sound.Configure」；试听由 SoundPicker 内部处理。
        /// </summary>
        private void OnSoundSelected(string name)
        {
            AppSettings s = AppServices.Settings;
            if (s == null)
            {
                return;
            }
            // 「-」= 不播放声音。这里**必须存成 "-"，不能存 null**：
            // null 表示「从没选过」，启动时会自动填成第一个音效；只有 "-" 才是用户明确的「不要声音」。
            s.SoundFilePath = string.IsNullOrEmpty(name) ? SoundLibrary.NoneOption : name;
            AppServices.SaveSettings();
            if (AppServices.Sound != null)
            {
                AppServices.Sound.Configure(s.SoundFilePath);
            }
        }

        /// <summary>打开音效目录，方便用户自己增删文件；下次展开下拉时 SoundPicker 会自动刷新列表。</summary>
        private void OnOpenSoundFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!SoundLibrary.EnsureDirectory())
                {
                    DialogService.Alert("音效目录", "无法创建音效目录：\r\n" + SoundLibrary.Directory);
                    return;
                }
                Process.Start("explorer.exe", SoundLibrary.Directory);
                if (_soundPicker != null)
                {
                    _soundPicker.Refresh();
                }
            }
            catch (Exception ex)
            {
                LogWriter.Write("打开音效目录失败", ex);
                DialogService.Alert("音效目录", "打开失败：" + ex.Message);
            }
        }

        // ================= 开机自启 =================

        private void OnAutoStartClick(object sender, RoutedEventArgs e)
        {
            bool enable = AutoStartCheck.IsChecked == true;
            bool ok = AutoStartService.SetEnabled(enable);
            if (!ok)
            {
                AutoStartCheck.IsChecked = AutoStartService.IsEnabled();
            }
        }

        // ================= AI 投递 =================

        private void OnExportAiRuleClick(object sender, RoutedEventArgs e)
        {
            string text;
            using (System.IO.Stream stream = typeof(SettingsWindow).Assembly
                .GetManifestResourceStream("ScheduleReminder.AI操作规则.md"))
            using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
            {
                text = reader.ReadToEnd();
            }

            string baseDirectory = AppPaths.BaseDirectory.Replace('\\', '/').TrimEnd('/');
            text = text.Replace("<程序目录>", baseDirectory);

            string folder = System.IO.Path.Combine(AppPaths.BaseDirectory, "AI操作规则");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(folder, "将此文件给AI读取.md"),
                text,
                new System.Text.UTF8Encoding(false));

            DialogService.Alert(
                "AI 操作规则",
                "已生成 AI 操作规则：\n请将该文件提供给 AI以供其阅读规则。");

            Process.Start("explorer.exe", "\"" + folder + "\"");
        }

        private void OnAiIntegrationClick(object sender, RoutedEventArgs e)
        {
            if (AppServices.Settings == null)
            {
                return;
            }
            AppServices.Settings.AiIntegrationEnabled = AiIntegrationCheck.IsChecked == true;
            AppServices.SaveSettings();
            if (AppServices.Settings.AiIntegrationEnabled)
            {
                AiSnapshotWriter.Write();
            }
        }

        // ================= 数据管理 =================

        private void OnOpenDataFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AppPaths.EnsureDataDirectory())
                {
                    Process.Start(AppPaths.DataDirectory);
                }
            }
            catch (Exception ex)
            {
                LogWriter.Write("打开数据目录失败", ex);
                DialogService.Alert("数据目录", "打开数据目录失败：" + ex.Message);
            }
        }

        private void OnExportJson(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "JSON|*.json";
            dialog.FileName = "schedules_export.json";
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    JsonStore.Save(dialog.FileName, AppServices.Repository != null ? AppServices.Repository.All : null);
                    DialogService.Alert("导出成功", "已导出到：\r\n" + dialog.FileName);
                }
                catch (Exception ex)
                {
                    LogWriter.Write("导出 JSON 失败", ex);
                    DialogService.Alert("导出失败", "导出 JSON 失败：" + ex.Message);
                }
            }
        }

        private void OnExportCsv(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "CSV|*.csv";
            dialog.FileName = "schedules_export.csv";
            if (dialog.ShowDialog() != true)
            {
                return;
            }
            try
            {
                List<Schedule> all = AppServices.Repository != null ? AppServices.Repository.All : new List<Schedule>();
                List<string> lines = new List<string>();
                lines.Add("标题,日期,时间,提前提醒(分),标签,重复,备注,背景色,文字色");
                for (int i = 0; i < all.Count; i++)
                {
                    Schedule item = all[i];
                    if (item == null)
                    {
                        continue;
                    }
                    lines.Add(string.Join(",", new string[]
                    {
                        CsvCell(item.Title),
                        item.Date,
                        item.Time,
                        item.AdvanceMinutes.ToString(CultureInfo.InvariantCulture),
                        CsvCell(item.Tag),
                        CsvCell(RecurrenceService.Describe(item)),
                        CsvCell(item.Note),
                        CsvCell(item.BgColor),
                        CsvCell(item.FontColor)
                    }));
                }
                File.WriteAllLines(dialog.FileName, lines, System.Text.Encoding.UTF8);
                DialogService.Alert("导出成功", "已导出到：\r\n" + dialog.FileName);
            }
            catch (Exception ex)
            {
                LogWriter.Write("导出 CSV 失败", ex);
                DialogService.Alert("导出失败", "导出 CSV 失败：" + ex.Message);
            }
        }

        private static string CsvCell(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            if (text.IndexOf(',') >= 0 || text.IndexOf('"') >= 0 || text.IndexOf('\n') >= 0)
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }
            return text;
        }

        // ================= 预设标签 =================

        /// <summary>列表行：色块 + 名字。</summary>
        private sealed class TagRow
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public Brush Bg { get; set; }
        }

        private void RefreshTagList()
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
                rows.Add(new TagRow { Id = t.Id, Name = t.Name, Bg = BrushForTemplate(t.BgColor) });
            }
            TagList.ItemsSource = rows;
        }

        /// <summary>背景色 -> 色块画笔；空（透明）用棋盘格，与日程条里的观感一致。</summary>
        private static Brush BrushForTemplate(string color)
        {
            if (string.IsNullOrEmpty(color))
            {
                object checker = Application.Current != null ? Application.Current.TryFindResource("CheckerBrush") : null;
                Brush brush = checker as Brush;
                return brush ?? Brushes.Transparent;
            }
            try
            {
                SolidColorBrush solid = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                solid.Freeze();
                return solid;
            }
            catch (Exception)
            {
                return Brushes.Transparent;
            }
        }

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
        /// 仅当 tags.json 中存在“有 name、没有 id”的异常/手工标签时，
        /// 设置窗编辑/删除才按名字兜底查找。
        /// 该分支不会补 Id、不会回填日程 tagId，也不参与 JSON 损坏恢复；
        /// 正常新建或迁移后的标签一律走 FindTemplateById。
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

        /// <summary>新建标签：弹出标签编辑窗（标签名 / 背景色 / 字体色 / 提醒铃）。</summary>
        private void OnCreateTagClick(object sender, RoutedEventArgs e)
        {
            if (AppServices.Settings == null)
            {
                return;
            }
            TagTemplateWindow win = new TagTemplateWindow(null) { Owner = this };
            // 出现在设置窗当前位置（含被拖动后的位置）；窗口尺寸固定、不可缩放
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            win.Left = this.Left + 20;
            win.Top = this.Top + 20;
            bool? confirmed = win.ShowDialog();
            if (confirmed == true && win.Result != null)
            {
                win.Result.Id = IdGenerator.NewTagId();
                if (AppServices.Tags != null)
                {
                    List<TagTemplate> candidate = new List<TagTemplate>(
                        TagTemplateWindow.Templates());
                    candidate.Add(win.Result);
                    SaveOutcome outcome = AppServices.Tags.TrySave(candidate);
                    if (outcome != SaveOutcome.Ok)
                    {
                        DialogService.ShowSaveFailure(outcome, "tags.json");
                    }
                }
                RefreshTagList();
            }
        }

        /// <summary>修改标签：编辑窗只负责改样式；删除由下方标签列表的「删除」按钮负责。</summary>
        private void OnEditTagClick(object sender, RoutedEventArgs e)
        {
            TagRow row = TagList.SelectedItem as TagRow;
            if (row == null)
            {
                return;
            }
            TagTemplate target = FindTemplateById(row.Id);
            // 兼容分支：仅 id 为空的异常标签；正常标签不会进入。
            if (target == null && string.IsNullOrEmpty(row.Id))
            {
                target = FindLegacyTemplateByName(row.Name);
            }
            if (target == null)
            {
                return;
            }

            TagTemplateWindow win = new TagTemplateWindow(target) { Owner = this };
            // 出现在设置窗当前位置（含被拖动后的位置）；窗口尺寸固定、不可缩放
            win.WindowStartupLocation = WindowStartupLocation.Manual;
            win.Left = this.Left;
            win.Top = this.Top;
            bool? confirmed = win.ShowDialog();
            if (confirmed != true)
            {
                return;
            }
            if (win.Result != null)
            {
                string oldName = target.Name;
                string oldBg = target.BgColor;
                string oldFont = target.FontColor;
                string oldSound = target.Sound;

                target.Name = win.Result.Name;
                target.BgColor = win.Result.BgColor;
                target.FontColor = win.Result.FontColor;
                target.Sound = win.Result.Sound;

                if (!PushTagToSchedules(target))
                {
                    target.Name = oldName;
                    target.BgColor = oldBg;
                    target.FontColor = oldFont;
                    target.Sound = oldSound;
                    RefreshTagList();
                    return;
                }

                SaveOutcome tagOutcome = AppServices.Tags == null
                    ? SaveOutcome.Ok
                    : AppServices.Tags.TrySave(new List<TagTemplate>(
                        TagTemplateWindow.Templates()));
                if (tagOutcome != SaveOutcome.Ok)
                {
                    target.Name = oldName;
                    target.BgColor = oldBg;
                    target.FontColor = oldFont;
                    target.Sound = oldSound;
                    DialogService.ShowSaveFailure(tagOutcome, "tags.json");
                    // 日程侧已同步成功，必须刷新界面，避免日历仍显示旧颜色
                    AppServices.RaiseDataChanged();
                    RefreshTagList();
                    return;
                }
                AppServices.RaiseDataChanged();
                RefreshTagList();
            }
        }

        private void OnRemoveTagClick(object sender, RoutedEventArgs e)
        {
            TagRow row = TagList.SelectedItem as TagRow;
            if (row == null)
            {
                return;
            }
            TagTemplate target = FindTemplateById(row.Id);
            // 兼容分支：仅 id 为空的异常标签；正常标签不会进入。
            if (target == null && string.IsNullOrEmpty(row.Id))
            {
                target = FindLegacyTemplateByName(row.Name);
            }
            if (target == null)
            {
                return;
            }
            bool ok = MessageDialog.ShowConfirm(
                "删除标签",
                "确定删除标签「" + row.Name + "」吗？\n\n此操作不可撤销。",
                "删除",
                true);
            if (!ok)
            {
                return;
            }
            if (AppServices.Tags != null)
            {
                List<TagTemplate> candidate = new List<TagTemplate>(
                    TagTemplateWindow.Templates());
                candidate.Remove(target);
                SaveOutcome outcome = AppServices.Tags.TrySave(candidate);
                if (outcome != SaveOutcome.Ok)
                {
                    DialogService.ShowSaveFailure(outcome, "tags.json");
                }
            }
            RefreshTagList();
        }

        /// <summary>
        /// 标签修改后，按 TagId 把新名字/颜色/铃声推送到命中的日程。
        /// 在候选列表上克隆修改，写盘成功才替换正式数据。
        /// 返回 false 表示日程没有保存成功，调用方需要撤销模板字段改动。
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
    }
}
