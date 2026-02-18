using Microsoft.Data.Sqlite;
using Mockapala.Result;
using Mockapala.Schema;
using System.Reflection;

namespace Mockapala.Export.SqlLite;

/// <summary>
/// Exports generated data to a SQLite database using parameterized INSERTs in a transaction.
/// </summary>
public sealed class SqliteExporter : ISchemaDataExporter
{
    private readonly SqliteExportOptions _options;

    public SqliteExporter(SqliteExportOptions? options = null)
    {
        this._options = options ?? new SqliteExportOptions();
    }

    /// <inheritdoc />
    public void Export(ISchema schema, IGeneratedData data, Stream output)
    {
        throw new NotSupportedException(
            "SQLite exporter writes to a database. Use ExportToDatabase(ISchema, IGeneratedData, string connectionString) instead.");
    }

    /// <summary>
    /// Exports generated data into a SQLite database using the given connection string.
    /// </summary>
    public void ExportToDatabase(ISchema schema, IGeneratedData data, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));

        using SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();
        this.ApplyPragmas(connection);

        using SqliteTransaction transaction = connection.BeginTransaction();

        foreach (Type entityType in schema.GenerationOrder)
        {
            IReadOnlyList<object> list = data.Get(entityType);
            if (list.Count == 0)
                continue;

            IEntityDefinition? definition = schema.Entities.FirstOrDefault(e => e.EntityType == entityType);
            IReadOnlyList<ExportableProperty> exportable = ExportableProperty.GetExportableProperties(entityType, definition);
            if (exportable.Count == 0)
                continue;

            string tableName = this._options.GetTableName(entityType);

            if (this._options.CreateTables)
                this.CreateTable(connection, tableName, exportable);

            this.InsertRows(connection, tableName, exportable, list);
        }

        transaction.Commit();
    }

    /// <summary>
    /// Convenience overload: exports generated data into a SQLite database file.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <param name="data">The generated data.</param>
    /// <param name="filePath">Path to the SQLite database file.</param>
    /// <param name="createIfMissing">When true (default), creates the file if it doesn't exist.</param>
    public void ExportToDatabase(ISchema schema, IGeneratedData data, string filePath, bool createIfMissing = true)
    {
        string mode = createIfMissing ? "ReadWriteCreate" : "ReadWrite";
        string connectionString = $"Data Source={filePath};Mode={mode}";
        this.ExportToDatabase(schema, data, connectionString);
    }

    private void ApplyPragmas(SqliteConnection connection)
    {
        if (this._options.UseWalMode)
        {
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode = WAL;";
            cmd.ExecuteNonQuery();
        }
    }

    private void CreateTable(SqliteConnection connection, string tableName, IReadOnlyList<ExportableProperty> properties)
    {
        string columns = string.Join(", ", properties.Select(p =>
            $"{this._options.QuoteColumn(p.Property.Name)} {GetSqliteColumnType(p.EffectiveType)}"));

        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {tableName} ({columns});";
        cmd.ExecuteNonQuery();
    }

    private void InsertRows(SqliteConnection connection, string tableName, IReadOnlyList<ExportableProperty> properties, IReadOnlyList<object> entities)
    {
        string columnNames = string.Join(", ", properties.Select(p => this._options.QuoteColumn(p.Property.Name)));
        string paramNames = string.Join(", ", properties.Select((_, i) => $"$p{i}"));

        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"INSERT INTO {tableName} ({columnNames}) VALUES ({paramNames});";

        // Pre-create parameters
        SqliteParameter[] parameters = new SqliteParameter[properties.Count];
        for (int i = 0; i < properties.Count; i++)
        {
            SqliteParameter param = new SqliteParameter($"$p{i}", GetSqliteParamType(properties[i].EffectiveType));
            cmd.Parameters.Add(param);
            parameters[i] = param;
        }

        cmd.Prepare();

        foreach (object entity in entities)
        {
            for (int i = 0; i < properties.Count; i++)
            {
                object? value = properties[i].GetValue(entity);
                parameters[i].Value = value ?? DBNull.Value;
            }
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Returns the public, readable+writable properties with scalar types for the given entity type.
    /// </summary>
    internal static IReadOnlyList<PropertyInfo> GetScalarProperties(Type entityType)
    {
        return entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && IsScalarType(p.PropertyType))
            .ToList();
    }

    private static bool IsScalarType(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive
            || t.IsEnum
            || t == typeof(string)
            || t == typeof(decimal)
            || t == typeof(DateTime)
            || t == typeof(DateTimeOffset)
            || t == typeof(TimeSpan)
            || t == typeof(Guid)
            || t == typeof(byte[]);
    }

    private static string GetSqliteColumnType(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;

        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
            || t == typeof(bool) || t.IsEnum)
            return "INTEGER";

        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal))
            return "REAL";

        if (t == typeof(byte[]))
            return "BLOB";

        // string, DateTime, DateTimeOffset, TimeSpan, Guid, and anything else -> TEXT
        return "TEXT";
    }

    private static SqliteType GetSqliteParamType(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;

        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
            || t == typeof(bool) || t.IsEnum)
            return SqliteType.Integer;

        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal))
            return SqliteType.Real;

        if (t == typeof(byte[]))
            return SqliteType.Blob;

        return SqliteType.Text;
    }
}
