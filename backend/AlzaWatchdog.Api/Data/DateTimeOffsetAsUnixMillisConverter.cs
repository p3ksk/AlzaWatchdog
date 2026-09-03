using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AlzaWatchdog.Api.Data;

/// <summary>
/// Persists a DateTimeOffset as Unix milliseconds.
///
/// SQLite has no date type, and EF refuses to translate ORDER BY or range
/// comparisons over DateTimeOffset against it — which is exactly what the history
/// query and the "what is due for a check" query need. Storing an integer makes
/// both translate, sorts correctly, and indexes well. Values come back normalised
/// to UTC, which is all this application ever stores.
/// </summary>
public class DateTimeOffsetAsUnixMillisConverter : ValueConverter<DateTimeOffset, long>
{
    public DateTimeOffsetAsUnixMillisConverter() : base(
        v => v.ToUnixTimeMilliseconds(),
        v => DateTimeOffset.FromUnixTimeMilliseconds(v))
    {
    }
}
