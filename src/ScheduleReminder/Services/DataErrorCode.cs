namespace ScheduleReminder.Services
{
    public static class DataErrorCode
    {
        public const string ReadOccupied = "DATA-IO-001";
        public const string BackupUnavailable = "DATA-BAK-001";
        public const string RecoveryReplaced = "DATA-REC-001";
        public const string RecoveryFailed = "DATA-REC-002";
        public const string SaveBlocked = "DATA-SAVE-001";
        public const string SaveOccupied = "DATA-SAVE-002";
        public const string SaveReadOnly = "DATA-SAVE-003";
        public const string LiteReadOccupied = "DATA-LITE-001";
        public const string LiteRecovered = "DATA-LITE-002";
        public const string LiteBackupUnavailable = "DATA-LITE-003";
        public const string LiteRecoveryFailed = "DATA-LITE-004";
    }
}
