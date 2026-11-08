using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScheduleReminder.Services
{
    /// <summary>
    /// 托盘常驻场景的内存裁剪（内存方案 A）。
    ///
    /// 本程序是长期挂在托盘的日历工具，主窗隐藏后界面完全不可见，此时占着
    /// 上百 MB 物理内存没有意义。做法：隐藏到托盘时先做一次完整 GC，再调用
    /// Win32 SetProcessWorkingSetSize(-1,-1) 把进程的物理内存页裁剪掉
    /// （未使用的页换出到页面文件，任务管理器里的占用随之下降）。
    ///
    /// 说明：这只释放**物理内存**，进程的私有字节（提交内存）不变；
    /// 唤起窗口时系统会按需把页换回来，代价是唤起瞬间有几十~几百毫秒的延迟。
    /// </summary>
    public static class MemoryHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process,
                                                            IntPtr minimumWorkingSetSize,
                                                            IntPtr maximumWorkingSetSize);

        /// <summary>完整 GC + 裁剪物理工作集。仅在窗口隐藏到托盘时调用。</summary>
        public static void Trim()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch (Exception ex)
            {
                LogWriter.Write("裁剪内存失败", ex);
            }
        }
    }
}
