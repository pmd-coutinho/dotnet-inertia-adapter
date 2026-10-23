using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Pathfinder.Analysis;
using InertiaNet.Pathfinder.Generation;
using InertiaNet.Pathfinder.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace InertiaNet.Pathfinder.Tests.Contracts;

public class SemanticContractTests
{
    private const string Source = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using InertiaNet.Core;
        namespace One { public record Post(int Id, string Title); public enum State { Ready, Done } }
        namespace Two { public record Post(string Slug); public enum State { Empty, Filled } }
        namespace Demo
        {
            public class Base { public string URLValue { get; set; } = "uri"; }
            public class Envelope<T> { public T Data { get; set; } = default!; }
            public partial class Props : Base
            {
                [JsonPropertyName("display-name")] public string Title { get; set; } = "Example";
                [JsonIgnore] public string Hidden => throw new Exception("must not read");
                public static string Static => "not serialized";
                public string? Omitted { get; set; }
                [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? PresentNull { get; set; }
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Default { get; set; }
                [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public int Count { get; set; } = 42;
                public One.Post Post { get; set; } = new(1, "First");
                public Two.Post Other { get; set; } = new("second");
                public List<One.Post?> Posts { get; set; } = [new(1, "First"), null];
                public Envelope<One.Post> Wrapped { get; set; } = new() { Data = new(2, "Wrapped") };
                public One.State FirstState { get; set; }
                public Two.State SecondState { get; set; }
                [JsonInclude] private int PrivateField = 7;
                public int ExcludedField = 8;
                [JsonExtensionData] public Dictionary<string, JsonElement> Extension { get; set; } = new() { ["extraFlag"] = JsonSerializer.SerializeToElement(true) };
            }
            public partial class Props { public int Extra { get; set; } = 3; }
            public class Page
            {
                public static readonly InertiaComponent<Props> Component = new("Posts/Index");
                public void Render(IInertiaService inertia) { inertia.Render(Component, new Props()); }
            }
        }
        """;

    [Fact]
    public async Task OutputContracts_MatchActualAdapterSerialization_AndPreserveSymbolIdentity()
    {
        var (compilation, contracts) = Discover(Source);
        PathfinderDiagnostics.Entries.Should().BeEmpty();
        contracts.Pages.Should().ContainSingle();
        var page = contracts.Pages.Single();
        page.ComponentName.Should().Be("Posts/Index");
        page.Props.Should().Contain(field => field.Name == "urlValue" && field.TypeScriptType == "string");
        page.Props.Should().Contain(field => field.Name == "display-name" && !field.IsOptional);
        page.Props.Should().Contain(field => field.Name == "omitted" && field.IsOptional && field.TypeScriptType == "string");
        page.Props.Should().Contain(field => field.Name == "presentNull" && !field.IsOptional && field.TypeScriptType == "string | null");
        page.Props.Should().Contain(field => field.Name == "count" && field.TypeScriptType == "string");
        page.Props.Should().Contain(field => field.Name == "privateField");
        page.Props.Should().NotContain(field => new[] { "hidden", "static", "excludedField", "extension" }.Contains(field.Name));
        page.HasExtensionData.Should().BeTrue();
        var first = contracts.Models.Single(model => model.FullName == "global::One.Post");
        var second = contracts.Models.Single(model => model.FullName == "global::Two.Post");
        first.ShortName.Should().NotBe(second.ShortName);
        first.Properties.Select(property => property.Name).Should().BeEquivalentTo("id", "title");
        second.Properties.Select(property => property.Name).Should().Equal("slug");
        page.Props.Single(field => field.Name == "posts").TypeScriptType.Should().Be($"({first.ShortName} | null)[]");
        var envelope = contracts.Models.Single(model => model.FullName.Contains("Demo.Envelope<", StringComparison.Ordinal));
        envelope.Properties.Single().TypeScriptType.Should().Be(first.ShortName);
        contracts.Enums.Select(item => item.ShortName).Should().OnlyHaveUniqueItems();

        using var emitted = new MemoryStream();
        var result = compilation.Emit(emitted);
        result.Success.Should().BeTrue(string.Join('\n', result.Diagnostics));
        emitted.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(emitted);
        var value = Activator.CreateInstance(assembly.GetType("Demo.Props")!)!;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInertia();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Path = "/posts";
        context.Request.Headers["X-Inertia"] = "true";
        context.Response.Body = new MemoryStream();
        await scope.ServiceProvider.GetRequiredService<IInertiaService>().Render("Posts/Index", value).ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        var props = json.RootElement.GetProperty("props");
        var expected = JsonSerializer.SerializeToElement(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        props.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(expected.EnumerateObject().Select(property => property.Name));
        props.GetProperty("count").GetString().Should().Be("42");
        props.GetProperty("presentNull").ValueKind.Should().Be(JsonValueKind.Null);
        props.GetProperty("post").GetProperty("title").GetString().Should().Be("First");
        props.GetProperty("posts")[1].ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void GeneratedFiles_UseUniqueModelsImportsAndPageMap_WithStableNamesAcrossSourceOrder()
    {
        var (_, contracts) = Discover(Source);
        using var project = new TemporaryProject();
        TypeScriptGenerator.Generate([], contracts.Enums, contracts.Pages, contracts.Models,
            new() { ProjectPath = project.Root, OutputPath = project.PathFor("output") }, contracts.SharedProps);
        var page = File.ReadAllText(project.PathFor("output/types/Posts.Index.ts"));
        page.Should().Contain("\"display-name\": string").And.Contain("presentNull: string | null").And.Contain("[key: string]: unknown");
        foreach (var model in contracts.Models.Where(model => model.FullName.EndsWith(".Post", StringComparison.Ordinal)))
            page.Should().Contain($"import type {{ {model.ShortName} }}");
        File.ReadAllText(project.PathFor("output/types/page-map.ts")).Should().Contain("\"Posts/Index\": PostsIndexProps & SharedProps");
        File.ReadAllText(project.PathFor("output/types/shared.ts")).Should().Contain("interface PageProps extends SharedProps");
        var split = Source.Split("namespace Demo", 2);
        var first = CSharpSyntaxTree.ParseText(split[0], path: "Types.cs");
        var second = CSharpSyntaxTree.ParseText("using InertiaNet.Core;\nusing System;\nusing System.Collections.Generic;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n#nullable enable\nnamespace Demo" + split[1], path: "Page.cs");
        var reverse = ProjectCompilationLoader.CreateForSources([second, first]);
        var generated = new SemanticContractDiscoverer(reverse, new()).Discover([second, first], []);
        generated.Models.Select(model => model.ShortName).Should().BeEquivalentTo(contracts.Models.Select(model => model.ShortName));
    }

    [Fact]
    public void RequestContracts_SeparateJsonQueryAndFormNames_AndExposeSupportedValidationHints()
    {
        var source = """
            #nullable enable
            using InertiaNet.Core;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.AspNetCore.Http;
            using System.ComponentModel.DataAnnotations;
            using System.Text.Json.Serialization;
            class Input
            {
                [JsonPropertyName("wire-title"), Required, StringLength(80, MinimumLength=2)]
                public string Title { get; set; } = "";
                [Range(1, 10)] public int Count { get; set; }
                [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public int StringOnOutput { get; set; }
                [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Flexible { get; set; }
                [Required] public string? RequiredNullable { get; set; }
                [JsonIgnore] public string Hidden { get; set; } = "";
                public IFormFile? Upload { get; set; }
            }
            class SearchInput
            {
                [FromQuery(Name="term"), Required] public string Title { get; set; } = "";
                [JsonIgnore] public string Hidden { get; set; } = "";
                [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public int StringOnOutput { get; set; }
            }
            class UploadInput
            {
                [FromForm(Name="title-field"), Required] public string Title { get; set; } = "";
                [JsonIgnore] public string Hidden { get; set; } = "";
                public IFormFile? Upload { get; set; }
            }
            [Route("items")] class ItemsController : Controller
            {
                [HttpPost("json")] public object Store([FromBody] Input input) => null!;
                [HttpGet("search")] public object Search([FromQuery] SearchInput query) => null!;
                [HttpPost("upload")] public object Upload([FromForm] UploadInput input) => null!;
                public void Page(IInertiaService inertia) { inertia.Render("Numbers", new Input()); }
            }
            """;
        var (compilation, contracts) = Discover(source, routes: true);
        var json = contracts.Models.Single(model => model.Usage == "input");
        var query = contracts.Models.Single(model => model.Usage == "query");
        var form = contracts.Models.Single(model => model.Usage == "form");
        json.Properties.Should().Contain(property => property.Name == "wire-title" && property.IsOptional == false).And.NotContain(property => property.Name == "hidden");
        query.Properties.Should().Contain(property => property.Name == "term").And.Contain(property => property.Name == "hidden");
        form.Properties.Should().Contain(property => property.Name == "title-field").And.Contain(property => property.Name == "upload" && property.TypeScriptType.Contains("File | Blob"));
        json.Properties.Single(property => property.Name == "wire-title").Validation.Should().Contain("required", true).And.Contain("minLength", 2).And.Contain("maxLength", 80);
        json.Properties.Single(property => property.Name == "count").IsOptional.Should().BeTrue();
        json.Properties.Single(property => property.Name == "stringOnOutput").TypeScriptType.Should().Be("number");
        json.Properties.Single(property => property.Name == "flexible").TypeScriptType.Should().Be("number | string");
        json.Properties.Single(property => property.Name == "requiredNullable").TypeScriptType.Should().Be("string");
        query.Properties.Single(property => property.Name == "stringOnOutput").TypeScriptType.Should().Be("number");
        contracts.Pages.Single().Props.Single(property => property.Name == "stringOnOutput").TypeScriptType.Should().Be("string");
        contracts.Routes.Single(route => route.ActionName == "Store").BodyTypeScriptType.Should().Be(json.ShortName);
        contracts.Routes.Single(route => route.ActionName == "Search").QueryTypeScriptType.Should().Be(query.ShortName);
        contracts.Routes.Single(route => route.ActionName == "Upload").FormTypeScriptType.Should().Be(form.ShortName);
    }

    [Fact]
    public void AnonymousAndDictionaryProps_InferFactoriesDotPathsAndExplicitNulls()
    {
        var (_, contracts) = Discover("""
            #nullable enable
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using InertiaNet.Core;
            class Page
            {
                void Index(IInertiaService inertia)
                {
                    var count = 3;
                    inertia.Render("Anonymous", new { count, deferred = inertia.Defer((_, _) => Task.FromResult<object?>(42)) });
                    inertia.Render("Dictionary", new Dictionary<string, object?> { ["stats.count"] = count, ["empty"] = null });
                }
            }
            """);
        var anonymous = contracts.Pages.Single(page => page.ComponentName == "Anonymous");
        anonymous.Props.Should().Contain(field => field.Name == "count" && field.TypeScriptType == "number");
        anonymous.Props.Should().Contain(field => field.Name == "deferred" && field.TypeScriptType == "number" && field.IsOptional && field.IsDeferred);
        var dictionary = contracts.Pages.Single(page => page.ComponentName == "Dictionary");
        dictionary.Props.Should().Contain(field => field.Name == "stats" && field.TypeScriptType.Contains("\"count\": number"));
        dictionary.Props.Should().Contain(field => field.Name == "empty" && field.TypeScriptType == "null" && !field.IsOptional);
        dictionary.Props.Should().NotContain(field => field.Name == "comparer" || field.Name == "keys" || field.Name == "count");
    }

    [Fact]
    public void ConstructedGenericNullability_IsPartOfContractIdentity()
    {
        var (_, contracts) = Discover("""
            #nullable enable
            using InertiaNet.Core;
            class Box<T> { public T Value { get; set; } = default!; }
            class Item { public int Id { get; set; } }
            class Props { public Box<Item> First { get; set; } = new(); public Box<Item?> Second { get; set; } = new(); }
            class Page { void Index(IInertiaService inertia) { inertia.Render("Generics", new Props()); } }
            """);
        var boxes = contracts.Models.Where(model => model.FullName.Contains("Box<", StringComparison.Ordinal)).ToArray();
        boxes.Should().HaveCount(2);
        boxes.Select(model => model.ShortName).Should().OnlyHaveUniqueItems();
        boxes.Select(model => model.Properties.Single().IsOptional).Should().BeEquivalentTo(new bool?[] { false, true });
    }

    [Fact]
    public void NestedFactoryWrappersAndScrollModifiers_RetainOmissionMetadata_AndRecursiveDelegatesAreDiagnosed()
    {
        var (_, contracts) = Discover("""
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using System.Collections.Generic;
            using InertiaNet.Core;
            using InertiaNet.Props;
            delegate Again Again();
            class Props
            {
                [InertiaPropType(typeof(int))] public Func<Task<DeferProp>> Stats { get; set; } = default!;
                public Again Recursive { get; set; } = default!;
            }
            class Page
            {
                void Index(IInertiaService inertia)
                {
                    inertia.Render("Factories", new Props());
                    inertia.Render("Scroll", new { Rows = inertia.Scroll(new[] { 1 }).Defer("slow") });
                    inertia.Render("DictionaryScroll", new Dictionary<string, object> { ["rows"] = inertia.Scroll(new[] { 1 }).Defer("slow") });
                }
            }
            """);
        var fields = contracts.Pages.Single(page => page.ComponentName == "Factories").Props;
        fields.Single(field => field.Name == "stats").IsOptional.Should().BeTrue();
        fields.Single(field => field.Name == "stats").IsDeferred.Should().BeTrue();
        fields.Single(field => field.Name == "recursive").TypeScriptType.Should().Be("unknown");
        contracts.Pages.Single(page => page.ComponentName == "Scroll").Props.Single().IsOptional.Should().BeTrue();
        contracts.Pages.Single(page => page.ComponentName == "Scroll").Props.Single().TypeScriptType.Should().Be("number[]");
        contracts.Pages.Single(page => page.ComponentName == "DictionaryScroll").Props.Single().IsOptional.Should().BeTrue();
        contracts.Pages.Single(page => page.ComponentName == "DictionaryScroll").Props.Single().TypeScriptType.Should().Be("number[]");
        PathfinderDiagnostics.Entries.Should().Contain(entry => entry.Code == "PATHFINDER221");
    }

    [Fact]
    public void ExplicitPropHints_AreNonNullableUnlessTheHintIsANullableValueType()
    {
        var (_, contracts) = Discover("""
            #nullable enable
            using InertiaNet.Core;
            using InertiaNet.Props;
            class Child { public int Id { get; set; } }
            class Props
            {
                [InertiaPropType(typeof(Child))] public required ScrollProp Rows { get; init; }
                [InertiaPropType(typeof(int?))] public required DeferProp Count { get; init; }
            }
            class Page { void Index(IInertiaService inertia) { inertia.Render<Props>("Hints", default!); } }
            """);
        var fields = contracts.Pages.Single().Props;
        fields.Single(field => field.Name == "rows").TypeScriptType.Should().Be("Child");
        fields.Single(field => field.Name == "rows").IsOptional.Should().BeFalse();
        fields.Single(field => field.Name == "count").TypeScriptType.Should().Be("number | null");
        fields.Single(field => field.Name == "count").IsOptional.Should().BeTrue();
        PathfinderDiagnostics.Entries.Should().BeEmpty();
    }

    [Fact]
    public void MinimalBinding_InferScalarQueries_AndDiagnoseAmbiguousBodyServiceAndGroupedParameters()
    {
        PathfinderDiagnostics.Clear();
        var tree = CSharpSyntaxTree.ParseText("""
            #nullable enable
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;
            var app = WebApplication.Create();
            app.MapGet("/search", (string? term, int? count, [FromServices] Repository repository) => "ok");
            app.MapPost("/implicit", (Input input) => "ok");
            app.MapGet("/grouped", ([AsParameters] Search search) => "ok");
            class Repository { }
            class Input { public string Title { get; set; } = ""; }
            class Search { public string? Term { get; set; } }
            """, path: "Bindings.cs");
        var compilation = ProjectCompilationLoader.CreateForSources([tree]);
        var routes = MinimalApiRouteDiscoverer.Discover(tree, compilation.GetSemanticModel(tree));
        var contracts = new SemanticContractDiscoverer(compilation, new()).Discover([tree], routes);
        var query = contracts.Routes.Single(route => route.UrlTemplate == "/search").QueryTypeScriptType;
        query.Should().Contain("\"term\"?: string | null").And.Contain("\"count\"?: number | null").And.NotContain("repository");
        contracts.Routes.Single(route => route.UrlTemplate == "/implicit").BodyTypeScriptType.Should().BeNull();
        contracts.Routes.Single(route => route.UrlTemplate == "/grouped").QueryTypeScriptType.Should().BeNull();
        PathfinderDiagnostics.Entries.Where(entry => entry.Code == "PATHFINDER229").Should().HaveCount(2);
    }

    [Fact]
    public void DomainTypesAndAttributes_WithFrameworkNames_KeepTheirActualSymbolContracts()
    {
        var (_, contracts) = Discover("""
            #nullable enable
            using System;
            using InertiaNet.Core;
            namespace Domain
            {
                class JsonIgnoreAttribute : Attribute { }
                class Task<T> { public T Value { get; set; } = default!; }
                class JsonElement { public string Text { get; set; } = ""; }
                class IFormFile { public string Name { get; set; } = ""; }
                class DeferProp { public int Id { get; set; } }
                class Props
                {
                    [JsonIgnore] public string Visible { get; set; } = "visible";
                    public Task<int> Work { get; set; } = new();
                    public JsonElement Data { get; set; } = new();
                    public IFormFile File { get; set; } = new();
                    public DeferProp Custom { get; set; } = new();
                }
                class Page
                {
                    void Index(IInertiaService inertia) { inertia.Render("Domain", new Props()); }
                }
            }
            """);
        var fields = contracts.Pages.Single().Props;
        fields.Should().Contain(field => field.Name == "visible" && field.TypeScriptType == "string");
        fields.Single(field => field.Name == "custom").IsOptional.Should().BeFalse();
        contracts.Models.Should().HaveCount(4);
        contracts.Models.Should().Contain(model => model.FullName.StartsWith("global::Domain.Task<", StringComparison.Ordinal));
        fields.Should().NotContain(field => field.TypeScriptType == "unknown");
        PathfinderDiagnostics.Entries.Should().BeEmpty();
    }

    [Fact]
    public void CustomConverters_ProduceUnknownAndDiagnostics_InsteadOfInventedDtoShapes()
    {
        var (_, contracts) = Discover("""
            using InertiaNet.Core;
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            [JsonConverter(typeof(TokenConverter))] class Token { public string Value { get; set; } }
            class TokenConverter : JsonConverter<Token> { public override Token Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => throw new Exception(); public override void Write(Utf8JsonWriter w, Token v, JsonSerializerOptions o) => w.WriteStringValue(v.Value); }
            class Props { public Token Value { get; set; } }
            class Page { void Index(IInertiaService inertia) { inertia.Render("Custom", new Props()); } }
            """);
        contracts.Pages.Single().Props.Single().TypeScriptType.Should().Be("unknown");
        contracts.Models.Should().BeEmpty();
        PathfinderDiagnostics.Entries.Should().Contain(entry => entry.Code == "PATHFINDER223");
    }

    private static (CSharpCompilation Compilation, SemanticContracts Contracts) Discover(string source, bool routes = false)
    {
        PathfinderDiagnostics.Clear();
        var tree = CSharpSyntaxTree.ParseText(source, path: "Contracts.cs");
        var compilation = ProjectCompilationLoader.CreateForSources([tree]);
        var discovered = routes ? ControllerRouteDiscoverer.Discover(tree) : [];
        return (compilation, new SemanticContractDiscoverer(compilation, new()).Discover([tree], discovered));
    }
}
