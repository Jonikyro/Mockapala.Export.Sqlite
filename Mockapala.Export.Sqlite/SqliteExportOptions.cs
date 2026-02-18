using Mockapala.Schema;

namespace Mockapala.Export.SqlLite;

/// <summary>
/// Options for SQLite export.
/// </summary>
public sealed class SqliteExportOptions
{
    /// <summary>
    /// Resolves the table name for an entity type. Default: type name (e.g. Company -> "Company").
    /// </summary>
    public Func<Type, string>? TableNameResolver { get; set; }

    /// <summary>
    /// Quote identifiers with double-quotes (SQLite standard). Default: true.
    /// </summary>
    public bool QuoteIdentifiers { get; set; } = true;

    /// <summary>
    /// Emit CREATE TABLE IF NOT EXISTS before inserts. Default: true.
    /// </summary>
    public bool CreateTables { get; set; } = true;

    /// <summary>
    /// Set PRAGMA journal_mode = WAL on connection open for better write performance. Default: true.
    /// </summary>
    public bool UseWalMode { get; set; } = true;

    internal string GetTableName(Type entityType, IEntityDefinition? definition = null)
    {
        string name = this.TableNameResolver != null
            ? this.TableNameResolver(entityType)
            : definition?.TableName ?? entityType.Name;
        return this.QuoteIdentifiers ? $"\"{name}\"" : name;
    }

    internal string QuoteColumn(string name) => this.QuoteIdentifiers ? $"\"{name}\"" : name;
}
