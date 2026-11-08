using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScheduleReminder.Services;
using ScheduleReminder.Tools.Calculator;

namespace ScheduleReminder.Views
{
    /// <summary>
    /// 计算器窗（小工具）：非模态、单实例、可拖拽移动、可拖拽边框缩放、无最大化按钮。
    /// 关闭后不写任何存储（不落设置、不记位置尺寸），下次打开重新创建，初始为最小三段全空结构。
    /// </summary>
    public partial class CalculatorWindow : Window
    {
        private static CalculatorWindow _instance;

        private readonly CalcEngine _engine = new CalcEngine();

        public CalculatorWindow()
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();
            DataContext = _engine;

            // 随「界面大小」档位缩放：基准取本窗 XAML 的 Width/Height，随档位系数缩放。
            ThemeService.ApplyScale(this, Width, Height, MinWidth, MinHeight);
            Closed += OnClosed;
        }

        /// <summary>
        /// 统一打开入口：单实例——已经开着就激活并前置，否则新建。
        /// 入口接线（托盘菜单 / 主界面按钮 / 单元格右键菜单）都只调这一个方法。
        /// </summary>
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

            CalculatorWindow window = new CalculatorWindow();
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

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }
            NumberCell cell = button.DataContext as NumberCell;
            if (cell != null)
            {
                _engine.RemoveNumber(cell);
            }
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            _engine.Reset();
        }

        /// <summary>数字框输入过滤：只接受数字、小数点、负号；负号只能在首位，小数点只能出现一次。</summary>
        private void OnNumberPreviewTextInput(object sender, TextCompositionEventArgs e)
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

            TextBox box = sender as TextBox;
            if (box == null)
            {
                return;
            }
            if (text == "-")
            {
                if (box.CaretIndex != 0 || box.Text.IndexOf('-') >= 0)
                {
                    e.Handled = true;
                }
                return;
            }
            if (text == "." && box.Text.IndexOf('.') >= 0)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 粘贴过滤：与手输同一套规则——只留数字、小数点和负号，负号只能落在首位，小数点只留一个。
        /// 全角数字与全角符号先归一（中文输入法下粘出来的常是全角）；科学计数法（1e3、2.5E-4）
        /// 先解析成普通小数再写入，否则把 e 当普通字符剔掉会变成 13。
        /// </summary>
        private void OnNumberPasting(object sender, DataObjectPastingEventArgs e)
        {
            TextBox box = sender as TextBox;
            if (box == null || e == null || e.DataObject == null || !e.DataObject.GetDataPresent(DataFormats.Text))
            {
                if (e != null)
                {
                    e.CancelCommand();
                }
                return;
            }

            string text = e.DataObject.GetData(DataFormats.Text) as string;
            if (string.IsNullOrEmpty(text))
            {
                e.CancelCommand();
                return;
            }

            bool rewritten = false;
            if (text.IndexOf('e') >= 0 || text.IndexOf('E') >= 0)
            {
                double scientific;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out scientific)
                    || double.IsNaN(scientific) || double.IsInfinity(scientific))
                {
                    e.CancelCommand();
                    return;
                }
                text = scientific.ToString("0.############", CultureInfo.InvariantCulture);
                rewritten = true;
            }

            int start = box.SelectionStart;
            string before = box.Text;
            if (box.SelectionLength > 0)
            {
                before = before.Remove(start, box.SelectionLength);
            }
            string after = box.Text.Substring(start + box.SelectionLength);

            string candidate = before + text + after;
            string sanitized = SanitizeNumber(candidate);

            // 粘进来本来就合法：交给 TextBox 自己处理，保留撤销栈与原生光标行为
            if (!rewritten && sanitized == candidate)
            {
                return;
            }

            e.CancelCommand();
            box.Text = sanitized;
            box.CaretIndex = Math.Min(SanitizeNumber(before + text).Length, sanitized.Length);
        }

        /// <summary>只保留数字、一个小数点、一个位于首位的负号；全角字符先转半角。</summary>
        private static string SanitizeNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            bool dotUsed = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '０' && c <= '９')
                {
                    c = (char)(c - 0xFEE0);
                }
                else if (c == '。' || c == '．' || c == '｡')
                {
                    c = '.';
                }
                else if (c == '−' || c == '–' || c == '—' || c == '－' || c == '﹣')
                {
                    c = '-';
                }
                else if (c == ' ' || c == '\t' || c == ',' || c == '，')
                {
                    continue;
                }

                if (c >= '0' && c <= '9')
                {
                    builder.Append(c);
                }
                else if (c == '-' && builder.Length == 0)
                {
                    builder.Append(c);
                }
                else if (c == '.' && !dotUsed)
                {
                    builder.Append(c);
                    dotUsed = true;
                }
            }
            return builder.ToString();
        }
    }
}
