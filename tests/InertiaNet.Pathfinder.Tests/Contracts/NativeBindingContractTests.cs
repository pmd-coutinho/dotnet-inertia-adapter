using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using InertiaNet.Pathfinder.Analysis;
using InertiaNet.Pathfinder.Tests.Contracts.Native;
using InertiaNet.Pathfinder.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Pathfinder.Tests.Contracts;

public class NativeBindingContractTests
{
    [Fact]
    public async Task GeneratedAliases_AgreeWithRealMvcJsonQueryAndMultipartBinding()
    {
        var file = Path.Combine(TemporaryProject.RepositoryRoot, "tests/InertiaNet.Pathfinder.Tests/Contracts/NativeBindingFixtures.cs");
        var tree = CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(file), path: file);
        var compilation = ProjectCompilationLoader.CreateForSources([tree]);
        var contracts = new SemanticContractDiscoverer(compilation, new()).Discover([tree], ControllerRouteDiscoverer.Discover(tree));
        var json = contracts.Models.Single(model => model.Usage == "input");
        var query = contracts.Models.Single(model => model.Usage == "query");
        var form = contracts.Models.Single(model => model.Usage == "form");
        json.Properties.Single().Name.Should().Be("wire-title");
        query.Properties.Single().Name.Should().Be("term");
        form.Properties.Select(property => property.Name).Should().BeEquivalentTo("title-field", "attachment");
        contracts.Routes.Single(route => route.ActionName == "Prefixed").QueryTypeScriptType.Should().Be("{ \"filter\": " + query.ShortName + " }");
        contracts.Routes.Single(route => route.ActionName == "Tags").QueryTypeScriptType.Should().Be("{ \"tag\": string[] }");
        contracts.Routes.Single(route => route.ActionName == "File").FormTypeScriptType.Should().Be("{ \"upload\": File | Blob }");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(NativeBindingController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var body = new StringContent(JsonSerializer.Serialize(new Dictionary<string, string> { [json.Properties.Single().Name] = "JSON" }), Encoding.UTF8, "application/json");
        using var jsonResponse = await client.PostAsync("/native-contract-binding/json", body);
        jsonResponse.EnsureSuccessStatusCode();
        (await jsonResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["title"].Should().Be("JSON");
        using var queryResponse = await client.GetAsync("/native-contract-binding/query?" + query.Properties.Single().Name + "=Query");
        queryResponse.EnsureSuccessStatusCode();
        (await queryResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["title"].Should().Be("Query");
        using var prefixedResponse = await client.GetAsync("/native-contract-binding/prefixed?filter.term=Prefixed");
        prefixedResponse.EnsureSuccessStatusCode();
        (await prefixedResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["title"].Should().Be("Prefixed");
        using var tagsResponse = await client.GetAsync("/native-contract-binding/tags?tag=one&tag=two");
        tagsResponse.EnsureSuccessStatusCode();
        (await tagsResponse.Content.ReadFromJsonAsync<Dictionary<string, string[]>>())!["tags"].Should().Equal("one", "two");
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("Form"), form.Properties.Single(property => property.TypeScriptType == "string").Name);
        multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("file content")), form.Properties.Single(property => property.Name == "attachment").Name, "probe.txt");
        using var formResponse = await client.PostAsync("/native-contract-binding/form", multipart);
        formResponse.EnsureSuccessStatusCode();
        var received = await formResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        received!["title"].Should().Be("Form");
        received["file"].Should().Be("probe.txt");
        using var fileBody = new MultipartFormDataContent();
        fileBody.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("file")), "upload", "standalone.txt");
        using var fileResponse = await client.PostAsync("/native-contract-binding/file", fileBody);
        fileResponse.EnsureSuccessStatusCode();
        (await fileResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["file"].Should().Be("standalone.txt");
    }
}
