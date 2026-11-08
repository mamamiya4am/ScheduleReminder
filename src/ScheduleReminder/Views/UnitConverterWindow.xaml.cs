using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScheduleReminder.Services;
using ScheduleReminder.Tools.UnitConverter;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 单位换算窗（小工具）：非模态、单实例、可拖拽移动、可拖拽边框缩放、无最大化按钮。
    /// 关闭后不写任何存储：不落设置、不记位置尺寸、不保留任何输入。
    /// 界面与行为以原型《单位换算窗.html》为基准：类别 → 单个源单位 → 该类其余全部单位的结果列表。
    /// </summary>
    public partial class UnitConverterWindow : Window
    {
        private static UnitConverterWindow _instance;

        private readonly ObservableCollection<UnitResultRow> _results = new ObservableCollection<UnitResultRow>();

        public UnitConverterWindow()
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();

            CategoryBox.ItemsSource = UnitCatalog.Categories;
            CategoryBox.SelectedIndex = 0;
            ResultList.ItemsSource = _results;
            // 显式重建一次单位列表：不依赖 SelectionChanged 的触发时序，避免单位下拉偶发为空
            ApplyCategory();

            // 随「界面大小」档位缩放：基准取本窗 XAML 的 Width/Height。
            ThemeService.ApplyScale(this, Width, Height, MinWidth, MinHeight);
            Closed += OnClosed;

            Recalculate();
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

            UnitConverterWindow window = new UnitConverterWindow();
            _instance = window;
            window.Closed += OnInstanceClosed;
            window.Show();
        }

        private static void OnInstanceClosed(object sender, EventArgs e)
        {
            _instance = null;
        }

        private void OnClosed(object sender, EventArgs e)
        {
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

        /// <summary>切换类别：重建单位列表并选中首项，输入值保留，随即重算。</summary>
        private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyCategory();
        }

        /// <summary>重建当前类别的单位列表并选中首项；构造时也会显式调用一次。</summary>
        private void ApplyCategory()
        {
            UnitCategory category = CategoryBox.SelectedItem as UnitCategory;
            if (category == null)
            {
                return;
            }
            UnitBox.ItemsSource = category.Units;
            UnitBox.SelectedIndex = 0;
            Recalculate();
        }

        private void OnUnitChanged(object sender, SelectionChangedEventArgs e)
        {
            Recalculate();
        }

        private void OnValueTextChanged(object sender, TextChangedEventArgs e)
        {
            Recalculate();
        }

        /// <summary>输入过滤：只接受数字、小数点、负号；负号只在首位且只有一个，小数点只保留一个。</summary>
        private void OnValuePreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            string text = e.Text;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!char.IsDigit(c) && c != '.' && c != '-')
                {
                    e.Handled = true;
                    return;
                }
            }
            if (text == "." && ValueBox.Text.IndexOf('.') >= 0)
            {
                e.Handled = true;
            }
            if (text == "-" && (ValueBox.CaretIndex != 0 || ValueBox.Text.IndexOf('-') >= 0))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 粘贴：先做字符清洗；如果是科学计数法（含 e/E），先解析成普通数字再写入，避免 1e5 变成 15。
        /// </summary>
        private void OnValuePasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e == null || e.DataObject == null || !e.DataObject.GetDataPresent(DataFormats.Text))
            {
                return;
            }
            string text = e.DataObject.GetData(DataFormats.Text) as string;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (text.IndexOf('e') >= 0 || text.IndexOf('E') >= 0)
            {
                double scientific;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out scientific) &&
                    !double.IsNaN(scientific) && !double.IsInfinity(scientific))
                {
                    e.CancelCommand();
                    ValueBox.Text = UnitCatalog.Sanitize(scientific.ToString("0.############", CultureInfo.InvariantCulture));
                    ValueBox.CaretIndex = ValueBox.Text.Length;
                }
                return;
            }

            string cleaned = UnitCatalog.Sanitize(text);
            if (cleaned == text)
            {
                return;
            }

            e.CancelCommand();
            int start = ValueBox.SelectionStart;
            string before = ValueBox.Text;
            if (ValueBox.SelectionLength > 0)
            {
                before = before.Remove(start, ValueBox.SelectionLength);
            }
            ValueBox.Text = before.Insert(start, cleaned);
            ValueBox.CaretIndex = start + cleaned.Length;
        }

        /// <summary>重算：非温度走系数，温度以摄氏中转后钳制到 −273.15。</summary>
        private void Recalculate()
        {
            _results.Clear();

            UnitCategory category = CategoryBox.SelectedItem as UnitCategory;
            UnitDefinition source = UnitBox.SelectedItem as UnitDefinition;
            string text = UnitCatalog.Sanitize(ValueBox.Text);

            if (category == null || source == null)
            {
                PlaceholderText.Visibility = Visibility.Visible;
                return;
            }

            // 输入为空 / 非法时也先把该类的目标单位列出来，只是值显示为「—」；
            // 用户不必先输入数字才能看到下面有哪些单位。
            double value = 0;
            if (UnitCatalog.IsEmptyInput(text)
                || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                PlaceholderText.Visibility = Visibility.Collapsed;
                for (int i = 0; i < category.Units.Count; i++)
                {
                    UnitDefinition target = category.Units[i];
                    if (target.Name == source.Name)
                    {
                        continue;
                    }
                    _results.Add(new UnitResultRow
                    {
                        UnitName = target.Name,
                        ValueText = "—"
                    });
                }
                return;
            }

            PlaceholderText.Visibility = Visibility.Collapsed;

            double baseValue = UnitCatalog.ToBase(source, value);
            if (category.IsTemperature && baseValue < UnitCatalog.AbsoluteZeroCelsius)
            {
                baseValue = UnitCatalog.AbsoluteZeroCelsius;
            }

            for (int i = 0; i < category.Units.Count; i++)
            {
                UnitDefinition target = category.Units[i];
                if (target.Name == source.Name)
                {
                    continue;
                }
                _results.Add(new UnitResultRow
                {
                    UnitName = target.Name,
                    ValueText = UnitCatalog.Format(UnitCatalog.FromBase(target, baseValue))
                });
            }
        }
    }
}
