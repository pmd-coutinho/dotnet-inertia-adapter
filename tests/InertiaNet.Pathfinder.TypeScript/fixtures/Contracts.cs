#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace First { public record Item(int Id); public enum State { Ready, Done } }
namespace Second { public record Item(string Slug); public enum State { Empty, Filled } }
namespace ContractDemo
{
    public record SharedUser(int Id, string Name);
    public record Auth(SharedUser User);
    public class Envelope<T> { public T Data { get; init; } = default!; }
    public class PageProps
    {
        [JsonPropertyName("display-name")] public string Title { get; init; } = "Contract";
        [JsonIgnore] public string Hidden => "not sent";
        public string? Omitted { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? PresentNull { get; init; }
        public First.Item Item { get; init; } = new(1);
        public Second.Item Other { get; init; } = new("two");
        public List<First.Item?> Items { get; init; } = [new(1), null];
        public Envelope<First.Item> Box { get; init; } = new() { Data = new(1) };
        public First.State FirstState { get; init; }
        public Second.State SecondState { get; init; }
        public Func<Task<First.Item>> AsyncItem { get; init; } = () => Task.FromResult(new First.Item(3));
    }
    public class Request
    {
        [JsonPropertyName("wire-title"), Required, StringLength(80, MinimumLength = 2)]
        public string Title { get; init; } = "";
        [Range(1, 10)] public int Count { get; init; }
        [JsonIgnore] public string Hidden { get; init; } = "";
        public IFormFile? File { get; init; }
    }
    public class SearchRequest
    {
        [FromQuery(Name = "term"), Required] public string Title { get; init; } = "";
        [JsonIgnore] public string Hidden { get; init; } = "";
    }
    public class UploadRequest
    {
        [FromForm(Name = "title-field"), Required] public string Title { get; init; } = "";
        [JsonIgnore] public string Hidden { get; init; } = "";
        [FromForm(Name = "attachment")] public IFormFile? File { get; init; }
    }
    [Route("contracts")]
    public class ContractsController : Controller
    {
        [HttpPost("store", Name = "Contracts.Store")] public object Store([FromBody] Request input) => input;
        [HttpGet("search", Name = "Contracts.Search")] public object Search([FromQuery] SearchRequest input) => input;
        [HttpPost("upload", Name = "Contracts.Upload")] public object Upload([FromForm] UploadRequest input) => input;
        public void Page()
        {
            inertia.Share("auth", new Auth(new SharedUser(1, "Ana")));
            inertia.Render("Contract/Index", new PageProps());
        }
    }
}
