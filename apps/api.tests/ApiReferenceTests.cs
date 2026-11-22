using System.Net;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ApiReferenceTests(ApiFactory factory)
{
    [Fact]
    public async Task Development_serves_the_scalar_ui_and_the_openapi_document()
    {
        var client = factory.CreateClient();

        var ui = await client.GetAsync("/scalar");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Contains("text/html", ui.Content.Headers.ContentType!.ToString());

        var document = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
    }
}
