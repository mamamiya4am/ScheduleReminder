namespace ScheduleReminder.Services
{
    public enum SaveOutcome
    {
        Ok,
        Blocked,
        Occupied,
        ReadOnlyOrIoError
    }
}
