using Cardflow.Api.Contracts;
using Cardflow.Api.Extensions;
using Cardflow.Api.Helpers;
using Cardflow.Api.Realtime;
using Cardflow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cardflow.Api.Controllers;

/// <summary>
/// Creating a board, joining one, reading its snapshot, and renaming it.
/// Columns and cards get their own controllers (<see cref="ColumnsController"/>,
/// <see cref="CardsController"/>) — one controller per resource, the same
/// granularity your reference project uses (nine controllers, one per
/// domain), rather than one controller for the whole board aggregate.
/// </summary>
[ApiController]
public sealed class BoardsController(
    BoardCommandService commands, BoardService boards, BoardEventPublisher publisher) : ControllerBase
{
    /// <summary>Creates a board and makes the caller its first member.</summary>
    [HttpPost("api/boards")]
    [ProducesResponseType(typeof(BoardCreated), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateBoard(
        [FromBody] CreateBoardRequest request, CancellationToken cancellationToken)
    {
        var session = SessionCookieHelper.ExistingOrNew(HttpContext);
        var value = await commands.CreateBoardAsync(request, session, cancellationToken)
            .PublishAndUnwrapAsync(publisher);
        SessionCookieHelper.Set(HttpContext, session);
        return Created($"/api/boards/{value.Token}", value);
    }

    /// <summary>Joins a board by its secret link, with a nickname and no signup.</summary>
    [HttpPost("api/boards/{token}/join")]
    [ProducesResponseType(typeof(MemberView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Join(
        string token, [FromBody] JoinBoardRequest request, CancellationToken cancellationToken)
    {
        var session = SessionCookieHelper.ExistingOrNew(HttpContext);
        var value = await commands.JoinAsync(token, request, session, cancellationToken)
            .PublishAndUnwrapAsync(publisher);
        SessionCookieHelper.Set(HttpContext, session);
        return Ok(value);
    }

    /// <summary>Returns the board's current columns, cards, members and sequence number.</summary>
    [HttpGet("api/boards/{token}")]
    [ProducesResponseType(typeof(BoardSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<BoardSnapshot> GetSnapshot(string token, CancellationToken cancellationToken) =>
        boards.SnapshotAsync(token, SessionCookieHelper.Existing(HttpContext), cancellationToken);

    /// <summary>Renames the board.</summary>
    [HttpPatch("api/boards/{token}")]
    [ProducesResponseType(typeof(BoardSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Rename(
        string token, [FromBody] RenameBoardRequest request, CancellationToken cancellationToken) =>
        Ok(await commands.RenameBoardAsync(token, SessionCookieHelper.Existing(HttpContext), request, cancellationToken)
            .PublishAndUnwrapAsync(publisher));
}
