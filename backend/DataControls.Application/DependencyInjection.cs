using DataControls.Application.Ai;
using DataControls.Application.Binding;
using DataControls.Application.Catalog;
using DataControls.Application.Controls;
using DataControls.Application.Execution;
using DataControls.Application.Results;
using DataControls.Application.Runs;
using Microsoft.Extensions.DependencyInjection;

namespace DataControls.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<DefinitionBinder>();
        services.AddSingleton<ViolationReasonBuilder>();
        services.AddScoped<IControlExecutionService, ControlExecutionService>();
        services.AddScoped<IBusinessCatalogService, BusinessCatalogService>();
        services.AddScoped<IControlService, ControlService>();
        services.AddScoped<IRunService, RunService>();
        services.AddScoped<IAiSuggestionService, AiSuggestionService>();
        return services;
    }
}
