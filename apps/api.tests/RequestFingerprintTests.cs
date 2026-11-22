using System.Text;
using FuelClub.Api.Infrastructure.Idempotency;
using Microsoft.Extensions.Primitives;

namespace FuelClub.Api.Tests;

public sealed class RequestFingerprintTests
{
    private static byte[] Fingerprint(
        string body = """{"a":1}""",
        string route = "/api/coupons/{id}/activate",
        string id = "A",
        Dictionary<string, StringValues>? query = null,
        string method = "POST",
        string? contentType = "application/json"
    ) =>
        RequestFingerprint.Compute(
            method,
            route,
            [KeyValuePair.Create("id", (string?)id)],
            query ?? [],
            contentType,
            Encoding.UTF8.GetBytes(body)
        );

    [Fact]
    public void Different_route_values_give_different_fingerprints() =>
        Assert.NotEqual(Fingerprint(id: "A"), Fingerprint(id: "B"));

    [Fact]
    public void Different_templates_and_methods_give_different_fingerprints()
    {
        Assert.NotEqual(
            Fingerprint(route: "/api/coupons/{id}/activate"),
            Fingerprint(route: "/api/coupons/{id}/redeem")
        );
        Assert.NotEqual(Fingerprint(method: "POST"), Fingerprint(method: "PUT"));
    }

    [Fact]
    public void Query_takes_part_and_its_order_does_not_matter()
    {
        Assert.NotEqual(Fingerprint(), Fingerprint(query: new() { ["x"] = "1" }));
        Assert.NotEqual(
            Fingerprint(query: new() { ["x"] = "1" }),
            Fingerprint(query: new() { ["x"] = "2" })
        );
        Assert.Equal(
            Fingerprint(query: new() { ["a"] = "1", ["b"] = "2" }),
            Fingerprint(query: new() { ["b"] = "2", ["a"] = "1" })
        );
    }

    [Fact]
    public void Json_key_order_and_whitespace_do_not_matter() =>
        Assert.Equal(
            Fingerprint("""{"a":1,"b":{"x":true,"y":[1,2]}}"""),
            Fingerprint(""" { "b" : { "y" : [ 1, 2 ], "x" : true }, "a" : 1 } """)
        );

    [Fact]
    public void Values_and_array_order_matter()
    {
        Assert.NotEqual(Fingerprint("""{"a":1}"""), Fingerprint("""{"a":2}"""));
        Assert.NotEqual(Fingerprint("""{"a":[1,2]}"""), Fingerprint("""{"a":[2,1]}"""));
        Assert.NotEqual(Fingerprint("""{"a":"1"}"""), Fingerprint("""{"a":1}"""));
    }

    [Fact]
    public void Number_spelling_matters_on_purpose() =>
        Assert.NotEqual(Fingerprint("""{"a":10}"""), Fingerprint("""{"a":10.0}"""));

    [Theory]
    [InlineData("""{"a":1,"a":2}""")]
    [InlineData("""{"outer":{"b":1,"b":1}}""")]
    [InlineData("""[{"c":1,"c":2}]""")]
    [InlineData("""{"amount":1,"Amount":2}""")]
    [InlineData("""{"Amount":2,"amount":1}""")]
    [InlineData("""{"outer":{"key":1,"KEY":1}}""")]
    public void Duplicate_property_names_are_rejected(string body)
    {
        var error = Assert.Throws<RequestFingerprintException>(() => Fingerprint(body));
        Assert.Equal(IdempotencyErrorCodes.DuplicateJsonProperty, error.Code);
    }

    [Fact]
    public void Invalid_json_is_rejected()
    {
        var error = Assert.Throws<RequestFingerprintException>(() => Fingerprint("{"));
        Assert.Equal(IdempotencyErrorCodes.InvalidJson, error.Code);
    }

    [Fact]
    public void Empty_and_non_json_bodies_are_fingerprinted_as_is()
    {
        Assert.NotEqual(Fingerprint(""), Fingerprint("{}"));
        Assert.Equal(
            Fingerprint("raw", contentType: "text/plain"),
            Fingerprint("raw", contentType: "text/plain")
        );
        Assert.NotEqual(
            Fingerprint("raw-1", contentType: "text/plain"),
            Fingerprint("raw-2", contentType: "text/plain")
        );
    }

    [Fact]
    public void Fingerprint_is_32_bytes() =>
        Assert.Equal(IdempotencyRecord.FingerprintLength, Fingerprint().Length);
}
