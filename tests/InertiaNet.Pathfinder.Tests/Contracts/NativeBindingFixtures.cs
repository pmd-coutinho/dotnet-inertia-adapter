using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace InertiaNet.Pathfinder.Tests.Contracts.Native;

public sealed class NativeJsonInput
{
    [Required, JsonPropertyName("wire-title")] public string Title { get; set; } = "";
}
public sealed class NativeQueryInput
{
    [Required, FromQuery(Name = "term")] public string Title { get; set; } = "";
}
public sealed class NativeFormInput
{
    [Required, FromForm(Name = "title-field")] public string Title { get; set; } = "";
    [FromForm(Name = "attachment")] public IFormFile? File { get; set; }
}
[ApiController, Route("native-contract-binding")]
public sealed class NativeBindingController : ControllerBase
{
    [HttpPost("json")] public object Store([FromBody] NativeJsonInput input) => new { input.Title };
    [HttpGet("query")] public object Search([FromQuery] NativeQueryInput input) => new { input.Title };
    [HttpGet("prefixed")] public object Prefixed([FromQuery(Name = "filter")] NativeQueryInput input) => new { input.Title };
    [HttpGet("tags")] public object Tags([FromQuery(Name = "tag")] string[] tags) => new { Tags = tags };
    [HttpPost("form")] public object Upload([FromForm] NativeFormInput input) => new { input.Title, File = input.File?.FileName };
    [HttpPost("file")] public object File([FromForm(Name = "upload")] IFormFile file) => new { File = file.FileName };
}
