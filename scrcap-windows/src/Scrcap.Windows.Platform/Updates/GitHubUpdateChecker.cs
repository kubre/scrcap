using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scrcap.Windows.Platform.Updates;

public enum UpdateAvailability
{
    Available,
    UpToDate,
    CannotCompare,
}

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("html_url")] Uri HtmlUrl);

public sealed record UpdateCheckResult(
    UpdateAvailability Availability,
    string CurrentVersion,
    GitHubRelease Release);

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default);
}

public sealed class GitHubUpdateChecker : IUpdateChecker
{
    public const string SourceUrl = "https://github.com/kubre/scrcap";
    private static readonly Uri LatestReleaseUrl = new("https://api.github.com/repos/kubre/scrcap/releases/latest");
    private static readonly HttpClient SharedClient = new();
    private readonly HttpClient client;

    public GitHubUpdateChecker(HttpClient? client = null)
    {
        this.client = client ?? SharedClient;
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("scrcap-windows");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        await using var content = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(content, cancellationToken: timeout.Token).ConfigureAwait(false)
                      ?? throw new InvalidDataException("GitHub returned an invalid release response.");
        if (string.IsNullOrWhiteSpace(release.TagName) || !release.HtmlUrl.IsAbsoluteUri)
        {
            throw new InvalidDataException("GitHub returned an incomplete release response.");
        }

        var availability = ReleaseVersion.TryParse(currentVersion, out var current)
                           && ReleaseVersion.TryParse(release.TagName, out var latest)
            ? latest > current ? UpdateAvailability.Available : UpdateAvailability.UpToDate
            : UpdateAvailability.CannotCompare;
        return new UpdateCheckResult(availability, currentVersion, release);
    }
}

public readonly struct ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private readonly IReadOnlyList<int> components;

    private ReleaseVersion(IReadOnlyList<int> components)
    {
        this.components = components;
    }

    public static bool TryParse(string? rawValue, out ReleaseVersion version)
    {
        version = default;
        var value = rawValue?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value[0] is 'v' or 'V')
        {
            value = value[1..];
        }

        value = value.Split(['-', '+'], 2)[0];
        var parts = value.Split('.', StringSplitOptions.None);
        var parsed = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length == 0
                || parts[index].Any(character => character is < '0' or > '9')
                || !int.TryParse(parts[index], out parsed[index]))
            {
                return false;
            }
        }

        version = new ReleaseVersion(parsed);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var left = components ?? [];
        var right = other.components ?? [];
        for (var index = 0; index < Math.Max(left.Count, right.Count); index++)
        {
            var comparison = (index < left.Count ? left[index] : 0).CompareTo(index < right.Count ? right[index] : 0);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    public bool Equals(ReleaseVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        var normalizedLength = components?.Count ?? 0;
        while (normalizedLength > 0 && components![normalizedLength - 1] == 0)
        {
            normalizedLength--;
        }

        for (var index = 0; index < normalizedLength; index++)
        {
            hash.Add(components![index]);
        }

        return hash.ToHashCode();
    }

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
}
