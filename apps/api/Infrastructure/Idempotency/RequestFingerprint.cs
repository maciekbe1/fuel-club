using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace FuelClub.Api.Infrastructure.Idempotency;

public sealed class RequestFingerprintException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// SHA-256 over a canonical description of the request: method, route template, route values, query
/// and body. JSON bodies are canonicalized (object keys sorted, insignificant whitespace removed);
/// numbers and strings keep their literal form, so <c>10</c> and <c>10.0</c> differ (fail-safe: a
/// different spelling is reported as key reuse instead of being replayed). Duplicate JSON property
/// names are rejected because binders resolve them differently; the check ignores case, because DTO
/// binding (JsonSerializerDefaults.Web) matches property names case-insensitively.
/// </summary>
public static class RequestFingerprint
{
    public static byte[] Compute(
        string method,
        string routeTemplate,
        IEnumerable<KeyValuePair<string, string?>> routeValues,
        IEnumerable<KeyValuePair<string, StringValues>> query,
        string? contentType,
        ReadOnlySpan<byte> body
    )
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("method", method.ToUpperInvariant());
            writer.WriteString("route", routeTemplate);

            writer.WriteStartObject("routeValues");
            foreach (
                var (name, value) in routeValues.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            )
                writer.WriteString(name, value);
            writer.WriteEndObject();

            writer.WriteStartObject("query");
            foreach (var (name, values) in query.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteStartArray(name);
                foreach (var value in values)
                    writer.WriteStringValue(value);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();

            writer.WritePropertyName("body");
            WriteBody(writer, contentType, body);
            writer.WriteEndObject();
        }

        return SHA256.HashData(buffer.ToArray());
    }

    private static void WriteBody(
        Utf8JsonWriter writer,
        string? contentType,
        ReadOnlySpan<byte> body
    )
    {
        if (body.IsEmpty)
        {
            writer.WriteNullValue();
            return;
        }

        if (!IsJson(contentType))
        {
            writer.WriteBase64StringValue(body);
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body.ToArray());
        }
        catch (JsonException)
        {
            throw new RequestFingerprintException(IdempotencyErrorCodes.InvalidJson);
        }

        using (document)
            WriteCanonical(writer, document.RootElement);
    }

    private static bool IsJson(string? contentType) =>
        contentType is not null
        && (
            contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("+json", StringComparison.OrdinalIgnoreCase)
        );

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = element.EnumerateObject().ToList();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in properties)
                    if (!names.Add(property.Name))
                        throw new RequestFingerprintException(
                            IdempotencyErrorCodes.DuplicateJsonProperty
                        );

                writer.WriteStartObject();
                foreach (
                    var property in properties.OrderBy(
                        property => property.Name,
                        StringComparer.Ordinal
                    )
                )
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
