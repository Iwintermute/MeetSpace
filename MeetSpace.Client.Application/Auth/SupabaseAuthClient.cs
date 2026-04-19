using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MeetSpace.Client.App.Auth;

public sealed class SupabaseAuthClient : ISupabaseAuthClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly string _anonKey;

    public SupabaseAuthClient(HttpClient httpClient, string projectUrl, string anonKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _anonKey = anonKey ?? string.Empty;

        if (string.IsNullOrWhiteSpace(projectUrl))
            throw new ArgumentException("Supabase project URL is not configured.", nameof(projectUrl));

        if (string.IsNullOrWhiteSpace(_anonKey))
            throw new ArgumentException("Supabase anon key is not configured.", nameof(anonKey));

        var normalized = projectUrl.Trim();

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed))
            throw new ArgumentException("Supabase project URL must be an absolute URI.", nameof(projectUrl));

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Supabase project URL must use http or https.", nameof(projectUrl));
        }

        var root = parsed.GetLeftPart(UriPartial.Authority);
        if (!root.EndsWith("/", StringComparison.Ordinal))
            root += "/";

        _baseUri = new Uri(root, UriKind.Absolute);

        if (_httpClient.BaseAddress == null)
            _httpClient.BaseAddress = _baseUri;
    }

    public Task<AuthResult> SignUpAsync(string email, string password, CancellationToken cancellationToken = default)
        => SendAuthRequestAsync(
            "auth/v1/signup",
            new { email, password },
            isSignUp: true,
            cancellationToken);

    public Task<AuthResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
        => SendAuthRequestAsync(
            "auth/v1/token?grant_type=password",
            new { email, password },
            isSignUp: false,
            cancellationToken);

    public async Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Refresh token must not be empty.", nameof(refreshToken));

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "auth/v1/token?grant_type=refresh_token",
            new { refresh_token = refreshToken });

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new SupabaseAuthException(ExtractErrorMessage(json), (int)response.StatusCode);

        return ParseTokensOrThrow(json);
    }

    public async Task SignOutAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return;

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("auth/v1/logout"));
        request.Headers.TryAddWithoutValidation("apikey", _anonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new SupabaseAuthException(ExtractErrorMessage(json), (int)response.StatusCode);
        }
    }

    private async Task<AuthResult> SendAuthRequestAsync(
        string path,
        object payload,
        bool isSignUp,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Post, path, payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new SupabaseAuthException(ExtractErrorMessage(json), (int)response.StatusCode);

        return ParseAuthResult(json, isSignUp);
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, object payload)
    {
        var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.TryAddWithoutValidation("apikey", _anonKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        return request;
    }

    private Uri BuildUri(string path)
    {
        var relative = (path ?? string.Empty).TrimStart('/');
        return new Uri(_baseUri, relative);
    }

    private static AuthResult ParseAuthResult(string json, bool isSignUp)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (TryParseTokens(root, out var tokens))
            return new AuthResult(true, false, tokens, null);

        if (isSignUp &&
            root.TryGetProperty("user", out var userProp) &&
            userProp.ValueKind == JsonValueKind.Object)
        {
            return new AuthResult(
                false,
                true,
                null,
                "Подтвердите email по ссылке из письма, затем выполните вход ещё раз.");
        }

        throw new SupabaseAuthException("Supabase returned an auth payload without a valid session.");
    }

    private static AuthTokens ParseTokensOrThrow(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (TryParseTokens(root, out var tokens))
            return tokens!;

        throw new SupabaseAuthException("Supabase returned an auth payload without a valid session.");
    }

    private static bool TryParseTokens(JsonElement root, out AuthTokens? tokens)
    {
        tokens = null;
        var tokenSource = root;
        var accessToken = NormalizeTokenValue(GetString(tokenSource, "access_token"));
        var refreshToken = NormalizeTokenValue(GetString(tokenSource, "refresh_token"));

        if ((string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken)) &&
            root.TryGetProperty("session", out var sessionProp) &&
            sessionProp.ValueKind == JsonValueKind.Object)
        {
            tokenSource = sessionProp;
            accessToken = NormalizeTokenValue(GetString(tokenSource, "access_token"));
            refreshToken = NormalizeTokenValue(GetString(tokenSource, "refresh_token"));
        }

        string? userId = null;
        string? email = null;

        if (root.TryGetProperty("user", out var userProp) && userProp.ValueKind == JsonValueKind.Object)
        {
            userId = NormalizeTokenValue(GetString(userProp, "id"));
            email = NormalizeTokenValue(GetString(userProp, "email"));
        }
        else if (tokenSource.ValueKind == JsonValueKind.Object &&
                 tokenSource.TryGetProperty("user", out var tokenUserProp) &&
                 tokenUserProp.ValueKind == JsonValueKind.Object)
        {
            userId = NormalizeTokenValue(GetString(tokenUserProp, "id"));
            email = NormalizeTokenValue(GetString(tokenUserProp, "email"));
        }

        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(refreshToken) ||
            string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        DateTimeOffset? expiresAtUtc = null;
        if (tokenSource.TryGetProperty("expires_in", out var expiresProp) &&
            expiresProp.ValueKind == JsonValueKind.Number &&
            expiresProp.TryGetInt32(out var expiresIn))
        {
            expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        }

        tokens = new AuthTokens(
            accessToken,
            refreshToken,
            userId,
            email,
            expiresAtUtc);

        return true;
    }

    private static string ExtractErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var message =
                GetString(root, "msg") ??
                GetString(root, "message") ??
                GetString(root, "error_description") ??
                GetString(root, "error");

            if (!string.IsNullOrWhiteSpace(message))
                return message!;
        }
        catch
        {
        }

        return "Supabase auth request failed.";
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
            return null;
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Number => prop.GetRawText(),
            _ => null
        };
    }

    private static string? NormalizeTokenValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "undefined", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized;
    }
}