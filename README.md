<img align="left" src="https://raw.githubusercontent.com/Jonikyro/Mockapala/main/Images/logo_300x300.png" alt="Mockapala logo" width="90" />

# Mockapala.Export.Sqlite

SQLite exporter for [Mockapala](https://github.com/Jonikyro/Mockapala). Exports generated test data into a SQLite database using parameterized INSERTs in a single transaction for fast, reliable seeding.

## Usage

### Export to a database file

```csharp
using Mockapala.Schema;
using Mockapala.Generation;
using Mockapala.Export.Sqlite;

// 1. Define schema
var schema = SchemaCreate.Create()
    .Entity<Company>(e => e.Key(c => c.Id))
    .Entity<Customer>(e =>
    {
        e.Key(c => c.Id);
        e.Relation<Company>(c => c.CompanyId);
    })
    .Entity<Order>(e =>
    {
        e.Key(o => o.Id);
        e.Relation<Customer>(o => o.CustomerId);
    })
    .Build();

// 2. Generate data
var generator = new DataGenerator();
var data = generator.Generate(schema, cfg => cfg
    .Count<Company>(5)
    .Count<Customer>(50)
    .Count<Order>(200)
    .Seed(42));

// 3. Export to SQLite file
var exporter = new SqliteExporter();
exporter.ExportToDatabase(schema, data, "testdata.db", createIfMissing: true);
```

### Export with a connection string

```csharp
exporter.ExportToDatabase(schema, data, "Data Source=testdata.db;Mode=ReadWriteCreate");
```

## Options

Configure the exporter via `SqliteExportOptions`:

```csharp
var exporter = new SqliteExporter(new SqliteExportOptions
{
    CreateTables = true,                       // Emit CREATE TABLE IF NOT EXISTS — default: true
    UseWalMode = true,                         // Set PRAGMA journal_mode = WAL — default: true
    QuoteIdentifiers = true,                   // Wrap identifiers in double-quotes — default: true
    TableNameResolver = t => $"tbl_{t.Name}",  // Custom table name mapping — default: type name
});
```

| Option | Default | Description |
|--------|---------|-------------|
| `CreateTables` | `true` | Emits `CREATE TABLE IF NOT EXISTS` before inserting rows. Set to `false` if tables already exist. |
| `UseWalMode` | `true` | Sets `PRAGMA journal_mode = WAL` on connection open for better write performance. |
| `QuoteIdentifiers` | `true` | Wraps table and column names in `"double quotes"`. |
| `TableNameResolver` | Type name | `Func<Type, string>` that maps an entity type to its destination table name. |

### Table and Column Names from Schema

The exporter respects `ToTable` and `HasColumnName` metadata defined in the Mockapala schema:

```csharp
var schema = SchemaCreate.Create()
    .Entity<Order>(e =>
    {
        e.Key(o => o.Id);
        e.ToTable("orders");
        e.Property(o => o.CustomerId).HasColumnName("customer_id");
    })
    .Build();
```

Priority: `TableNameResolver` (exporter option) > `ToTable` (schema) > type name.

## Property Conversions

The exporter respects property conversions defined in the schema. This lets you store non-scalar types or transform values at export time:

```csharp
.Entity<Order>(e =>
{
    e.Key(o => o.Id);
    e.Property(o => o.Status).HasConversion(s => s.ToString()); // enum → TEXT
})
```

## Type Mapping

| .NET Type | SQLite Type |
|-----------|-------------|
| `int`, `long`, `short`, `byte`, `bool`, enums | `INTEGER` |
| `float`, `double`, `decimal` | `REAL` |
| `byte[]` | `BLOB` |
| `string`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, others | `TEXT` |