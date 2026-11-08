using System;
using System.Windows.Forms;
using ScheduleReminder.Views;
using Application = System.Windows.Application;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 系统托盘常驻（BCL System.Windows.Forms.NotifyIcon，零 NuGet）。
    /// 右键菜单：创建日程 / 打开 / 分隔线 / 退出程序。
    /// （设置窗已设 Owner，需在主窗存在时打开，故托盘不提供入口）
    /// </summary>
    public class TrayService : IDisposable
    {
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _menu;
        private System.Drawing.Icon _icon;

        public void Initialize()
        {
            _icon = IconHelper.GetIcon();
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Text = "日程提醒";
            if (_icon != null)
            {
                _notifyIcon.Icon = _icon;
            }
            _notifyIcon.Visible = true;

            // 菜单项顺序与文案：创建日程 / 打开 / 分隔线 / 退出
            _menu = new ContextMenuStrip();
            _menu.Items.Add(CreateItem("创建日程", OnCreateClick));
            _menu.Items.Add(CreateItem("打开", OnOpenClick));
            _menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exitItem = CreateItem("退出", OnExitClick);
            exitItem.ForeColor = System.Drawing.Color.FromArgb(0xFF, 0x6B, 0x6B);
            _menu.Items.Add(exitItem);
            _notifyIcon.ContextMenuStrip = _menu;

            _notifyIcon.DoubleClick += OnOpenClick;
            _notifyIcon.MouseClick += OnMouseClick;
        }

        private static ToolStripMenuItem CreateItem(string text, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += handler;
            return item;
        }

        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            // 左键单击仅用于弹出菜单；双击由 DoubleClick 处理
        }

        private void OnOpenClick(object sender, EventArgs e)
        {
            if (Application.Current == null)
            {
                return;
            }
            Application.Current.Dispatcher.BeginInvoke(new Action(delegate
            {
                MainWindow main = AppServices.MainWindow;
                if (main == null)
                {
                    return;
                }
                main.ShowFromTray();
            }));
        }

        private void OnCreateClick(object sender, EventArgs e)
        {
            if (Application.Current == null)
            {
                return;
            }
            Application.Current.Dispatcher.BeginInvoke(new Action(delegate
            {
                CreateWindow window = new CreateWindow(null);
                window.Show();
            }));
        }

        // 设置窗由主窗打开并设置 Owner

        private void OnExitClick(object sender, EventArgs e)
        {
            if (Application.Current == null)
            {
                return;
            }
            // 托盘右键「退出」= 直接退出，不再弹「确定要退出吗」确认框（用户要求）
            Application.Current.Dispatcher.BeginInvoke(new Action(delegate
            {
                Application.Current.Shutdown();
            }));
        }

        public void Dispose()
        {
            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.DoubleClick -= OnOpenClick;
                    _notifyIcon.MouseClick -= OnMouseClick;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
                if (_menu != null)
                {
                    _menu.Dispose();
                    _menu = null;
                }
                if (_icon != null)
                {
                    _icon.Dispose();
                    _icon = null;
                }
            }
            catch (Exception ex)
            {
                LogWriter.Write("释放托盘图标失败", ex);
            }
        }
    }
}
