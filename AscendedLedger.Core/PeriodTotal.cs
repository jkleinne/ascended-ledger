using System;

namespace AscendedLedger;

/// <summary>Net gil earned in one period, identified by the period's first local day.</summary>
public sealed record PeriodTotal(DateOnly PeriodStart, long NetGil);
