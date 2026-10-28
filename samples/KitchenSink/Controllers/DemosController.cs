using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using InertiaNet.Core;
using InertiaNet.Props;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace KitchenSink.Controllers;

public sealed class ContactInput
{
    [Required, StringLength(80, MinimumLength = 2)] public string Name { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, RegularExpression("^(Design|Engineering|Operations)$")] public string Team { get; set; } = "Design";
}
public sealed class ProfileInput
{
    [Required, StringLength(40, MinimumLength = 2)] public string Name { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
}
public sealed class SignInInput { [Required, StringLength(40, MinimumLength = 2)] public string Name { get; set; } = ""; }
public sealed class UploadInput { [Required] public IFormFile? File { get; set; } }
public sealed record UploadReceipt(string Name, long Bytes, string ContentType);
public sealed record ContactsProps(ContactPage Contacts, string Search);
public sealed record Metric(string Label, string Value);
public sealed record FeedEntry(int Id, string Title, string Team);
public sealed record FeedPage(FeedEntry[] Data, int CurrentPage, int? NextPage);
public sealed class DemoRenderException : Exception;
public sealed class ErrorPageProps
{
    [InertiaPropType(typeof(string))] public required AlwaysProp Message { get; init; }
}
public sealed class MergingProps
{
    public int Batch { get; init; }
    [InertiaPropType(typeof(FeedEntry[]))] public required MergeProp Entries { get; init; }
    [InertiaPropType(typeof(Dictionary<string, object?>))] public required MergeProp Preferences { get; init; }
}

public sealed class DemosController(IInertiaService inertia, ContactRepository repository) : Controller
{
    [HttpGet("/", Name = "Demos.home")]
    public IActionResult Home() => inertia.Render("Home", new { contacts = repository.Page().Total });

    [HttpGet("/contacts", Name = "Demos.contacts")]
    public IActionResult Contacts([FromQuery] string search = "", [FromQuery] int page = 1)
        => inertia.Render(new InertiaComponent<ContactsProps>("Contacts"), new(repository.Page(search, page), search));

    [HttpPost("/contacts", Name = "Demos.storeContact")]
    public IActionResult StoreContact([FromBody] ContactInput input)
    {
        if (!ModelState.IsValid) return LocalRedirect("/contacts");
        repository.Add(input.Name, input.Email, input.Team);
        inertia.Flash("message", "Contact created. Saved to SQLite.");
        return LocalRedirect("/contacts");
    }

    [HttpPut("/contacts/{id:int}", Name = "Demos.updateContact")]
    public IActionResult UpdateContact(int id, [FromBody] ContactInput input)
    {
        if (!ModelState.IsValid) return LocalRedirect("/contacts");
        if (!repository.Update(id, input.Name, input.Email, input.Team)) return NotFound();
        inertia.Flash("message", "Contact updated.");
        return LocalRedirect("/contacts");
    }

    [HttpDelete("/contacts/{id:int}", Name = "Demos.deleteContact")]
    public IActionResult DeleteContact(int id)
    {
        if (!repository.Delete(id)) return NotFound();
        inertia.Flash("message", "Contact deleted.");
        return LocalRedirect("/contacts");
    }

    [HttpGet("/forms", Name = "Demos.forms")]
    public IActionResult Forms() => inertia.Render("Forms", new { description = "DataAnnotations power both submit-time validation and Precognition." });

    [HttpPost("/forms/profile", Name = "Demos.saveProfile")]
    public IActionResult SaveProfile([FromBody] ProfileInput input)
    {
        if (!ModelState.IsValid) return LocalRedirect("/forms");
        inertia.Flash("message", $"Profile accepted for {input.Name}. No account was created.");
        return LocalRedirect("/forms");
    }

    [HttpPost("/forms/upload", Name = "Demos.upload")]
    [RequestSizeLimit(2 * 1024 * 1024 + 16384)]
    public async Task<IActionResult> Upload([FromForm] UploadInput input, CancellationToken cancellationToken)
    {
        if (input.File is { Length: > 2 * 1024 * 1024 }) ModelState.AddModelError("file", "Choose a file smaller than 2 MB.");
        if (!ModelState.IsValid) return LocalRedirect("/forms");
        // Consume the stream, but do not persist or serve untrusted files.
        await input.File!.CopyToAsync(Stream.Null, cancellationToken);
        inertia.Flash("upload", new UploadReceipt(Path.GetFileName(input.File.FileName), input.File.Length, input.File.ContentType));
        inertia.Flash("message", "Upload received. The sample discards the file after reading it.");
        return LocalRedirect("/forms");
    }

