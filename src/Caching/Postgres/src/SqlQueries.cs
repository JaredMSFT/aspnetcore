// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace Microsoft.Extensions.Caching.Postgres;

internal sealed class SqlQueries
{

    private const string UpdateCacheItemFormat = """
        UPDATE {0}
        SET expiresAtTime = CASE WHEN EXTRACT(SECOND FROM AGE(absoluteExpiration, @utcNow)) <= slidingExpirationInSeconds THEN absoluteExpiration
        ELSE @utcNow + INTERVAL '1 second' * slidingExpirationInSeconds
        END
        WHERE id = @id
        AND expiresAtTime >= @utcNow
        AND slidingExpirationInSeconds IS NOT NULL
        AND (absoluteExpiration IS NULL OR absoluteExpiration <> expiresAtTime);
""";

    private const string GetCacheItemFormat =
        "SELECT value FROM {0} WHERE id = @id AND expiresAtTime >= @utcNow;";

    private const string SetCacheItemFormat = """
        WITH _PARAMS (slidingExpirationInSeconds, absoluteExpiration, utcNow) AS
        ( VALUES (@slidingExpirationInSeconds, @absoluteExpiration::timestamp, @utcNow::timestamp) )
        INSERT INTO {0} (id, value, expiresAtTime, slidingExpirationInSeconds, absoluteExpiration)
        SELECT @id, @value, CASE
            WHEN slidingExpirationInSeconds IS NULL THEN absoluteExpiration::timestamp
            ELSE utcNow + INTERVAL '1 second' * slidingExpirationInSeconds::bigint
        END, slidingExpirationInSeconds::bigint, absoluteExpiration FROM _PARAMS
        ON CONFLICT (id) DO UPDATE
        SET value = EXCLUDED.value,
        expiresAtTime = EXCLUDED.expiresAtTime,
        slidingExpirationInSeconds = EXCLUDED.slidingExpirationInSeconds,
        absoluteExpiration = EXCLUDED.absoluteExpiration;
""";

    private const string DeleteCacheItemFormat = "DELETE FROM {0} WHERE id = @id";

    public const string DeleteExpiredCacheItemsFormat = "DELETE FROM {0} WHERE expiresAtTime < @utcNow";

    public SqlQueries(string schemaName, string tableName)
    {
        var tableNameWithSchema = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1}", DelimitIdentifier(schemaName), DelimitIdentifier(tableName));

        // when retrieving an item, we do an UPDATE first and then a SELECT
        GetCacheItem = string.Format(CultureInfo.InvariantCulture, UpdateCacheItemFormat + GetCacheItemFormat, tableNameWithSchema);
        GetCacheItemWithoutValue = string.Format(CultureInfo.InvariantCulture, UpdateCacheItemFormat, tableNameWithSchema);
        DeleteCacheItem = string.Format(CultureInfo.InvariantCulture, DeleteCacheItemFormat, tableNameWithSchema);
        DeleteExpiredCacheItems = string.Format(CultureInfo.InvariantCulture, DeleteExpiredCacheItemsFormat, tableNameWithSchema);
        SetCacheItem = string.Format(CultureInfo.InvariantCulture, SetCacheItemFormat, tableNameWithSchema);
    }

    public string GetCacheItem { get; }

    public string GetCacheItemWithoutValue { get; }

    public string SetCacheItem { get; }

    public string DeleteCacheItem { get; }

    public string DeleteExpiredCacheItems { get; }

    // TODO Review From EF's PostgresQuerySqlGenerator
    private static string DelimitIdentifier(string identifier)
    {
        return identifier;
    }

}
