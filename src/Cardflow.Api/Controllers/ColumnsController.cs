using Cardflow.Api.Contracts;
using Cardflow.Api.Extensions;
using Cardflow.Api.Helpers;
using Cardflow.Api.Realtime;
using Cardflow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cardflow.Api.Controllers;

/// <summary>Creating and renaming columns on a board.</summary>
[ApiController]
public sealed class ColumnsController(BoardCommandService commands, BoardEventPublisher publisher) : ControllerBase
{
    /// <summary>Adds a column, optionally between two existing columns.</summary>
    [HttpPost("api/boards/{token}/columns")]
    [ProducesResponseType(typeof(ColumnView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        string token, [FromBody] CreateColumnRequest request, CancellationToken cancellationToken) =>
        Ok(await commands.CreateColumnAsync(token, SessionCookieHelper.Existing(HttpContext), request, cancellationToken)
            .PublishAndUnwrapAsync(publisher));

    /// <summary>Renames a column.</summary>
    [HttpPatch("api/boards/{token}/columns/{columnId:guid}")]
    [ProducesResponseType(typeof(ColumnView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rename(
        string token, Guid columnId, [FromBody] RenameColumnRequest request, CancellationToken cancellationToken) =>
        Ok(await commands.RenameColumnAsync(token, SessionCookieHelper.Existing(HttpContext), columnId, request, cancellationToken)
            .PublishAndUnwrapAsync(publisher));
}
