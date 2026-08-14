namespace IceCrow.Hearthstone.Data;

public sealed record HearthstoneDataVersion(
    int SchemaVersion,
    string DataVersion,
    string? HearthstoneBuild,
    string Sha256,
    DateTimeOffset CreatedAt);
