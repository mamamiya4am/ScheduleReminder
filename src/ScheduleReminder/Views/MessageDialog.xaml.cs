using System;
using System.Windows;
using System.Windows.Input;
using ScheduleReminder.Services;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Views
{
    /// <summary>统一的确认 / 提示 / 只读告警弹窗（置顶模态）。</summary>
    public partial class MessageDialog : Window
    {
        private bool _result;

        public MessageDialog()
        {
            InitializeComponent();
            Icon = IconHelper.GetImageSource();
        }

        private void Configure(string title, string message, string okText, string cancelText, bool danger)
        {
            TitleText.Text = title;
            MessageText.Text = message;
            OkButton.Content = string.IsNullOrEmpty(okText) ? "确定" : okText;
            if (cancelText == null)
            {
                CancelButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                CancelButton.Visibility = Visibility.Visible;
                CancelButton.Content = cancelText;
            }
            // OPT-019：确认键不再用固定主按钮蓝，默认走普通按钮方案（底色/边框/文字随设置变化）
            OkButton.Style = danger
                ? (Style)FindResource("DangerButtonStyle")
                : (Style)FindResource("NormalButtonStyle");
            _result = false;
        }

        /// <summary>二次确认；返回 true 表示点了确认按钮。</summary>
        public static bool ShowConfirm(string title, string message, string okText, bool danger)
        {
            MessageDialog dialog = new MessageDialog();
            dialog.Configure(title, message, okText, "取消", danger);
            dialog.ShowDialog();
            return dialog._result;
        }

        public static void ShowAlert(string title, string message)
        {
            MessageDialog dialog = new MessageDialog();
            dialog.Configure(title, message, "确定", null, false);
            dialog.ShowDialog();
        }

        /// <summary>写盘失败时的置顶模态告警。</summary>
        public static void ShowReadOnlyError()
        {
            MessageDialog dialog = new MessageDialog();
            dialog.Configure("无法写入数据",
                "当前空间只读，请移动到其他硬盘空间再试\r\n\r\n"
                + "错误代码：" + Services.DataErrorCode.SaveReadOnly,
                "确定", null, true);
            dialog.ShowDialog();
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            _result = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _result = false;
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            _result = false;
            Close();
        }

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }
    }
}
