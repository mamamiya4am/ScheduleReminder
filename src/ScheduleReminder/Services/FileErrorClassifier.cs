using System;
using System.IO;

namespace ScheduleReminder.Services
{
    public static class FileErrorClassifier
    {
        public static bool IsSharingViolation(IOException ex)
        {
            if (ex == null)
            {
                return false;
            }
            int code = ex.HResult & 0xFFFF;
            return code == 32 || code == 33;
        }
    }
}
