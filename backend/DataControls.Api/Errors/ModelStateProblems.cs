using System.Text.RegularExpressions;
using DataControls.Application.Errors;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Errors;

/// <summary>
/// Turns model-binding / DataAnnotations errors into the same Hebrew validation ProblemDetails as the engine.
/// Framework messages (English, from System.Text.Json or MVC) are replaced with Hebrew ones.
/// </summary>
public static partial class ModelStateProblems
{
    [GeneratedRegex("[֐-׿]")]
    private static partial Regex Hebrew();

    public static IActionResult Create(ActionContext context)
    {
        var errors = new List<ValidationError>();
        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.Errors.Count == 0) continue;
            var path = NormalizePath(key);
            foreach (var error in entry.Errors)
            {
                var message = !string.IsNullOrEmpty(error.ErrorMessage) && Hebrew().IsMatch(error.ErrorMessage)
                    ? error.ErrorMessage
                    : path.Length == 0 || path == "request"
                        ? "גוף הבקשה חסר או שאינו JSON תקין."
                        : $"הערך בשדה \"{path}\" אינו תקין.";
                errors.Add(new ValidationError(path == "request" ? "" : path, message));
            }
        }

        // a JSON error on a specific field also leaves a generic "body is required" error behind — keep the specific one
        if (errors.Any(e => e.Path.Length > 0))
            errors.RemoveAll(e => e.Path.Length == 0);

        var problem = Problems.Validation(ProblemTitles.InvalidRequest, errors.DistinctBy(e => (e.Path, e.Message)).ToList());
        problem.Instance = context.HttpContext.Request.Path;
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    }

    /// <summary>"$.severity" → "severity", "$" → "".</summary>
    private static string NormalizePath(string key) =>
        key == "$" ? "" : key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
}