    [HttpGet("/props", Name = "Demos.props")]
    public IActionResult Props([FromQuery] bool recover = false) => inertia.Render("Props", new
    {
        initial = DateTimeOffset.UtcNow.ToString("O"),
        optional = inertia.Optional(async (_, ct) => { await Task.Delay(250, ct); return new { message = "Fetched only when explicitly requested.", at = DateTimeOffset.UtcNow.ToString("O") }; }),
        metrics = inertia.Defer(async (_, ct) => { await Task.Delay(650, ct); return new[] { new Metric("Contacts in SQLite", repository.Page().Total.ToString()), new Metric("Deferred group", "dashboard") }; }, "dashboard"),
        summary = inertia.Defer(async (_, ct) => { await Task.Delay(300, ct); return "I arrived in the same request as the metrics."; }, "dashboard"),
        fragile = inertia.Defer(async (_, ct) => { await Task.Delay(350, ct); if (!recover) throw new InvalidOperationException("Intentional rescued demo failure"); return "Recovered on a targeted retry."; }, "recovery").Rescue(),
        once = inertia.Once((_, _) => Task.FromResult<object?>(new { token = Guid.NewGuid().ToString("N")[..8], at = DateTimeOffset.UtcNow.ToString("O") })).As("kitchen-sink-once").Until(TimeSpan.FromMinutes(5)),
        visible = inertia.Optional((_, _) => Task.FromResult<object?>("Loaded when this section entered the viewport.")),
    });

    [HttpGet("/merging", Name = "Demos.merging")]
    public IActionResult Merging([FromQuery] int batch = 1, [FromQuery] string direction = "append")
    {
        batch = Math.Clamp(batch, 1, 20);
        var entries = Enumerable.Range((batch - 1) * 3 + 1, 3).Select(id => new FeedEntry(id, $"Activity {id}", "Engineering")).ToArray();
        var merge = inertia.Merge(entries).MatchOn("id");
        if (direction == "prepend") merge.Prepend();
        return inertia.Render("Merging", new MergingProps
        {
            Batch = batch, Entries = merge,
            Preferences = inertia.DeepMerge(new Dictionary<string, object?> { [batch % 2 == 1 ? "appearance" : "notifications"] = batch % 2 == 1 ? new { theme = "sage", revision = batch } : (object)new { email = true, revision = batch } }),
        });
    }

    [HttpGet("/feed", Name = "Demos.feed")]
    public IActionResult Feed([FromQuery] int page = 1)
    {
        page = Math.Clamp(page, 1, 5);
        var data = new FeedPage(Enumerable.Range((page - 1) * 6 + 1, 6).Select(id => new FeedEntry(id, $"A small update from the studio · {id:00}", new[] { "Design", "Engineering", "Operations" }[id % 3])).ToArray(), page, page < 5 ? page + 1 : null);
        return inertia.Render("Feed", new { feed = inertia.Scroll(data, metadata: new ScrollMetadata { CurrentPage = page, PreviousPage = page > 1 ? page - 1 : null, NextPage = data.NextPage }).MatchOn("data.id") });
    }

    [HttpGet("/polling", Name = "Demos.polling")]
    public IActionResult Polling() => inertia.Render("Polling", new
    {
        pulse = new { at = DateTimeOffset.UtcNow.ToString("O"), contacts = repository.Page().Total, requestId = Guid.NewGuid().ToString("N")[..8] },
        initial = DateTimeOffset.UtcNow.ToString("O"),
    });

    [HttpGet("/navigation", Name = "Demos.navigation")]
    public IActionResult Navigation([FromQuery] bool encrypted = false)
    {
        inertia.EncryptHistory(encrypted);
        return inertia.Render("Navigation", new { encrypted });
    }

    [HttpPost("/session", Name = "Demos.login")]
    public async Task<IActionResult> Login([FromBody] SignInInput input)
    {
        if (!ModelState.IsValid) return LocalRedirect("/navigation");
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, input.Name)], CookieAuthenticationDefaults.AuthenticationScheme)));
        inertia.Flash("message", $"Hello, {input.Name}. Shared auth props now reflect your cookie identity.");
        return LocalRedirect("/navigation");
    }

    [HttpDelete("/session", Name = "Demos.logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        inertia.ClearHistory();
        inertia.Flash("message", "Signed out. Inertia cleared its history encryption key.");
        return LocalRedirect("/navigation");
    }

    [HttpPost("/navigation/fragment", Name = "Demos.fragment")]
    public IActionResult Fragment()
    {
        inertia.PreserveFragment();
        inertia.Flash("message", "Redirected while preserving your URL fragment.");
        return LocalRedirect("/navigation");
    }

    [HttpGet("/navigation/location", Name = "Demos.location")]
    public IResult LocationDemo() => inertia.Location("/navigation?location=full-visit");

    [HttpGet("/rendering", Name = "Demos.rendering")]
    public IActionResult Rendering([FromQuery] bool csr = false, [FromQuery] bool failSsr = false)
    {
        if (csr) inertia.WithoutSsr();
        return inertia.Render("Rendering", new { mode = csr ? "Client rendering requested" : "Server rendering requested", forceSsrFailure = failSsr });
    }

    [HttpGet("/rendering/error", Name = "Demos.error")]
    public IActionResult ErrorDemo() => inertia.Render("Error", new ErrorPageProps
    {
        Message = inertia.Always((_, _) => throw new DemoRenderException()),
    });
}
