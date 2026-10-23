using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

app.MapInertia("/about", "About").WithName("About");
app.MapMethods("/method", ["HEAD", "OPTIONS"], () => "method").WithName("Methods");

namespace Demo.Controllers
{
    [Route("items")]
    public class ItemsController : Controller
    {
        [HttpGet("{id:int?}", Name = "Items.Optional")] public object Optional(int? id = null) => null;
        [HttpGet("{id:int=1}", Name = "Items.Default")] public object Default(int id = 1) => null;
        [HttpGet("files/{**path}", Name = "Items.CatchAll")] public object CatchAll(string? path = null) => null;
        [HttpGet("single/{*path}", Name = "Items.SingleCatchAll")] public object SingleCatchAll(string? path = null) => null;
        [HttpGet("unconstrained/{id}", Name = "Items.Unconstrained")] public object Unconstrained(int id) => null;
        [HttpGet("locale/{locale}/posts/{id:int}", Name = "Items.Locale")] public object Locale(int id) => null;
        [HttpGet("alias/{id:int}", Name = "Items.Alias")] public object Alias([FromRoute(Name = "id")] int value) => null;
        [HttpGet("flag/{enabled:bool}", Name = "Items.Flag")] public object Flag(bool enabled) => null;
        [HttpGet("slug/{slug}", Name = "Items.Slug")] public object Slug(string slug) => null;
        [HttpGet("file/{name}.{extension?}", Name = "Items.File")] public object File(string name, string? extension = null) => null;
        [HttpGet("show/{id:int}/{slug?}", Name = "Items.Show")] public object Show(int id, string? slug = null) => null;
        [HttpPut("{id:int}", Name = "Items.Update"), HttpPatch("{id:int}")] public object Update(int id) => null;
        [HttpDelete("{id:int}", Name = "Items.Delete")] public object Delete(int id) => null;
        [HttpGet("~/absolute", Name = "Items.Absolute")] public object Absolute() => null;
        [HttpPost("payload", Name = "Items.Store")] public object Store([FromBody] CreateRequest request) => null;
        [HttpGet("first/{id:int}"), HttpGet("second/{id:int}")] public object Multi(int id) => null;
        [HttpGet("state/{state}", Name = "Items.State")] public object State(Status state) => null;
    }
    public enum Status { Pending, Active, Archived = 5, Next, Negative = -1, Flag = 1 << 3, Combined = Archived | Flag }
    public class CreateRequest { public Status State { get; set; } public string Title { get; set; } }
    public class PageProps { public Dictionary<string, CreateRequest> Requests { get; set; } public Status Status { get; set; } }
    public class Pages { public void Index() { inertia.Render<PageProps>("Items/Index", new PageProps()); } }
}
