namespace EprAzureStub;

/// <summary>
/// The years a PRN or PERN can be accepted into at a given moment, as the PRN common backend's
/// AcceptanceYearRule defines them: most notes only into the current compliance year; December
/// waste may also, or instead, be accepted into the following year.
/// </summary>
public static class PrnAcceptanceYearRule
{
    private const int DecemberWasteNoChoiceYear = 2025;
    private const int DecemberWasteNoChoiceUntilYear = 2026;
    private static readonly TimeZoneInfo UkZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    /// <summary>The compliance year runs February to January, judged on the UK calendar.</summary>
    public static int ComplianceYear(DateTimeOffset now)
    {
        var ukNow = TimeZoneInfo.ConvertTime(now, UkZone);

        return ukNow.Month == 1 ? ukNow.Year - 1 : ukNow.Year;
    }

    public static IReadOnlyList<int> AvailableYears(int obligationYear, bool decemberWaste, DateTimeOffset now)
    {
        if (obligationYear is < 1 or > 9998)
        {
            return [];
        }

        var thisComplianceYear = ComplianceYear(now);

        if (obligationYear > thisComplianceYear)
        {
            return [];
        }

        if (!decemberWaste)
        {
            return obligationYear == thisComplianceYear ? [obligationYear] : [];
        }

        if (obligationYear == DecemberWasteNoChoiceYear && thisComplianceYear <= DecemberWasteNoChoiceUntilYear)
        {
            return [thisComplianceYear];
        }

        var windowStart = new DateTimeOffset(obligationYear, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = new DateTimeOffset(obligationYear + 1, 2, 1, 0, 0, 0, TimeSpan.Zero);

        if (now >= windowStart && now < windowEnd)
        {
            return [obligationYear, obligationYear + 1];
        }

        if (obligationYear == thisComplianceYear)
        {
            return [obligationYear];
        }

        return obligationYear + 1 == thisComplianceYear ? [thisComplianceYear] : [];
    }

    /// <summary>Whether a note can be accepted into <paramref name="year"/> now.</summary>
    public static bool IsAcceptableInto(int year, int obligationYear, bool decemberWaste, DateTimeOffset now) =>
        AvailableYears(obligationYear, decemberWaste, now).Contains(year);
}
