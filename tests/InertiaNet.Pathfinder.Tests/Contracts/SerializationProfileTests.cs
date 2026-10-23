using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using InertiaNet.Pathfinder.Analysis;
using InertiaNet.Pathfinder.Tests.TestHelpers;
using Microsoft.CodeAnalysis.CSharp;

namespace InertiaNet.Pathfinder.Tests.Contracts;

public class SerializationProfileTests
{
    private const string Source = """
        #nullable enable
        using InertiaNet.Core;
        using Microsoft.AspNetCore.Mvc;
        using System.Text.Json.Serialization;
        public enum State { InProgress, Done }
        public class Payload
        {
            public string DisplayName { get; set; } = "example";
            public string? Empty { get; set; }
            public State State { get; set; }
            public int Count { get; set; } = 42;
            [JsonNumberHandling(JsonNumberHandling.Strict)] public int Strict { get; set; }
            [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public int[] Counts { get; set; } = [1, 2];
        }
        [Route("profiles")] public class ProfilesController : Controller
        {
            [HttpPost] public object Store([FromBody] Payload payload) => payload;
            [HttpGet("{state}")] public object Query(State state, [FromQuery] Payload payload) => payload;
            public void Page(IInertiaService inertia) { inertia.Render("Profiles", new Payload()); }
        }
        """;

