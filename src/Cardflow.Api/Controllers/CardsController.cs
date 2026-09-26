using Cardflow.Api.Contracts;
using Cardflow.Api.Extensions;
using Cardflow.Api.Helpers;
using Cardflow.Api.Realtime;
using Cardflow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cardflow.Api.Controllers;

/// <summary>Creating, editing, moving and archiving cards.</summary>
[ApiController]
public sealed class CardsController(BoardCommandService commands, BoardEventPublisher publisher) : ControllerBase
{
    /// <summary>Adds a card to a column, optionally between two existing cards.</summary>
    [HttpPost("api/boards/{token}/columns/{columnId:guid}/cards")]
    [ProducesResponseType(typeof(CardView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        string token, Guid columnId, [FromBody] CreateCardRequest request, CancellationToken cancellationToken) =>
        Ok(await commands.CreateCardAsync(token, SessionCookieHelper.Existing(HttpContext), columnId, request, cancellationToken)
            .PublishAndUnwrapAsync(publisher));

    /// <summary>Edits a card's title and description. Rejected if the caller's version is stale.</summary>
    [HttpPatch("api/boards/{token}/cards/{cardId:guid}")]
    [ProducesResponseType(typeof(CardView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Edit(
        string token, Guid cardId, [FromBody] EditCardRequest request, CancellationToken cancellationToken) =>
        Ok(await commands.EditCardAsync(token, SessionCookieHelper.Existing(HttpContext), cardId, request, cancellationToken)
            .PublishAndUnwrapAsync(publisher));

    /// <summary>Moves a card to a new column and/or position. Rejected if the caller's version is stale.</summary>
    [HttpPost("api/boards/{token}/cards/{cardId:guid}/move")]
    [ProducesResponseType(typeof(CardView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Move(
        string token, Guid cardId, [FromBody] MoveCardRequest request, CancellationToken cancellationToken) =>
        Ok(await commands.MoveCardAsync(token, SessionCookieHelper.Existing(HttpContext), cardId, request, cancellationToken)
            .PublishAndUnwrapAsync(publisher));

    /// <summary>Archives a card. Rejected if the caller's version is stale.</summary>
    [HttpPost("api/boards/{token}/cards/{cardId:guid}/archive")]
    [ProducesResponseType(typeof(CardView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Archive(
        string token, Guid cardId, [FromBody] VersionRequest request, CancellationToken cancellationToken) =>
        Ok(await commands.ArchiveCardAsync(token, SessionCookieHelper.Existing(HttpContext), cardId, request, cancellationToken)
            .PublishAndUnwrapAsync(publisher));
}
