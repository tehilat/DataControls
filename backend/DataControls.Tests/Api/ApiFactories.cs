using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataControls.Application.Abstractions;
using DataControls.Domain.Metadata;
using DataControls.Infrastructure.Metadata;
using DataControls.Tests.Integration;
using DataControls.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DataControls.Tests.Api;

/// <summary>The real API against the local database (integration tests).</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DroneOps", IntegrationDb.ConnectionString);
    }
}

/// <summary>
/// The real API with the in-memory <see cref="TestCatalog"/> instead of meta.* — metadata, validation and
/// error-format tests run without a database.
/// </summary>
public class CatalogOnlyApiFactory : ApiFactory
{
    protected virtual MetadataCatalog Catalog => TestCatalog.Create();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // no database at all: anything that tries to reach one fails fast
        builder.UseSetting("ConnectionStrings:DroneOps",
            "Server=127.0.0.1,1;Database=None;Connect Timeout=1;Trusted_Connection=True;TrustServerCertificate=True;");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMetadataProvider>();
            services.AddSingleton<IMetadataProvider>(new StaticMetadataProvider(Catalog));
        });
    }
}

/// <summary>Test catalog plus relations that form cycles (Hub → Drone → Hub, Drone → Drone).</summary>
public sealed class CyclicCatalogApiFactory : CatalogOnlyApiFactory
{
    protected override MetadataCatalog Catalog => MetadataCatalogFactory.Create(TestCatalog.Rows(extraRelations:
    [
        TestCatalog.R("Hub", "AnyDrone", "Drone", "HubId", "HubId"),
        TestCatalog.R("Drone", "Twin", "Drone", "DroneId", "DroneId")
    ]), ["ops"], out _);
}

public static class Http
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, string json) =>
        client.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, string json) =>
        client.PutAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    public static Task<T?> ReadAsync<T>(this HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<T>(Json);

    /// <summary>Messages of a validation problem in order: (path, message).</summary>
    public static async Task<List<(string Path, string Message)>> ValidationErrorsAsync(this HttpResponseMessage response)
    {
        var body = await response.JsonAsync();
        return body.GetProperty("validationErrors").EnumerateArray()
            .Select(e => (e.GetProperty("path").GetString()!, e.GetProperty("message").GetString()!))
            .ToList();
    }
}
