using System;
using System.Windows;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>统一弹窗入口（确认框 / 提示框 / 只读告警框）。</summary>
    public static class DialogService
    {
        /// <summary>二次确认；返回 true 表示用户点了确认按钮。</summary>
        public static bool Confirm(string title, string message, string okText, bool danger)
        {
            return Views.MessageDialog.ShowConfirm(title, message, okText, danger);
        }

        /// <summary>普通提示（仅一个「确定」按钮）。</summary>
        public static void Alert(string title, string message)
        {
            Views.MessageDialog.ShowAlert(title, message);
        }

        /// <summary>
        /// 写盘失败时的置顶模态告警：不可最小化、关闭前阻塞用户对程序的任何其他操作。
        /// </summary>
        public static void ShowReadOnlyError()
        {
            Views.MessageDialog.ShowReadOnlyError();
        }

        public static void ShowSaveFailure(SaveOutcome outcome, string fileName)
        {
            if (outcome == SaveOutcome.Blocked)
            {
                Alert("无法保存",
                    "当前因数据问题保存功能暂时关闭，请排除问题后点击「刷新」恢复功能。\r\n\r\n"
                    + "错误代码：" + DataErrorCode.SaveBlocked);
            }
            else if (outcome == SaveOutcome.Occupied)
            {
                Alert("保存失败",
                    "保存失败！\r\n"
                    + fileName + " 当前是只读状态，文件或被占用，请解除占用后重试。\r\n\r\n"
                    + "错误代码：" + DataErrorCode.SaveOccupied);
            }
            else if (outcome == SaveOutcome.ReadOnlyOrIoError)
            {
                ShowReadOnlyError();
            }
        }

        /// <summary>在后台线程上需要弹窗时使用。</summary>
        public static void AlertOnUiThread(string title, string message)
        {
            Application app = Application.Current;
            if (app == null)
            {
                return;
            }
            app.Dispatcher.BeginInvoke(new Action(delegate { Alert(title, message); }));
        }
    }
}
