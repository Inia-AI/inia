using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Infrastructure.Storage;

/// <summary>
/// Database storage service implementation that stores files in the Files table. A file streams
/// in and out: the insert sends the caller's stream as its parameter, and a read holds the row open
/// with sequential access until the reader disposes the stream, so no whole-file copy is made.
/// </summary>
public class DatabaseStorageService : IFileStorageService
{
    private const string ID_PARAMETER = "@id";
    private const string BYTES_PARAMETER = "@bytes";
    private const string ORGANIZATION_PARAMETER = "@organizationId";
    private const string STORED_AT_PARAMETER = "@storedAt";
    private const string IS_DELETED_PARAMETER = "@isDeleted";

    // The (max) size makes SqlClient stream the value instead of buffering it; Npgsql and SQLite
    // read the stream either way.
    private const int UNBOUNDED_SIZE = -1;

    private readonly IServiceScopeFactory _serviceScopeFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="DatabaseStorageService"/>.
    /// </summary>
    public DatabaseStorageService(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(string organizationId, string category, string key, Stream content, string contentType)
    {
        await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        // PostgreSQL and SQL Server read a stream parameter that can seek; SQLite, which runs the
        // tests, binds none, and a stream that cannot seek comes only from another store.
        object bytesValue = content.CanSeek && !context.Database.IsSqlite() ? content : await StoredObjectStream.ReadAllAsync(content);

        await context.Database.OpenConnectionAsync();
        try
        {
            await using DbCommand insert = context.Database.GetDbConnection().CreateCommand();
            // Raw SQL reviewed: the table and column names come from the EF model, quoted by the provider; every value enters as a parameter.
            insert.CommandText = InsertSql(context);
            DateTime storedAt = DateTime.UtcNow;
            AddParameter(insert, ID_PARAMETER, key);
            AddParameter(insert, ORGANIZATION_PARAMETER, organizationId);
            AddParameter(insert, STORED_AT_PARAMETER, storedAt);
            AddParameter(insert, IS_DELETED_PARAMETER, false);
            DbParameter bytes = AddParameter(insert, BYTES_PARAMETER, bytesValue);
            bytes.DbType = DbType.Binary;
            bytes.Size = UNBOUNDED_SIZE;

            _ = await insert.ExecuteNonQueryAsync();
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        return key;
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string organizationId, string category, string path)
    {
        // Path is the File ID in database storage
        var read = new OpenRowRead(_serviceScopeFactory.CreateAsyncScope());
        try
        {
            UserDataDbContext context = read.Scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            read.Command = context.Files
                .Where(file => file.Id == path)
                .Select(file => new { Length = (long)file.Bytes.Length, file.Bytes })
                .CreateDbCommand();

            await context.Database.OpenConnectionAsync();
            read.Context = context;
            read.Reader = await read.Command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleRow);
            if (!await read.Reader.ReadAsync())
            {
                throw new FileNotFoundException($"File with ID '{path}' not found.");
            }

            long length = Convert.ToInt64(read.Reader.GetValue(0));
            return new StoredObjectStream(read.Reader.GetStream(1), length, read);
        }
        catch
        {
            await read.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string organizationId, string category, string path)
    {
        // Path is the File ID in database storage. One set-based DELETE: loading the row first
        // would pull the whole blob into memory just to throw it away. A missing row deletes
        // zero rows, which is the wanted no-op. The organization predicate is defense in depth:
        // a key misrouted across tenants deletes nothing.
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = await context.Files
            .Where(file => file.Id == path && file.OrganizationId == organizationId)
            .ExecuteDeleteAsync();
    }

    private static DbParameter AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        _ = command.Parameters.Add(parameter);
        return parameter;
    }

    private static string InsertSql(UserDataDbContext context)
    {
        IEntityType files = context.Model.FindEntityType(typeof(File))!;
        StoreObjectIdentifier table = StoreObjectIdentifier.Table(files.GetTableName()!, files.GetSchema());
        ISqlGenerationHelper sql = context.GetService<ISqlGenerationHelper>();

        string Column(string property)
        {
            return sql.DelimitIdentifier(files.FindProperty(property)!.GetColumnName(table)!);
        }

        return $"INSERT INTO {sql.DelimitIdentifier(table.Name, table.Schema)} " +
            $"({Column(nameof(File.Id))}, {Column(nameof(File.Bytes))}, {Column(nameof(File.OrganizationId))}, " +
            $"{Column(nameof(File.CreatedAt))}, {Column(nameof(File.UpdatedAt))}, {Column(nameof(File.IsDeleted))}) " +
            $"VALUES ({ID_PARAMETER}, {BYTES_PARAMETER}, {ORGANIZATION_PARAMETER}, {STORED_AT_PARAMETER}, {STORED_AT_PARAMETER}, {IS_DELETED_PARAMETER})";
    }

    /// <summary>
    /// What holds a streamed read open: the scope, its context's open connection, the command and
    /// its reader. Released in the reverse order when the read's stream is disposed.
    /// </summary>
    private sealed class OpenRowRead(AsyncServiceScope scope) : IDisposable, IAsyncDisposable
    {
        public AsyncServiceScope Scope { get; } = scope;

        public UserDataDbContext? Context { get; set; }

        public DbCommand? Command { get; set; }

        public DbDataReader? Reader { get; set; }

        public void Dispose()
        {
            Reader?.Dispose();
            Command?.Dispose();
            Context?.Database.CloseConnection();
            Scope.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (Reader is not null)
            {
                await Reader.DisposeAsync();
            }

            if (Command is not null)
            {
                await Command.DisposeAsync();
            }

            if (Context is not null)
            {
                await Context.Database.CloseConnectionAsync();
            }

            await Scope.DisposeAsync();
        }
    }
}
