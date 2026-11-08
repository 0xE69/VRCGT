namespace VRCGroupTools.Services;

public record ScheduledPostDraft(string Key, string GroupId, string Title, string Body, string? ImageId, DateTime DueUtc,
    string Audience = "group", bool Notify = false, string? LocalImagePath = null, bool OmitReferencedArtwork = false);

public static class ScheduleTime
{
    // WPF date/time pickers produce wall-clock values; Kind.Local must not override the selected zone.
    public static bool TryUtc(DateTime wallTime, string zoneId, out DateTime utc)
    {
        utc = default;
        if (wallTime.Kind == DateTimeKind.Utc) { utc = wallTime; return true; }
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            var wall = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(wall) || zone.IsAmbiguousTime(wall)) return false;
            utc = TimeZoneInfo.ConvertTimeToUtc(wall, zone);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }
}
