using DataControls.Application.Abstractions;
using DataControls.Domain.Metadata;

namespace DataControls.Tests.Support;

/// <summary>Serves a fixed in-memory catalog (no database).</summary>
public sealed class StaticMetadataProvider(MetadataCatalog catalog) : IMetadataProvider
{
    public Task<MetadataCatalog> GetCatalogAsync(CancellationToken ct = default) => Task.FromResult(catalog);

    public void Invalidate() { }
}
