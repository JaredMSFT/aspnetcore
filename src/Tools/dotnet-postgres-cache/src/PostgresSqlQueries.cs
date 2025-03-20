// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;

namespace Microsoft.Extensions.Caching.SqlConfig.Tools;

internal sealed class PostgresSqlQueries
{
    private const string CreateTableFormat = "CREATE TABLE {0}(" +
        // Unsure whether there is a maximum size of primary key varchar column in Postgres, but will follow existing convention.
        // In the case where the key is greater than 898 bytes, then it gets truncated.
        // - Add collation to the key column to make it case-sensitive
        "id VARCHAR(449) COLLATE \"C\" NOT NULL, " +
        "value BYTEA NOT NULL, " +
        "expiresAtTime TIMESTAMPTZ NOT NULL, " +
        "slidingExpirationInSeconds BIGINT NULL," +
        "absoluteExpiration TIMESTAMPTZ NULL, " +
        "PRIMARY KEY (id))";

    private const string CreateNonClusteredIndexOnExpirationTimeFormat
        = "CREATE INDEX ix_expiresAtTime ON {0}(expiresAtTime)";

    private const string TableInfoFormat =
         "SELECT table_catalog, table_schema, table_name, table_type " +
         "FROM information_schema.tables " +
         "WHERE table_schema = '{0}' " +
         "AND table_name = '{1}'";

    public PostgresSqlQueries(string schemaName, string tableName)
    {
        ArgumentException.ThrowIfNullOrEmpty(schemaName);
        ArgumentException.ThrowIfNullOrEmpty(tableName);

        var tableNameWithSchema = string.Format(
            CultureInfo.InvariantCulture, "{0}.{1}", DelimitIdentifier(schemaName), DelimitIdentifier(tableName));
        CreateTable = string.Format(CultureInfo.InvariantCulture, CreateTableFormat, tableNameWithSchema);
        CreateNonClusteredIndexOnExpirationTime = string.Format(
            CultureInfo.InvariantCulture,
            CreateNonClusteredIndexOnExpirationTimeFormat,
            tableNameWithSchema);
        TableInfo = string.Format(CultureInfo.InvariantCulture, TableInfoFormat, EscapeLiteral(schemaName), EscapeLiteral(tableName));
    }

    public string CreateTable { get; }

    public string CreateNonClusteredIndexOnExpirationTime { get; }

    public string TableInfo { get; }

    // From EF's SqlServerQuerySqlGenerator
    private static string DelimitIdentifier(string identifier)
    {
        return identifier;
    }

    private static string EscapeLiteral(string literal)
    {
        return literal.Replace("'", "''");
    }
}
