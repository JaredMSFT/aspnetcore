// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Npgsql;

namespace Microsoft.Extensions.Caching.Benchmarks;

[MemoryDiagnoser, ShortRunJob]
public class DistributedCacheBenchmarks : IDisposable
{
    private readonly IBufferDistributedCache sqlServer, redis, postgres;
    private readonly ConnectionMultiplexer multiplexer;
    private readonly Random random = new Random();
    private readonly string[] keys;
    private readonly Task<byte[]?>[] pendingBlobs = new Task<byte[]?>[OperationsPerInvoke];

    // create a local DB named CacheBench, then
    // dotnet tool install --global dotnet-sql-cache
    // dotnet sql-cache create "Data Source=127.0.0.1;Initial Catalog=CacheBench;user=sa;password=Test.123!;Trust Server Certificate=True" dbo BenchmarkCache

    // dotnet tool install --global dotnet-postgres-cache
    // dotnet psotgres-cache create "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres" public BenchmarkCache false

    private const string SqlServerConnectionString = "Data Source=127.0.0.1;Initial Catalog=CacheBench;user=sa;password=Test.123!;Trust Server Certificate=True";
    private const string RedisConfigurationString = "127.0.0.1,AllowAdmin=true";
    private const string PostgresConnectionString = "Host=localhost;Port=6432;Username=postgres;Password=postgres;Database=postgres;Pooling=true;Timeout=0;Command Timeout=0;Maximum Pool Size=20000;";
    public const int OperationsPerInvoke = 256;

    public void Dispose()
    {
        (postgres as IDisposable)?.Dispose();
        (sqlServer as IDisposable)?.Dispose();
        (redis as IDisposable)?.Dispose();
        
        multiplexer.Dispose();
    }

    public enum BackendType
    {
        Postgres,
        Redis,
        SqlServer,
        
    }
    [Params(BackendType.Postgres, BackendType.Redis, BackendType.SqlServer)]
    public BackendType Backend { get; set; } = BackendType.Postgres;

    private IBufferDistributedCache _backend = null!;

    public DistributedCacheBenchmarks()
    {
        var services = new ServiceCollection();
        services.AddDistributedSqlServerCache(options =>
        {
            options.TableName = "BenchmarkCache";
            options.SchemaName = "dbo";
            options.ConnectionString = SqlServerConnectionString;
        });
        sqlServer = (IBufferDistributedCache)services.BuildServiceProvider().GetRequiredService<IDistributedCache>();

        multiplexer = ConnectionMultiplexer.Connect(RedisConfigurationString);
        services = new ServiceCollection();
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(multiplexer);
        });
        redis = (IBufferDistributedCache)services.BuildServiceProvider().GetRequiredService<IDistributedCache>();

        services = new ServiceCollection();
        services.AddDistributedPostgresCache(options =>
        {
            options.TableName = "BenchmarkCache";
            options.SchemaName = "public";
            options.ConnectionString = PostgresConnectionString;
        });
        postgres = (IBufferDistributedCache)services.BuildServiceProvider().GetRequiredService<IDistributedCache>();

        keys = new string[10000];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = Guid.NewGuid().ToString();
        }
    }

    [GlobalSetup]
    public void GlobalSetup()
    {
        // reset
        _backend = Backend switch
        {
            BackendType.Postgres => postgres,
            BackendType.Redis => redis,
            BackendType.SqlServer => sqlServer,            
            _ => throw new ArgumentOutOfRangeException(nameof(Backend)),
        };
        _backend.Get(new Guid().ToString()); // just to touch it first
        switch (Backend)
        {
            case BackendType.SqlServer:
                using (var conn = new SqlConnection(SqlServerConnectionString))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "truncate table dbo.BenchmarkCache";
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
                break;
            case BackendType.Redis:
                using (var multiplexer = ConnectionMultiplexer.Connect(RedisConfigurationString))
                {
                    multiplexer.GetServer(multiplexer.GetEndPoints().Single()).FlushDatabase();
                }
                break;
            case BackendType.Postgres:
                using (var conn = new NpgsqlConnection(PostgresConnectionString))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "TRUNCATE TABLE public.BenchmarkCache";
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
                break;

        }
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) };
        options.SlidingExpiration = Sliding ? TimeSpan.FromMinutes(5) : null;

        var value = new byte[PayloadSize];
        foreach (var key in keys)
        {
            random.NextBytes(value);
            _backend.Set(key, value, options);
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int GetSingleRandom()
    {
        int total = 0;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            total += _backend.Get(RandomKey())?.Length ?? 0;
        }
        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int GetConcurrentRandom()
    {
        Func<Task<byte[]?>> callback = () => _backend.GetAsync(RandomKey());
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            pendingBlobs[i] = Task.Run(callback);
        }
        int total = 0;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            total += (pendingBlobs[i].Result)?.Length ?? 0;
        }
        return total;

    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public async Task<int> GetSingleRandomAsync()
    {
        int total = 0;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            total += (await _backend.GetAsync(RandomKey()))?.Length ?? 0;
        }
        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public async Task<int> GetConcurrentRandomAsync()
    {
        Func<Task<byte[]?>> callback = () => _backend.GetAsync(RandomKey());
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            pendingBlobs[i] = Task.Run(callback);
        }
        int total = 0;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            total += (await pendingBlobs[i])?.Length ?? 0;
        }
        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int GetSingleFixed()
    {
        int total = 0;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            total += _backend.Get(FixedKey())?.Length ?? 0;
        }
        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int GetConcurrentFixed()
    {
        Func<Task<byte[]?>> callback = () => _backend.GetAsync(FixedKey());
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            pendingBlobs[i] = Task.Run(callback);
        }
        int total = 0;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            total += (pendingBlobs[i].Result)?.Length ?? 0;
        }
        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public async Task<int> GetSingleFixedAsync()
    {
        int total = 0;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            total += (await _backend.GetAsync(FixedKey()))?.Length ?? 0;
        }
        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public async Task<int> GetConcurrentFixedAsync()
    {
        Func<Task<byte[]?>> callback = () => _backend.GetAsync(FixedKey());
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            pendingBlobs[i] = Task.Run(callback);
        }
        int total = 0;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            total += (await pendingBlobs[i])?.Length ?? 0;
        }
        return total;
    }

    private string FixedKey() => keys[42];

    private string RandomKey() => keys[random.Next(keys.Length)];

    [Params(1024, 128, 10 * 1024)]
    public int PayloadSize { get; set; } = 1024;

    [Params(true, false)]
    public bool Sliding { get; set; } = true;
}
