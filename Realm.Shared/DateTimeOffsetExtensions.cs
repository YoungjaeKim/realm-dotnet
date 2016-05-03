﻿using System;
using System.ComponentModel;

// Implement equivalents of extensions only available in .NET 4.6
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class DateTimeOffsetExtensions
{
    private static readonly DateTimeOffset UnixEpoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static long ToRealmUnixTimeMilliseconds(this DateTimeOffset @this)
    {
        return Convert.ToInt64((@this.ToUniversalTime() - UnixEpoch).TotalMilliseconds);
    }

    internal static DateTimeOffset FromRealmUnixTimeMilliseconds( Int64 ms)
    {
        return UnixEpoch.AddMilliseconds(ms);
    }
}

