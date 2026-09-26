using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Cardflow.MultiInstance.Tests;

public sealed class CrossInstanceBroadcastTests
{
    [Fact]
    public async Task ClientsOnSeparateInstancesExchangeBoardEvents()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var cookies = new CookieContainer();
        using var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies })
        {
            BaseAddress = new Uri("http://127.0.0.1:8082")
        };

        Assert.Equal("api1", (await new HttpClient().GetFromJsonAsync<Health>("http://127.0.0.1:8081/health", timeout.Token))?.Instance);
        Assert.Equal("api2", (await http.GetFromJsonAsync<Health>("/health", timeout.Token))?.Instance);

        using var boardResponse = await http.PostAsJsonAsync("/api/boards", new { title = "Across instances", nickname = "Ada" }, timeout.Token);
        boardResponse.EnsureSuccessStatusCode();
        using var board = await JsonDocument.ParseAsync(await boardResponse.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
        var token = board.RootElement.GetProperty("token").GetString()!;
        var boardUrl = $"/api/boards/{token}";

        using var columnResponse = await http.PostAsJsonAsync($"{boardUrl}/columns",
            new { title = "Now", previousColumnId = (Guid?)null, nextColumnId = (Guid?)null }, timeout.Token);
        columnResponse.EnsureSuccessStatusCode();
        using var column = await JsonDocument.ParseAsync(await columnResponse.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
        var columnId = column.RootElement.GetProperty("id").GetGuid();

        var session = cookies.GetCookies(new Uri("http://127.0.0.1:8082"))["cardflow_session"]
            ?? throw new InvalidOperationException("Board creation did not set a session cookie.");
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Cookie", $"{session.Name}={session.Value}");
        await socket.ConnectAsync(new Uri("ws://127.0.0.1:8081/hubs/board"), timeout.Token);
        var buffer = new StringBuilder();
        await SendAsync(socket, "{\"protocol\":\"json\",\"version\":1}\u001e", timeout.Token);
        using (var handshake = await ReceiveAsync(socket, buffer, timeout.Token))
        {
            Assert.Equal(JsonValueKind.Object, handshake.RootElement.ValueKind);
        }

        await SendAsync(socket, JsonSerializer.Serialize(new
        {
            type = 1, invocationId = "join-1", target = "JoinBoard", arguments = new[] { token }
        }) + "\u001e", timeout.Token);
        while (true)
        {
            using var message = await ReceiveAsync(socket, buffer, timeout.Token);
            if (message.RootElement.TryGetProperty("type", out var type) && type.GetInt32() == 3 &&
                message.RootElement.GetProperty("invocationId").GetString() == "join-1")
            {
                Assert.False(message.RootElement.TryGetProperty("error", out _));
                break;
            }
        }

        using var cardResponse = await http.PostAsJsonAsync($"{boardUrl}/columns/{columnId}/cards",
            new { title = "From api2", description = "", previousCardId = (Guid?)null, nextCardId = (Guid?)null }, timeout.Token);
        cardResponse.EnsureSuccessStatusCode();

        while (true)
        {
            using var message = await ReceiveAsync(socket, buffer, timeout.Token);
            if (!message.RootElement.TryGetProperty("type", out var type) || type.GetInt32() != 1 ||
                message.RootElement.GetProperty("target").GetString() != "BoardEvent")
                continue;

            var boardEvent = message.RootElement.GetProperty("arguments")[0];
            if (boardEvent.GetProperty("type").GetString() != "CardCreated")
                continue;

            Assert.Equal(3, boardEvent.GetProperty("seq").GetInt64());
            Assert.Equal("From api2", boardEvent.GetProperty("payload").GetProperty("title").GetString());
            break;
        }

        using var secondSocket = new ClientWebSocket();
        secondSocket.Options.SetRequestHeader("Cookie", $"{session.Name}={session.Value}");
        await secondSocket.ConnectAsync(new Uri("ws://127.0.0.1:8082/hubs/board"), timeout.Token);
        var secondBuffer = new StringBuilder();
        await SendAsync(secondSocket, "{\"protocol\":\"json\",\"version\":1}\u001e", timeout.Token);
        using (var handshake = await ReceiveAsync(secondSocket, secondBuffer, timeout.Token))
            Assert.Equal(JsonValueKind.Object, handshake.RootElement.ValueKind);

        await SendAsync(secondSocket, JsonSerializer.Serialize(new
        {
            type = 1, invocationId = "create-2", target = "CreateCard",
            arguments = new object[]
            {
                token, columnId,
                new { title = "Through the hub", description = "", previousCardId = (Guid?)null, nextCardId = (Guid?)null }
            }
        }) + "\u001e", timeout.Token);

        while (true)
        {
            using var message = await ReceiveAsync(secondSocket, secondBuffer, timeout.Token);
            if (message.RootElement.TryGetProperty("type", out var type) && type.GetInt32() == 3 &&
                message.RootElement.GetProperty("invocationId").GetString() == "create-2")
            {
                Assert.False(message.RootElement.TryGetProperty("error", out var error), error.ToString());
                break;
            }
        }

        while (true)
        {
            using var message = await ReceiveAsync(socket, buffer, timeout.Token);
            if (!message.RootElement.TryGetProperty("type", out var type) || type.GetInt32() != 1 ||
                message.RootElement.GetProperty("target").GetString() != "BoardEvent")
                continue;

            var boardEvent = message.RootElement.GetProperty("arguments")[0];
            if (boardEvent.GetProperty("type").GetString() != "CardCreated")
                continue;

            Assert.Equal(4, boardEvent.GetProperty("seq").GetInt64());
            Assert.Equal("Through the hub", boardEvent.GetProperty("payload").GetProperty("title").GetString());
            break;
        }

        await SendAsync(secondSocket, JsonSerializer.Serialize(new
        {
            type = 1, invocationId = "denied-3", target = "CreateCard",
            arguments = new object[]
            {
                "not-a-board-token", columnId,
                new { title = "Must not appear", description = "", previousCardId = (Guid?)null, nextCardId = (Guid?)null }
            }
        }) + "\u001e", timeout.Token);

        while (true)
        {
            using var message = await ReceiveAsync(secondSocket, secondBuffer, timeout.Token);
            if (message.RootElement.TryGetProperty("type", out var type) && type.GetInt32() == 3 &&
                message.RootElement.GetProperty("invocationId").GetString() == "denied-3")
            {
                Assert.True(message.RootElement.TryGetProperty("error", out var error),
                    "A command with an invalid board token must fail.");
                Assert.Contains("404", error.GetString());
                break;
            }
        }
    }

    private static Task SendAsync(ClientWebSocket socket, string text, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellationToken);

    private static async Task<JsonDocument> ReceiveAsync(ClientWebSocket socket, StringBuilder pending, CancellationToken cancellationToken)
    {
        var bytes = new byte[4096];
        while (true)
        {
            var current = pending.ToString();
            var end = current.IndexOf('\u001e');
            if (end >= 0)
            {
                pending.Remove(0, end + 1);
                return JsonDocument.Parse(current[..end]);
            }

            var result = await socket.ReceiveAsync(bytes, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("The SignalR socket closed before the event arrived.");
            pending.Append(Encoding.UTF8.GetString(bytes, 0, result.Count));
        }
    }

    private sealed record Health(string Status, string Instance);
}
