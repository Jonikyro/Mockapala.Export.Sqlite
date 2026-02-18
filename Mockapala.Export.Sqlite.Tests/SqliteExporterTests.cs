using Microsoft.Data.Sqlite;
using Mockapala.Export.Sqlite.Tests.DomainModels;
using Mockapala.Generation;
using Mockapala.Result;
using Mockapala.Schema;
using Xunit;

namespace Mockapala.Export.Sqlite.Tests;

public class SqliteExporterTests
{
    [Fact]
    public void Export_ThrowsNotSupportedException_WithMessageToUseExportToDatabase()
    {
        ISchema schema = SchemaCreate.Create()
            .Entity<Company>(e => e.Key(c => c.Id))
            .Build();
        DataGenerator generator = new DataGenerator();
        IGeneratedData result = generator.Generate(schema, cfg => cfg.Count<Company>(1).Seed(1));

        var exporter = new SqliteExporter();
        NotSupportedException ex = Assert.Throws<NotSupportedException>(() =>
            exporter.Export(schema, result, new MemoryStream()));
        Assert.Contains("ExportToDatabase", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportToDatabase_CreatesTablesAndInsertsRows()
    {
        ISchema schema = SchemaCreate.Create()
            .Entity<Company>(e => e.Key(c => c.Id))
            .Entity<Product>(e => e.Key(p => p.Id))
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

        DataGenerator generator = new DataGenerator();
        IGeneratedData result = generator.Generate(schema, cfg => cfg
            .Count<Company>(3)
            .Count<Product>(5)
            .Count<Customer>(10)
            .Count<Order>(20)
            .Seed(42));

        using SqliteConnection connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        // Export using the open connection's connection string won't work for in-memory
        // because a new connection = new empty database. Instead, export directly.
        var exporter = new SqliteExporter();
        // We need to use the same connection. Let's use a shared in-memory DB.
        string connStr = "Data Source=InMemoryTest;Mode=Memory;Cache=Shared";

        using SqliteConnection keepAlive = new SqliteConnection(connStr);
        keepAlive.Open(); // keep the in-memory DB alive

        exporter.ExportToDatabase(schema, result, connStr);

        AssertRowCount(keepAlive, "Company", 3);
        AssertRowCount(keepAlive, "Product", 5);
        AssertRowCount(keepAlive, "Customer", 10);
        AssertRowCount(keepAlive, "Order", 20);

        // Verify tables were created with correct columns
        AssertColumnExists(keepAlive, "Company", "Id");
        AssertColumnExists(keepAlive, "Company", "Name");
        AssertColumnExists(keepAlive, "Product", "Price");
        AssertColumnExists(keepAlive, "Product", "IsActive");
    }

    [Fact]
    public void ExportToDatabase_FilePath_CreatesDatabase()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"mockapala_test_{Guid.NewGuid():N}.db");
        try
        {
            ISchema schema = SchemaCreate.Create()
                .Entity<Company>(e => e.Key(c => c.Id))
                .Entity<Product>(e => e.Key(p => p.Id))
                .Build();

            DataGenerator generator = new DataGenerator();
            IGeneratedData result = generator.Generate(schema, cfg => cfg
                .Count<Company>(2)
                .Count<Product>(3)
                .Seed(42));

            var exporter = new SqliteExporter();
            exporter.ExportToDatabase(schema, result, filePath, createIfMissing: true);

            Assert.True(File.Exists(filePath), "SQLite database file should have been created.");

            using (SqliteConnection connection = new SqliteConnection($"Data Source={filePath}"))
            {
                connection.Open();
                AssertRowCount(connection, "Company", 2);
                AssertRowCount(connection, "Product", 3);
            }
        }
        finally
        {
            // Clear connection pool to release file locks before deleting
            SqliteConnection.ClearAllPools();
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void ExportToDatabase_WithCreateTablesFalse_SkipsTableCreation()
    {
        string connStr = "Data Source=NoCreateTest;Mode=Memory;Cache=Shared";
        using SqliteConnection keepAlive = new SqliteConnection(connStr);
        keepAlive.Open();

        // Pre-create the table
        using (SqliteCommand cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE \"Company\" (\"Id\" INTEGER, \"Name\" TEXT);";
            cmd.ExecuteNonQuery();
        }

        ISchema schema = SchemaCreate.Create()
            .Entity<Company>(e => e.Key(c => c.Id))
            .Build();

        DataGenerator generator = new DataGenerator();
        IGeneratedData result = generator.Generate(schema, cfg => cfg.Count<Company>(3).Seed(42));

        var exporter = new SqliteExporter(new SqliteExportOptions { CreateTables = false });
        exporter.ExportToDatabase(schema, result, connStr);

        AssertRowCount(keepAlive, "Company", 3);
    }

    [Fact]
    public void ExportToDatabase_WithCustomTableNameResolver()
    {
        string connStr = "Data Source=CustomNameTest;Mode=Memory;Cache=Shared";
        using SqliteConnection keepAlive = new SqliteConnection(connStr);
        keepAlive.Open();

        ISchema schema = SchemaCreate.Create()
            .Entity<Company>(e => e.Key(c => c.Id))
            .Build();

        DataGenerator generator = new DataGenerator();
        IGeneratedData result = generator.Generate(schema, cfg => cfg.Count<Company>(2).Seed(42));

        var exporter = new SqliteExporter(new SqliteExportOptions
        {
            TableNameResolver = t => $"tbl_{t.Name}"
        });
        exporter.ExportToDatabase(schema, result, connStr);

        AssertRowCount(keepAlive, "tbl_Company", 2);
    }

    private static void AssertRowCount(SqliteConnection connection, string tableName, int expectedCount)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\";";
        int count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(expectedCount, count);
    }

    private static void AssertColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        using SqliteDataReader reader = cmd.ExecuteReader();
        List<string> columns = new List<string>();
        while (reader.Read())
            columns.Add(reader.GetString(1)); // column 1 is "name"
        Assert.Contains(columnName, columns);
    }

