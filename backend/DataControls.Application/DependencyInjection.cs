using DataControls.Application.Binding;
using DataControls.Application.Execution;
using DataControls.Application.Results;
using Microsoft.Extensions.DependencyInjection;

namespace DataControls.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<DefinitionBinder>();
        services.AddSingleton<ViolationReasonBuilder>();
        services.AddScoped<IControlExecutionService, ControlExecutionService>();
        return services;
    }
}