    [Fact]
    public void OutputInputAndRouteEnums_UseIndependentWireProfiles_ThatAgreeWithSerializers()
    {
        PathfinderDiagnostics.Clear();
        using var project = new TemporaryProject();
        var profiles = ContractConfiguration.Load(project.File("contracts.json", """
            {
              "output": { "naming": "snake_case", "ignoreCondition": "Never", "stringEnums": true,
                          "enumNaming": "kebab-case", "enumAllowIntegers": false },
              "input": { "naming": "preserve" }
            }
            """));
        var tree = CSharpSyntaxTree.ParseText(Source, path: "Profiles.cs");
        var compilation = ProjectCompilationLoader.CreateForSources([tree]);
        var contracts = new SemanticContractDiscoverer(compilation, new() { ContractFile = "contracts.json" }, profiles.Output, profiles.Input)
            .Discover([tree], ControllerRouteDiscoverer.Discover(tree));
        PathfinderDiagnostics.Entries.Should().BeEmpty();
        var page = contracts.Pages.Single();
        page.Props.Should().Contain(field => field.Name == "display_name");
        page.Props.Single(field => field.Name == "empty").IsOptional.Should().BeFalse();
        page.Props.Single(field => field.Name == "empty").TypeScriptType.Should().Be("string | null");
        page.Props.Single(field => field.Name == "counts").TypeScriptType.Should().Be("string[]");
        var input = contracts.Models.Single(model => model.Usage == "input");
        input.Properties.Should().Contain(field => field.Name == "DisplayName");
        input.Properties.Single(field => field.Name == "Count").TypeScriptType.Should().Be("number | string");
        input.Properties.Single(field => field.Name == "Strict").TypeScriptType.Should().Be("number");
        input.Properties.Single(field => field.Name == "Counts").TypeScriptType.Should().Be("number[]");
        var outputEnum = contracts.Enums.Single(item => item.ShortName == page.Props.Single(field => field.Name == "state").TypeScriptType);
        outputEnum.StringValues.Should().BeTrue();
        outputEnum.Members.First().Value.Should().Be("in-progress");
        outputEnum.WireType.Should().BeNull();
        var inputEnum = contracts.Enums.Single(item => item.ShortName == input.Properties.Single(field => field.Name == "State").TypeScriptType);
        inputEnum.StringValues.Should().BeFalse();
        inputEnum.WireType.Should().Be("number");
        var routeEnum = contracts.Enums.Single(item => item.ShortName == contracts.Routes.Single(route => route.ActionName == "Query").Parameters.Single().TypeScriptType);
        routeEnum.WireType.Should().Be("string | number");
        var query = contracts.Models.Single(model => model.Usage == "query");
        query.Properties.Should().Contain(field => field.Name == "displayName");
        query.Properties.Single(field => field.Name == "count").TypeScriptType.Should().Be("number");

        using var stream = new MemoryStream();
        var emitted = compilation.WithAssemblyName("Profiles_" + Guid.NewGuid().ToString("N")).Emit(stream);
        emitted.Success.Should().BeTrue(string.Join('\n', emitted.Diagnostics));
        stream.Position = 0;
        var type = AssemblyLoadContext.Default.LoadFromStream(stream).GetType("Payload")!;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false));
        var actual = JsonSerializer.SerializeToElement(Activator.CreateInstance(type), type, options);
        actual.GetProperty("state").GetString().Should().Be(outputEnum.Members.First().Value);
        actual.GetProperty("empty").ValueKind.Should().Be(JsonValueKind.Null);
        actual.GetProperty("counts")[0].GetString().Should().Be("1");
        var inputOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null };
        var received = JsonSerializer.Deserialize("""{"DisplayName":"input", "State":1, "Count":"12", "Strict":3, "Counts":[4]}""", type, inputOptions)!;
        type.GetProperty("Count")!.GetValue(received).Should().Be(12);
        Action invalid = () => JsonSerializer.Deserialize("""{"Strict":"3"}""", type, inputOptions);
        invalid.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("Number", "number")]
    [InlineData("String", "string")]
    [InlineData("BigInt", "number | bigint")]
    public void LargeIntegerPolicies_ApplyToInt64UInt64BigIntegerAndEnums_WithoutChangingOtherNumbers(string policyName, string wireType)
    {
        var policy = Enum.Parse<LargeIntegerHandling>(policyName);
        PathfinderDiagnostics.Clear();
        var tree = CSharpSyntaxTree.ParseText("""
            #nullable enable
            using InertiaNet.Core;
            using System.Numerics;
            using System.Text.Json.Serialization;
            enum Large : ulong { Safe = 42, Unsafe = ulong.MaxValue }
            class Props
            {
                public long Id { get; set; }
                public ulong Unsigned { get; set; }
                public BigInteger Huge { get; set; }
                public int Count { get; set; }
                public decimal Price { get; set; }
                public long? Optional { get; set; }
                public Large Code { get; set; }
                [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long Text { get; set; }
            }
            class Page { void Index(IInertiaService inertia) { inertia.Render("Large", new Props()); } }
            """, path: "Large.cs");
        var compilation = ProjectCompilationLoader.CreateForSources([tree]);
        var contracts = new SemanticContractDiscoverer(compilation, new() { ContractFile = "explicit" }, new() { LargeIntegers = policy }).Discover([tree], []);
        var fields = contracts.Pages.Single().Props;
        foreach (var name in new[] { "id", "unsigned", "huge", "optional" }) fields.Single(field => field.Name == name).TypeScriptType.Should().Be(wireType);
        fields.Single(field => field.Name == "optional").IsOptional.Should().BeTrue();
        fields.Single(field => field.Name == "count").TypeScriptType.Should().Be("number");
        fields.Single(field => field.Name == "price").TypeScriptType.Should().Be("number");
        fields.Single(field => field.Name == "text").TypeScriptType.Should().Be("string");
        var large = contracts.Enums.Single();
        large.WireType.Should().Be(wireType);
        large.Members.Single(member => member.Name == "Unsafe").BigInt.Should().Be(policy == LargeIntegerHandling.BigInt);
        large.StringValues.Should().Be(policy == LargeIntegerHandling.String);
        PathfinderDiagnostics.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{\"output\":{\"namng\":\"preserve\"}}")]
    [InlineData("{\"input\":{\"naming\":\"invented\"}}")]
    [InlineData("{\"output\":{\"typeOverrides\":{\"Token\":\"string; console.log(1)\"}}}")]
    public void InvalidProfiles_FailInsteadOfSilentlyChangingContracts(string json)
    {
        using var project = new TemporaryProject();
        Action load = () => ContractConfiguration.Load(project.File("invalid.json", json));
        load.Should().Throw<Exception>();
    }

    [Fact]
    public void BigIntegerRequests_NeedAnExplicitConverterContract_WhileOutputUsesTheAdapterPolicy()
    {
        PathfinderDiagnostics.Clear();
        var tree = CSharpSyntaxTree.ParseText("""
            using System.Numerics;
            using Microsoft.AspNetCore.Mvc;
            [Route("huge")] class HugeController { [HttpPost] public object Store([FromBody] BigInteger value) => value; }
            """, path: "Huge.cs");
        var compilation = ProjectCompilationLoader.CreateForSources([tree]);
        var routes = ControllerRouteDiscoverer.Discover(tree);
        var inferred = new SemanticContractDiscoverer(compilation, new()).Discover([tree], routes);
        inferred.Routes.Single().BodyTypeScriptType.Should().Be("unknown");
        PathfinderDiagnostics.Entries.Should().Contain(entry => entry.Code == "PATHFINDER223");
        PathfinderDiagnostics.Clear();
        var input = new SerializationProfile { TypeOverrides = new() { ["System.Numerics.BigInteger"] = "string" } };
        var configured = new SemanticContractDiscoverer(compilation, new() { ContractFile = "explicit" }, inputProfile: input).Discover([tree], routes);
        configured.Routes.Single().BodyTypeScriptType.Should().Be("string");
        PathfinderDiagnostics.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ExplicitConverterOverrides_AreScopedToTheirProfile()
    {
        PathfinderDiagnostics.Clear();
        using var project = new TemporaryProject();
        var configuration = ContractConfiguration.Load(project.File("contracts.json", """{"output":{"typeOverrides":{"Token":"string"}}}"""));
        var tree = CSharpSyntaxTree.ParseText("""
            using InertiaNet.Core;
            using System.Text.Json.Serialization;
            [JsonConverter(typeof(UnknownConverter))] class Token { public string Value { get; set; } }
            class Page { void Index(IInertiaService inertia) { inertia.Render("Token", new { Value = new Token() }); } }
            """, path: "Token.cs");
        var compilation = ProjectCompilationLoader.CreateForSources([tree]);
        var contracts = new SemanticContractDiscoverer(compilation, new() { ContractFile = "contracts.json" }, configuration.Output, configuration.Input).Discover([tree], []);
        contracts.Pages.Single().Props.Single().TypeScriptType.Should().Be("string");
        contracts.Models.Should().BeEmpty();
        PathfinderDiagnostics.Entries.Should().BeEmpty();
    }
}
