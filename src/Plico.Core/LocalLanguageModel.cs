using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Plico.Core;

/// <summary>Explicit consent and the locally installed model names for one machine.</summary>
public sealed record LocalModelSettings(bool Enabled, string Endpoint, string ChatModel, string EmbeddingModel,
    bool UseEmbeddingPrefixes = false)
{
    /// <summary>The initial configuration. Model requests require explicit activation.</summary>
    public static LocalModelSettings Default { get; } = new(false, "http://127.0.0.1:11434", "", "");

    /// <summary>Validates and normalizes an HTTP endpoint that is reachable only through the loopback interface.</summary>
    public static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            uri.Port <= 0 || uri.AbsolutePath.TrimEnd('/') is not ("" or "/v1"))
            throw new ArgumentException("Enter the local model server address, for example http://127.0.0.1:11434.");
        var builder = new UriBuilder(uri) { Path = "/v1/" };
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) builder.Host = IPAddress.Loopback.ToString();
        else if (!IPAddress.TryParse(uri.DnsSafeHost, out var address) ||
                 (!address.Equals(IPAddress.Loopback) && !address.Equals(IPAddress.IPv6Loopback)))
            throw new ArgumentException("Use 127.0.0.1, localhost, or ::1 for the model server.");
        return builder.Uri;
    }

    /// <summary>Checks the settings without connecting to the server.</summary>
    public LocalModelSettings Validate()
    {
        string chat = CleanModelName(ChatModel);
        string embedding = CleanModelName(EmbeddingModel);
        string endpoint = ValidateEndpoint(Endpoint).AbsoluteUri.TrimEnd('/');
        if (Enabled && chat.Length == 0 && embedding.Length == 0)
            throw new ArgumentException("Enter the name of an installed chat or embedding model.");
        return this with { Endpoint = endpoint, ChatModel = chat, EmbeddingModel = embedding };
    }

    private static string CleanModelName(string model)
    {
        const int maximumModelName = 200;
        var name = (model ?? "").Trim();
        if (name.Length > maximumModelName || name.Any(char.IsControl) || name.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a locally installed model name.");
        return name;
    }
}

/// <summary>The two input roles used by retrieval-oriented embedding models.</summary>
public enum EmbeddingInput { Query, Document }

/// <summary>Embeds bounded text for a local similarity index.</summary>
public interface ILocalEmbeddingModel
{
    /// <summary>An identity that changes when the embedding configuration changes.</summary>
    string CacheKey { get; }

    /// <summary>Returns one vector for each input in order.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> text, EmbeddingInput input, CancellationToken cancellationToken);
}

/// <summary>A message in a single local conversation.</summary>
public sealed record LocalModelMessage(string Role, string Content);

/// <summary>Produces text or a JSON object from a local model without executing tools.</summary>
public interface ILocalLanguageModel
{
    /// <summary>Completes the supplied conversation within the caller's cancellation boundary.</summary>
    Task<string> CompleteAsync(IReadOnlyList<LocalModelMessage> messages, bool jsonObject, CancellationToken cancellationToken);
}

/// <summary>Connects to a user's opted-in OpenAI-compatible loopback service.</summary>
public sealed class LocalModelClient : ILocalEmbeddingModel, ILocalLanguageModel, IDisposable
{
    internal const int MaximumEmbeddingBatch = 32;
    internal const int MaximumDimension = 4_096;
    private const int MaximumInputCharacters = 131_072;
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private const int MaximumOutputTokens = 768;
    private const int MaximumMessageCount = 20;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(4);
    private readonly LocalModelSettings _settings;
    private readonly HttpClient _client;

