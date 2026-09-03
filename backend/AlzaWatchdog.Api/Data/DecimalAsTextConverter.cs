using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AlzaWatchdog.Api.Data;

/// <summary>
/// Persists a decimal as invariant-culture text. SQLite would otherwise store it
/// as a floating point REAL and silently round prices.
///
/// Caveat: because the stored form is text, SQL comparisons over these columns are
/// lexicographic — MIN would call "9.90" dearer than "18.90", and ORDER BY would
/// sort nonsensically. Never aggregate or sort on a price in a LINQ query that EF
/// translates to SQL; pull the rows and do it in memory, as ItemEndpoints does.
/// </summary>
public class DecimalAsTextConverter : ValueConverter<decimal, string>
{
    public DecimalAsTextConverter() : base(
        v => v.ToString(CultureInfo.InvariantCulture),
        v => decimal.Parse(v, NumberStyles.Number, CultureInfo.InvariantCulture))
    {
    }
}