    [Fact]
    public void ExportToDatabase_WithConversion_AppliesConverter()
    {
        ISchema schema = SchemaCreate.Create()
            .Entity<ConvertibleEntity>(e =>
            {
                e.Key(c => c.Id);
                e.Property(c => c.Status).HasConversion(s => s.ToString());
            })
            .Build();

        DataGenerator generator = new DataGenerator();
        IGeneratedData data = generator.Generate(schema, cfg => cfg.Count<ConvertibleEntity>(3).Seed(42));

        string connStr = "Data Source=ConversionTest;Mode=Memory;Cache=Shared";
        using SqliteConnection keepAlive = new SqliteConnection(connStr);
        keepAlive.Open();

        var exporter = new SqliteExporter();
        exporter.ExportToDatabase(schema, data, connStr);

        // Verify the Status column exists and contains string values
        AssertColumnExists(keepAlive, "ConvertibleEntity", "Status");

        using SqliteCommand cmd = keepAlive.CreateCommand();
        cmd.CommandText = "SELECT \"Status\" FROM \"ConvertibleEntity\";";
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string value = reader.GetString(0);
            Assert.Contains(value, new[] { "Pending", "Active", "Closed" });
        }
    }

    [Fact]
    public void ExportToDatabase_WithConversion_NonScalarBecomesExportable()
    {
        ISchema schema = SchemaCreate.Create()
            .Entity<EntityWithAddress>(e =>
            {
                e.Key(c => c.Id);
                e.Property(c => c.HomeAddress).HasConversion(a => $"{a.Street}, {a.City}");
            })
            .Build();

        DataGenerator generator = new DataGenerator();
        IGeneratedData data = generator.Generate(schema, cfg => cfg.Count<EntityWithAddress>(2).Seed(42));

        // Set addresses on the generated entities (since Bogus won't fill complex types)
        IReadOnlyList<EntityWithAddress> entities = data.Get<EntityWithAddress>();
        entities[0].HomeAddress = new SimpleAddress { Street = "123 Main", City = "Springfield" };
        entities[1].HomeAddress = new SimpleAddress { Street = "456 Oak", City = "Shelbyville" };

        string connStr = "Data Source=NonScalarConvTest;Mode=Memory;Cache=Shared";
        using SqliteConnection keepAlive = new SqliteConnection(connStr);
        keepAlive.Open();

        var exporter = new SqliteExporter();
        exporter.ExportToDatabase(schema, data, connStr);

        AssertColumnExists(keepAlive, "EntityWithAddress", "HomeAddress");
        AssertRowCount(keepAlive, "EntityWithAddress", 2);

        using SqliteCommand cmd = keepAlive.CreateCommand();
        cmd.CommandText = "SELECT \"HomeAddress\" FROM \"EntityWithAddress\" ORDER BY \"Id\";";
        using SqliteDataReader reader = cmd.ExecuteReader();
        reader.Read();
        Assert.Equal("123 Main, Springfield", reader.GetString(0));
        reader.Read();
        Assert.Equal("456 Oak, Shelbyville", reader.GetString(0));
    }

    [Fact]
    public void ExportToDatabase_ScalarConversion_OverridesValue()
    {
        ISchema schema = SchemaCreate.Create()
            .Entity<Product>(e =>
            {
                e.Key(p => p.Id);
                e.Property(p => p.Price).HasConversion(p => (int)(p * 100));
            })
            .Build();

        DataGenerator generator = new DataGenerator();
        IGeneratedData data = generator.Generate(schema, cfg => cfg.Count<Product>(1).Seed(42));

        Product product = data.Get<Product>()[0];
        product.Price = 19.99m;

        string connStr = "Data Source=ScalarConvTest;Mode=Memory;Cache=Shared";
        using SqliteConnection keepAlive = new SqliteConnection(connStr);
        keepAlive.Open();

        var exporter = new SqliteExporter();
        exporter.ExportToDatabase(schema, data, connStr);

        using SqliteCommand cmd = keepAlive.CreateCommand();
        cmd.CommandText = "SELECT \"Price\" FROM \"Product\";";
        int value = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1999, value);
    }
}
