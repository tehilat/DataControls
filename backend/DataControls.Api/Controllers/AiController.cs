using DataControls.Api.Contracts;
using DataControls.Application.Ai;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Controllers;

/// <summary>
/// Free text → proposed control (stage B). Only proposes: nothing is saved or run here.
/// The AI service is optional — when it is down these endpoints return 503 and the rest of the API keeps working.
/// </summary>
[ApiController]
[Route("api/ai")]
public sealed class AiController(IAiSuggestionService suggestions) : ControllerBase
{
    /// <summary>Proposes a control definition for a free-text description, validated against the catalog.</summary>
    [HttpPost("suggest-control")]
    [ProducesResponseType<AiSuggestionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<AiSuggestionDto> SuggestControl(SuggestControlRequest request, CancellationToken ct) =>
        AiSuggestionDto.From(await suggestions.SuggestAsync(request.Text, ct));

    /// <summary>The full prompt a real model would receive for <paramref name="text"/> (demonstration).</summary>
    /// <param name="text">The control described in free text.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("prompt-preview")]
    [ProducesResponseType<AiPromptPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<AiPromptPreviewDto> PromptPreview([FromQuery] string? text, CancellationToken ct) =>
        AiPromptPreviewDto.From(await suggestions.PreviewPromptAsync(text, ct));
}
