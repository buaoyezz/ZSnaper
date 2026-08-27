using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZSnaper.Services;

public sealed record HitokotoSentence(string Text, string? Source);

/// <summary>
/// Small, non-blocking client for the public Hitokoto sentence API.
/// </summary>
public static class HitokotoService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<HitokotoSentence?> FetchAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await Client.GetAsync(
            "?encode=json",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        HitokotoResponse? result = await JsonSerializer.DeserializeAsync<HitokotoResponse>(
            responseStream,
            JsonOptions,
            cancellationToken);

        string? text = Normalize(result?.Text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new HitokotoSentence(text, Normalize(result?.Source));
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://v1.hitokoto.cn/"),
            Timeout = TimeSpan.FromSeconds(3)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZSnaper/0.0.2");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private sealed class HitokotoResponse
    {
        [JsonPropertyName("hitokoto")]
        public string? Text { get; set; }

        [JsonPropertyName("from")]
        public string? Source { get; set; }
    }
}
