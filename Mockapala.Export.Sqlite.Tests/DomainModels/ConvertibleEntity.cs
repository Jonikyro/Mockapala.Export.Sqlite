namespace Mockapala.Export.Sqlite.Tests.DomainModels;

public enum EntityStatus { Pending, Active, Closed }

public class ConvertibleEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public EntityStatus Status { get; set; }
}
