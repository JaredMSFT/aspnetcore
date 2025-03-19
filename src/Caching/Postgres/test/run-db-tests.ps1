param(
    [Parameter(Mandatory = $false)][string]$ConnectionString = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres",
    [Parameter(Mandatory = $false)][string]$SchemaName = "public",
    [Parameter(Mandatory = $false)][string]$TableName = "CacheTest")

function ExecuteScalar($Connection, $Script) {
    $cmd = New-Object Npgsql.NpgsqlCommand
    $cmd.Connection = $Connection
    $cmd.CommandText = $Script
    $cmd.ExecuteScalar()
}

Add-Type -AssemblyName Npgsql

# Check if the database exists
Write-Host "Checking for database..."
$ServerConnectionBuilder = New-Object Npgsql.NpgsqlConnectionStringBuilder $ConnectionString

if (!$ServerConnectionBuilder.Database) {
    throw "A 'Database' value must be provided in the connection string!"
}
$DatabaseName = $ServerConnectionBuilder.Database

if ($ServerConnectionBuilder.Host -ne "localhost" -and $ServerConnectionBuilder.Host -ne "127.0.0.1") {
    Write-Warning "This script is really only designed for running against your local instance of Postgres DB. Continue at your own risk!"
}

$ServerConnection = New-Object Npgsql.NpgsqlConnection $ServerConnectionBuilder.ConnectionString
$ServerConnection.Open();

# Yes, this is SQL Injectable, but you're using it on your local machine with the intent of connecting to your local db.
$dbid = ExecuteScalar $ServerConnection "SELECT oid FROM pg_database WHERE datname = '$DatabaseName'"
if (!$dbid) {
    Write-Host "Database not found, creating..."

    # Create the database
    ExecuteScalar $ServerConnection "CREATE DATABASE $DatabaseName"
}

# Close the server connection
$ServerConnection.Close()

# Check for the table
$DbConnection = New-Object Npgsql.NpgsqlConnection $ConnectionString
$DbConnection.Open();
$tableid = ExecuteScalar $DbConnection "SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '$TableName'"
if ($tableid) {
    Write-Host "Table exists, dropping it..."
    ExecuteScalar $DbConnection "DROP TABLE $TableName"
}

$DbConnection.Close()

# Fill the database with sql cache goodies
dotnet sql-cache create $ConnectionString $SchemaName $TableName

# Set environment variables and launch tests
$oldConnectionString = $env:SQLCACHETESTS_ConnectionString
$oldSchemaName = $env:SQLCACHETESTS_SchemaName
$oldTableName = $env:SQLCACHETESTS_TableName
$oldEnabled = $env:SQLCACHETESTS_ENABLED
try {
    $env:SQLCACHETESTS_ConnectionString = $ConnectionString
    $env:SQLCACHETESTS_SchemaName = $SchemaName
    $env:SQLCACHETESTS_TableName = $TableName
    $env:SQLCACHETESTS_ENABLED = "1"

    Write-Host "Launching Tests..."
    dotnet test "$PSScriptRoot/Microsoft.Extensions.Caching.Postgres.Tests.csproj"
}
finally {
    if ($oldConnectionString) {
        $env:SQLCACHETESTS_ConnectionString = $oldConnectionString
    }
    else {
        Remove-Item env:\SQLCACHETESTS_ConnectionString
    }

    if ($oldSchemaName) {
        $env:SQLCACHETESTS_SchemaName = $oldSchemaName
    }
    else {
        Remove-Item env:\SQLCACHETESTS_SchemaName
    }

    if ($oldTableName) {
        $env:SQLCACHETESTS_TableName = $oldTableName
    }
    else {
        Remove-Item env:\SQLCACHETESTS_TableName
    }

    if ($oldEnabled) {
        $env:SQLCACHETESTS_ENABLED = $oldEnabled
    }
    else {
        Remove-Item env:\SQLCACHETESTS_ENABLED
    }
}
