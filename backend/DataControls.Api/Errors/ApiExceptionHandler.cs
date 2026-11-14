using DataControls.Application.Errors;
using DataControls.Domain.Definition;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Errors;

/// <summary>
/// Maps application exceptions to ProblemDetails with Hebrew, user-facing text.
/// Unexpected exceptions never leak their message or stack trace to the client.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            ControlValidationException ex => Problems.Validation(ProblemTitles.InvalidDefinition, ex.Errors),
            InvalidRequestException ex => Problems.Validation(ProblemTitles.InvalidRequest, ex.Errors),
            DefinitionFormatException ex => Problems.Validation(ProblemTitles.InvalidDefinition, [new ValidationError("", ex.Message)]),
            NotFoundException ex => Problems.Create(StatusCodes.Status404NotFound, ProblemTitles.NotFound, ex.Message),
            ConflictException ex => WithPath(Problems.Create(StatusCodes.Status409Conflict, ProblemTitles.Conflict, ex.Message), ex.Path),
            ControlExecutionException ex => WithRunId(
                Problems.Create(StatusCodes.Status422UnprocessableEntity, ProblemTitles.ExecutionFailed, ex.Message), ex.RunId),
            AiServiceUnavailableException ex => Problems.Create(StatusCodes.Status503ServiceUnavailable, ProblemTitles.AiUnavailable, ex.Message),
            BadHttpRequestException ex => Problems.Create(ex.StatusCode, ProblemTitles.InvalidRequest, ProblemTitles.InvalidRequest),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => null,
            _ => null
        };

        if (problem is null)
        {
            if (context.RequestAborted.IsCancellationRequested) return true; // client went away; nothing to send
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            problem = Problems.Create(StatusCodes.Status500InternalServerError, ProblemTitles.ServerError,
                "אירעה שגיאה בלתי צפויה. אם הבעיה חוזרת, יש לפנות לתמיכה עם מזהה הבקשה.");
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails WithPath(ProblemDetails problem, string? path)
    {
        if (path is not null) problem.Extensions["path"] = path;
        return problem;
    }

    private static ProblemDetails WithRunId(ProblemDetails problem, int? runId)
    {
        if (runId is not null) problem.Extensions["runId"] = runId;
        return problem;
    }
}

public static class ProblemTitles
{
    public const string InvalidDefinition = "הגדרת הבקרה אינה תקינה";
    public const string InvalidRequest = "הבקשה אינה תקינה";
    public const string NotFound = "הפריט המבוקש לא נמצא";
    public const string Conflict = "הפעולה מתנגשת במצב הנוכחי";
    public const string ExecutionFailed = "הרצת הבקרה נכשלה";
    public const string ServerError = "שגיאת שרת";
    public const string AiUnavailable = "שירות ה-AI אינו זמין";

    public static string ForStatus(int status) => status switch
    {
        400 => InvalidRequest,
        401 => "נדרשת הזדהות",
        403 => "אין הרשאה לפעולה זו",
        404 => NotFound,
        405 => "הפעולה אינה נתמכת בכתובת זו",
        409 => Conflict,
        415 => "סוג התוכן אינו נתמך (יש לשלוח JSON)",
        422 => ExecutionFailed,
        503 => "השירות אינו זמין כרגע",
        _ when status >= 500 => ServerError,
        _ => "שגיאה"
    };
}

public static class Problems
{
    public static ProblemDetails Create(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };

    /// <summary>
    /// 400 with "errors" as path → messages (the standard ValidationProblemDetails shape) plus "validationErrors",
    /// an ordered list of { path, message } — the path points into the JSON (e.g. "condition.children[1].field").
    /// </summary>
    public static ValidationProblemDetails Validation(string title, IReadOnlyList<ValidationError> errors)
    {
        var problem = new ValidationProblemDetails(errors
            .GroupBy(e => e.Path)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = title,
            Detail = errors.Count == 1 ? errors[0].Message : $"נמצאו {errors.Count} שגיאות."
        };
        problem.Extensions["validationErrors"] = errors.Select(e => new { path = e.Path, message = e.Message }).ToList();
        return problem;
    }
}
