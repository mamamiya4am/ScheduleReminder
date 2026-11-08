namespace ScheduleReminder.Services
{
    public sealed class DataError
    {
        public DataError(string code, string title, string userMessage)
        {
            Code = code;
            Title = title;
            UserMessage = userMessage;
        }

        public string Code { get; private set; }

        public string Title { get; private set; }

        public string UserMessage { get; private set; }
    }

    public sealed class ErrorContext
    {
        public string Operation { get; set; }

        public string FilePath { get; set; }

        public string BackupPath { get; set; }

        public string Decision { get; set; }

        public int? RestoredCount { get; set; }

        public bool Result { get; set; }

        public string AppVersion { get; set; }
    }
}
