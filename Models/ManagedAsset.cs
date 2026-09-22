namespace EzPocket.Models;

/// <summary>Describes an asset found on a Pocket target without assuming which core consumes it.</summary>
public sealed record ManagedAsset(
    string Kind,
    string Name,
    string RelativePath,
    string Format,
    long SizeBytes,
    string Compatibility);
