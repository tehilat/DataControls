using DataControls.Application.Abstractions;
using DataControls.Application.Ai;
using DataControls.Application.Execution;
using DataControls.Infrastructure.Ai;
using DataControls.Infrastructure.Execution;
using DataControls.Infrastructure.Metadata;
using DataControls.Infrastructure.Persistence;
using DataControls.Infrastructure.Querying;
using DataControls.Infrastructure.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DataControls.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DroneOps";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

        services.AddSingleton<ISqlConnectionFactory>(new SqlConnectionFactory(connectionString));
        services.AddMemoryCache();

        services.AddOptions<ControlExecutionOptions>()
            .Bind(configuration.GetSection(ControlExecutionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<MetadataOptions>()
            .Bind(configuration.GetSection(MetadataOptions.SectionName));

        services.AddSingleton<IMetadataProvider, SqlMetadataProvider>();
        services.AddSingleton<IQueryBuilder, SqlQueryBuilder>();
        services.AddScoped<IControlRunner, SqlControlRunner>();
        services.AddScoped<IControlRepository, SqlControlRepository>();
        services.AddScoped<IControlRunRepository, SqlControlRunRepository>();

        // AI service (stage B): the only outbound dependency, optional — failures surface as 503 on /api/ai only
        services.AddOptions<AiServiceOptions>()
            .Bind(configuration.GetSection(AiServiceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<IAiControlGenerator, HttpAiControlGenerator>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AiServiceOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.MaxResponseContentBufferSize = HttpAiControlGenerator.MaxResponseBytes;
        });
        return services;
    }
}
