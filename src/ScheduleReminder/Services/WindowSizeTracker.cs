using System;
using System.Windows;
using System.Windows.Interop;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 识别"用户真正改变了窗口尺寸/位置"这一手势。
    ///
    /// 为什么需要它：WPF 的 SizeChanged / LocationChanged 分不清"谁改的尺寸"——
    /// 程序自己改（比如整体缩放写 Width）和用户拖拉都会触发，靠"时间窗"或"比对数值"去猜是不可靠的。
    /// 这里改用 Win32 的 WM_EXITSIZEMOVE：只有**用户拖动边框/拖动窗口、松开手时**才触发一次，
    /// 天然把程序造成的尺寸变化排除在外。
    /// </summary>
    public static class WindowSizeTracker
    {
        private const int WmExitSizeMove = 0x0232;

        /// <summary>注册回调：用户拖完窗口（尺寸或位置变化结束）时触发一次。</summary>
        public static void Track(Window window, Action onUserResizeFinished)
        {
            if (window == null || onUserResizeFinished == null)
            {
                return;
            }
            // 注意：窗口句柄要等 SourceInitialized（显示前一刻）才存在；
            // IsInitialized 在 InitializeComponent 后就已是 true，不能用它在构造阶段判断。
            window.SourceInitialized += delegate { Attach(window, onUserResizeFinished); };
        }

        private static void Attach(Window window, Action callback)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                // 句柄仍未创建：等下一次 SourceInitialized
                window.SourceInitialized += delegate { Attach(window, callback); };
                return;
            }
            HwndSource source = HwndSource.FromHwnd(handle);
            if (source == null)
            {
                return;
            }

            HwndSourceHook hook = delegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (msg == WmExitSizeMove)
                {
                    callback();
                }
                return IntPtr.Zero;
            };
            source.AddHook(hook);

            // 窗口关闭时摘掉钩子，避免闭包长期持有窗口
            window.Closed += delegate { source.RemoveHook(hook); };
        }
    }
}
