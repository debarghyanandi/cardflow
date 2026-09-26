using Cardflow.Api.Contracts;
using Cardflow.Api.Realtime;

namespace Cardflow.Api.Extensions;

/// <summary>
/// Every mutating board command returns a <see cref="BoardMutation{T}"/> — the
/// new value, plus the event that must be broadcast in the same breath. A
/// controller action always does the same two things with that pair: publish
/// the event, then hand back the value. This extension is that glue, so no
/// controller action repeats it — the response-shaping-extension idea from
/// your reference project's <c>Api/Extensions</c> folder, adapted to
/// Cardflow's value+event shape instead of its response-envelope shape.
/// </summary>
public static class BoardMutationExtensions
{
    public static async Task<T> PublishAndUnwrapAsync<T>(
        this Task<BoardMutation<T>> operation, BoardEventPublisher publisher)
    {
        var mutation = await operation;
        await publisher.PublishAsync(mutation.Event);
        return mutation.Value;
    }
}
