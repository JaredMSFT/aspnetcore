// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;

namespace Microsoft.Extensions.Caching.SqlConfig.Tools;

internal sealed class PostgresSqlQueries
{
    private const string CreateTableFormat = "CREATE TABLE {0}(" +
        // Maximum size of primary key column is 900 bytes (898 bytes from key + 2 additional bytes used by the
        // Sql Server). In the case where the key is greater than 898 bytes, then it gets truncated.
        // - Add collation to the key column to make it case-sensitive
        "id varchar(449) COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL, " +
        "value varbinary(MAX) NOT NULL, " +
        "expiresAtTime timestamptz NOT NULL, " +
        "slidingExpirationInSeconds bigint NULL," +
        "absoluteExpiration timestamptz NULL, " +
        "PRIMARY KEY (id))";

    private const string CreateNonClusteredIndexOnExpirationTimeFormat
        = "CREATE NONCLUSTERED INDEX ix_expiresAtTime ON {0}(expiresAtTime)";

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
        return "[" + identifier.Replace("]", "]]") + "]";
    }

    private static string EscapeLiteral(string literal)
    {
        return literal.Replace("'", "''");
    }
}