    /// <summary>Configures the local connection without contacting the service.</summary>
    public LocalModelClient(LocalModelSettings settings) : this(settings, new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(5)
    }) { }

    internal LocalModelClient(LocalModelSettings settings, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled)
        {
            handler.Dispose();
            throw new InvalidOperationException("Enable local models in settings first.");
        }
        try
        {
            _settings = settings.Validate();
            _client = new HttpClient(handler) { BaseAddress = LocalModelSettings.ValidateEndpoint(_settings.Endpoint), Timeout = RequestTimeout };
        }
        catch
        {
            handler.Dispose();
            throw;
        }
        CacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{_settings.Endpoint}\n{_settings.EmbeddingModel}\n{_settings.UseEmbeddingPrefixes}")));
    }

    /// <inheritdoc/>
    public string CacheKey { get; }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> text, EmbeddingInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_settings.EmbeddingModel.Length == 0) throw new InvalidOperationException("Choose a local embedding model in settings.");
        if (text.Count is < 1 or > MaximumEmbeddingBatch || text.Sum(item => (long)item.Length) > MaximumInputCharacters)
            throw new ArgumentException("The local embedding batch is too large.", nameof(text));
        var inputs = !_settings.UseEmbeddingPrefixes ? text : text.Select(value =>
            (input == EmbeddingInput.Query ? "query: " : "passage: ") + value).ToArray();
        using var json = await SendAsync("embeddings", new { model = _settings.EmbeddingModel, input = inputs, encoding_format = "float" },
            cancellationToken).ConfigureAwait(false);

        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != text.Count)
            throw new InvalidDataException("The local model returned a different number of embeddings.");
        var vectors = new float[text.Count][];
        int dimension = 0;
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("index", out var index) ||
                index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out int entry) ||
                entry < 0 || entry >= vectors.Length || vectors[entry] is not null ||
                !item.TryGetProperty("embedding", out var embedding) || embedding.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The local model returned an invalid embedding entry.");
            int count = embedding.GetArrayLength();
            if (count is < 1 or > MaximumDimension || (dimension != 0 && dimension != count))
                throw new InvalidDataException("The local model returned inconsistent embedding dimensions.");
            dimension = count;
            var vector = new float[count];
            int coordinate = 0;
            foreach (var component in embedding.EnumerateArray())
            {
                if (component.ValueKind != JsonValueKind.Number || !component.TryGetSingle(out float value) || !float.IsFinite(value))
                    throw new InvalidDataException("The local model returned a non-finite embedding.");
                vector[coordinate++] = value;
            }
            vectors[entry] = vector;
        }
        return vectors;
    }

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(IReadOnlyList<LocalModelMessage> messages, bool jsonObject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (_settings.ChatModel.Length == 0) throw new InvalidOperationException("Choose a local chat model in settings.");
        if (messages.Count is < 1 or > MaximumMessageCount || messages.Sum(message => (long)message.Content.Length) > MaximumInputCharacters ||
            messages.Any(message => message.Role is not ("system" or "user" or "assistant")))
            throw new ArgumentException("The local conversation is too large or contains an unsupported role.", nameof(messages));
        var request = new Dictionary<string, object>
        {
            ["model"] = _settings.ChatModel, ["messages"] = messages, ["stream"] = false,
            ["max_tokens"] = MaximumOutputTokens, ["temperature"] = 0
        };
        if (jsonObject) request["response_format"] = new { type = "json_object" };
        using var json = await SendAsync("chat/completions", request, cancellationToken).ConfigureAwait(false);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 || choices[0].ValueKind != JsonValueKind.Object ||
            !choices[0].TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String ||
            content.GetString() is not { Length: > 0 } answer)
            throw new InvalidDataException("The local model returned no text.");
        return answer.Trim();
    }

    private async Task<JsonDocument> SendAsync(string route, object payload, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(payload) };
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new IOException($"The local model server returned HTTP {(int)response.StatusCode}. Check the server and installed model.");
            await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, deadline.Token).ConfigureAwait(false);
            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(body, cancellationToken: deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("The local model took too long to respond. Check the server or choose a smaller model.");
        }
        catch (HttpRequestException error)
        {
            throw new IOException("The local model server could not be reached or returned too much data.", error);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The local model server returned invalid JSON.", error);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _client.Dispose();
}
