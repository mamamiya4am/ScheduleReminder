using System.Diagnostics;

namespace ScheduleReminder.Services
{
    /// <summary>UI 点击闸门：只挡手抖连点，不做单实例、不改按钮状态。</summary>
    public static class ClickGuard
    {
        private static readonly long IntervalTicks = 4 * Stopwatch.Frequency / 10;
        private static long _createLast;
        private static long _refreshLast;

        public static bool TryPassCreate()
        {
            return TryPass(ref _createLast);
        }

        public static bool TryPassRefresh()
        {
            return TryPass(ref _refreshLast);
        }

        private static bool TryPass(ref long last)
        {
            long now = Stopwatch.GetTimestamp();
            if (now - last < IntervalTicks)
            {
                return false;
            }
            last = now;
            return true;
        }
    }
}
