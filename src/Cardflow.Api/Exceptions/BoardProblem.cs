namespace Cardflow.Api.Exceptions;

/// <summary>
/// An error that should reach the client as an HTTP problem response — a bad
/// request, a missing board, a stale version — rather than a 500. A service
/// throws this; the pipeline in <c>WebApplicationExtensions</c> turns it into
/// a problem-details response with <see cref="StatusCode"/>.
/// </summary>
public sealed class BoardProblem(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
