namespace Mockapala.Export.Sqlite.Tests.DomainModels;

public class SimpleAddress
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public class EntityWithAddress
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public SimpleAddress HomeAddress { get; set; } = new();
}
