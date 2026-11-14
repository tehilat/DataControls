using System.Text.Json.Serialization;
using DataControls.Api.Errors;
using DataControls.Application;
using DataControls.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

const string FrontendCorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddControllers(options =>
    {
        // validation errors are keyed by the JSON names ("name", not "Name")
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
    })
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ModelStateProblems.Create);

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var problem = context.ProblemDetails;
    if (problem.Status is { } status && (problem.Title is null || !ContainsHebrew(problem.Title)))
        problem.Title = ProblemTitles.ForStatus(status);
    problem.Instance ??= context.HttpContext.Request.Path;
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() is { Length: > 0 } origins
    ? origins
    : ["http://localhost:4200"];
builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Location")));

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Data Controls API";
    document.Info.Description = "מערכת בקרות נתונים — קטלוג עסקי, הגדרת בקרות, הרצה ותוצאות.";
    return Task.CompletedTask;
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Data Controls API v1");
        options.RoutePrefix = "swagger";
    });
}
else
{
    // in development the Angular dev server calls plain http; a redirect would break the CORS preflight
    app.UseHttpsRedirection();
}

app.UseCors(FrontendCorsPolicy);
app.UseAuthorization();
app.MapControllers();

app.Run();

static bool ContainsHebrew(string text) => text.Any(c => c is >= '֐' and <= '׿');

/// <summary>Entry point; public for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
