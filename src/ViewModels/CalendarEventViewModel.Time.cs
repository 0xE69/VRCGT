using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCGroupTools.Services;
namespace VRCGroupTools.ViewModels;
public partial class CalendarEventViewModel
{
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _endDate;
    [ObservableProperty] private string _startClock = "00:00";
    [ObservableProperty] private string _endClock = "00:00";
    private void SyncScheduleFields(CalendarEvent value)
    {
        DateTime Wall(DateTime date)
        {
            if (date.Kind != DateTimeKind.Utc) return date;
            try { return TimeZoneInfo.ConvertTimeFromUtc(date, TimeZoneInfo.FindSystemTimeZoneById(value.TimeZoneId)); }
            catch (TimeZoneNotFoundException) { return date; }
            catch (InvalidTimeZoneException) { return date; }
        }
        var start = Wall(value.StartTime); var end = Wall(value.EndTime);
        StartDate = start.Date; EndDate = end.Date;
        StartClock = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        EndClock = end.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
    private bool TryApplyScheduleFields()
    {
        if (!StartDate.HasValue || !EndDate.HasValue ||
            !DateTime.TryParseExact(StartClock, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !DateTime.TryParseExact(EndClock, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) return false;
        // Never parse a clock TextBox as a whole DateTime: that silently replaced the picked date with today.
        Draft.StartTime = DateTime.SpecifyKind(StartDate.Value.Date + start.TimeOfDay, DateTimeKind.Unspecified);
        Draft.EndTime = DateTime.SpecifyKind(EndDate.Value.Date + end.TimeOfDay, DateTimeKind.Unspecified);
        return true;
    }
}
