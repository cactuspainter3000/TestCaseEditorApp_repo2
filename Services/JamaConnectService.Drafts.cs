using System.Net;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace TestCaseEditorApp.Services;

public partial class JamaConnectService
{
    public JamaConnectService(string baseUrl, string apiToken, HttpClient httpClient) : this(baseUrl, apiToken)
    {
        _httpClient.Dispose();
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _accessToken = apiToken;
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
    }

    public async Task<JamaDraftOptions> GetDraftPublicationOptionsAsync(
        int projectId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectId);
        await EnsureDraftAuthenticationAsync();
        var types = await ReadDraftPagesAsync($"projects/{projectId}/itemtypes", cancellationToken);
        var items = await ReadDraftPagesAsync($"items?project={projectId}", cancellationToken);
        var relationships = await ReadDraftPagesAsync("relationshiptypes", cancellationToken);
        var caseTypes = types.Where(type =>
                (GetStringProperty(type, "display") ?? "").Contains("case", StringComparison.OrdinalIgnoreCase))
            .Select(type => new JamaDraftOption(type.GetProperty("id").GetInt32(),
                GetStringProperty(type, "display") ?? "Unnamed case type")).ToList();
        var containers = items.Where(item => item.TryGetProperty("childItemType", out var child) &&
                child.TryGetInt32(out var id) &&
                caseTypes.Any(type => type.Id == id))
            .Select(item => new JamaDraftOption(item.GetProperty("id").GetInt32(),
                $"{GetStringProperty(item, "documentKey")} - {GetStringProperty(item.GetProperty("fields"), "name")}",
                item.GetProperty("childItemType").GetInt32())).ToList();
        return new JamaDraftOptions(caseTypes, containers, relationships.Select(type =>
            new JamaDraftOption(type.GetProperty("id").GetInt32(), GetStringProperty(type, "name") ?? "Unnamed relationship")).ToList());
    }

    public async Task ValidateDraftDestinationAsync(int projectId, int containerId, int itemTypeId,
        IReadOnlyList<int> requirementIds, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projectId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(containerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemTypeId);
        await EnsureDraftAuthenticationAsync();
        var container = await ReadDraftItemAsync(containerId, cancellationToken);
        if (GetIntProperty(container, "project") != projectId ||
            GetIntProperty(container, "childItemType") != itemTypeId)
            throw new InvalidOperationException("The selected set no longer belongs to this project or accepts this item type.");
        foreach (var id in requirementIds.Distinct())
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
            var requirement = await ReadDraftItemAsync(id, cancellationToken);
            if (GetIntProperty(requirement, "project") != projectId)
                throw new InvalidOperationException($"Requirement item {id} does not belong to the selected Jama project.");
        }
    }

    public async Task<int> CreateDraftTestCaseAsync(int projectId, int containerId, int itemTypeId,
        string name, string description, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await ValidateDraftDestinationAsync(projectId, containerId, itemTypeId, [], cancellationToken);
        var body = new
        {
            project = projectId, itemType = itemTypeId, location = new { parent = containerId },
            fields = new
            {
                name,
                description = $"<pre>{WebUtility.HtmlEncode(description)}</pre>"
            }
        };
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        // Never retry a creation or fall back to another destination.
        using var response = await _httpClient.PostAsync($"{_baseUrl}/rest/v1/items", content, cancellationToken);
        await RequireDraftSuccessAsync(response, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var id = root.TryGetProperty("meta", out var meta) ? GetIntProperty(meta, "id") : null;
        id ??= GetIntProperty(root, "id");
        if (id is not > 0)
            throw new InvalidDataException("Jama did not return a created item ID. Inspect the destination before retrying.");
        return id.Value;
    }

    public async Task LinkDraftTestCaseAsync(int projectId, int requirementId, int testCaseId,
        int relationshipTypeId, CancellationToken cancellationToken = default)
    {
        await EnsureDraftAuthenticationAsync();
        foreach (var id in new[] { requirementId, testCaseId })
        {
            var item = await ReadDraftItemAsync(id, cancellationToken);
            if (GetIntProperty(item, "project") != projectId)
                throw new InvalidOperationException("Cannot create a relationship across different Jama projects.");
        }
        var types = await ReadDraftPagesAsync("relationshiptypes", cancellationToken);
        if (!types.Any(type => GetIntProperty(type, "id") == relationshipTypeId))
            throw new InvalidOperationException("The selected relationship type is no longer available.");
        var existing = await ReadDraftPagesAsync($"items/{requirementId}/downstreamrelationships", cancellationToken);
        if (existing.Any(link => GetIntProperty(link, "toItem") == testCaseId &&
            GetIntProperty(link, "relationshipType") == relationshipTypeId))
            return;
        using var content = new StringContent(JsonSerializer.Serialize(new
        {
            fromItem = requirementId, toItem = testCaseId, relationshipType = relationshipTypeId
        }), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync($"{_baseUrl}/rest/v1/relationships", content, cancellationToken);
        await RequireDraftSuccessAsync(response, cancellationToken);
    }

    private async Task<JsonElement> ReadDraftItemAsync(int itemId, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync($"{_baseUrl}/rest/v1/items/{itemId}", ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task EnsureDraftAuthenticationAsync()
    {
        if (!await EnsureAccessTokenAsync())
            throw new InvalidOperationException("Jama authentication failed. No draft changes were attempted.");
    }

    private static async Task RequireDraftSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Jama returned {(int)response.StatusCode} ({response.ReasonPhrase}): {detail}");
        }
    }

    private async Task<List<JsonElement>> ReadDraftPagesAsync(string route, CancellationToken ct)
    {
        var result = new List<JsonElement>();
        var seenIds = new HashSet<int>();
        const int pageSize = 50;
        while (true)
        {
            var separator = route.Contains('?') ? '&' : '?';
            using var response = await _httpClient.GetAsync(
                $"{_baseUrl}/rest/v1/{route}{separator}startAt={result.Count}&maxResults={pageSize}", ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var data = document.RootElement.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Jama returned a non-list response for {route}.");
            var count = data.GetArrayLength();
            foreach (var item in data.EnumerateArray())
            {
                if (!seenIds.Add(item.GetProperty("id").GetInt32()))
                    throw new InvalidDataException("Jama returned repeated pages or duplicate item IDs.");
                result.Add(item.Clone());
            }
            if (document.RootElement.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("pageInfo", out var info) &&
                info.TryGetProperty("totalResults", out var total))
            {
                if (result.Count >= total.GetInt32()) return result;
                if (count == 0) throw new InvalidDataException("Jama pagination ended before all results were returned.");
            }
            else if (count < pageSize) return result;
        }
    }
}
