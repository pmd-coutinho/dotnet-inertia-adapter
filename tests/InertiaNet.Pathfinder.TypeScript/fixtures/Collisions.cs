using InertiaNet.Core;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FirstRoot.Controllers
{
    [Route("collision/first")] public class DuplicateController { [HttpGet] public object Index() => null!; }
}
namespace SecondRoot.Controllers
{
    [Route("collision/second")] public class DuplicateController { [HttpGet] public object Index() => null!; }
}
namespace Collisions.Controllers
{
    [Route("collision/index")] public class IndexController { [HttpGet] public object Show() => null!; }
    [Route("collision/object")] public class ObjectController
    {
        [HttpGet("helper", Name="collision.some-name")] public object QueryParams() => null!;
        [HttpGet("safe", Name="collision.some_name")] public object BuildUrl() => null!;
        [HttpGet("class", Name="collision.class")] public object Class() => null!;
        [HttpGet("class-method", Name="collision.classMethod")] public object ClassMethod() => null!;
        [HttpGet("case", Name="Collision.value")] public object Other() => null!;
        [HttpGet("root", Name="collision")] public object Root() => null!;
        [HttpGet("empty", Name="..empty-segment")] public object Empty() => null!;
        [HttpGet("/collision/comment*/\"quoted", Name="odd-name")] public object Comment() => null!;
        [HttpPost("prototype", Name="collision.__proto__")] public object Prototype([FromBody] Collisions.Contracts.PrototypeBody body) => null!;
    }
    [Route("collision/foo")] public class FooController { [HttpGet] public object Show() => null!; }
}
namespace Collisions.Controllers.Foo
{
    [Route("collision/bar")] public class BarController { [HttpGet] public object Show() => null!; }
}
namespace Collisions.Contracts
{
    public class Foo { public int Id { get; set; } }
    public class FooValidation { public int Value { get; set; } }
    public class PropsFor { public int Value { get; set; } }
    public enum PrototypeEnum { __proto__ = 7 }
    public class PrototypeBody { [Required, JsonPropertyName("__proto__")] public string Name { get; set; } = ""; }
    public class PrototypeBodyInputValidation { public int Value { get; set; } }
    public class Page
    {
        void Index(IInertiaService inertia) { inertia.Render("Collision/Models", new { First = new Foo(), Second = new FooValidation(), Reserved = new PropsFor(), ValidationName = new PrototypeBodyInputValidation() }); }
    }
}
