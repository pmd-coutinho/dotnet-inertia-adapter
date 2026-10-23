using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using InertiaNet.Core;
using InertiaNet.Props;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Playground.Controllers;

public sealed class TodoInput
{
    [Required, StringLength(80, MinimumLength = 2)] public string Title { get; set; } = "";
    public bool Done { get; set; }
}
public sealed class LoginInput { [Required, StringLength(40)] public string Name { get; set; } = ""; }
public sealed class TodoIndexProps
{
    public required string Client { get; init; }
    [InertiaPropType(typeof(TodoPage))] public required ScrollProp Todos { get; init; }
    [InertiaPropType(typeof(TodoStats))] public required DeferProp Stats { get; init; }
    public int Total { get; init; }
    public bool ForceSsrFailure { get; init; }
}
public static class Pages
{
    public static readonly InertiaComponent<TodoIndexProps> ReactTodos = new("React/Todos");
    public static readonly InertiaComponent<TodoIndexProps> VueTodos = new("Vue/Todos");
}

[Route("{client:regex(^react$|^vue$)}/todos")]
public sealed class TodosController(IInertiaService inertia, TodoRepository repository) : Controller
{
    [HttpGet("", Name = "Todos.index")]
    public IActionResult Index(string client, [FromQuery] int page = 1, [FromQuery] bool failSsr = false)
    {
        page = Math.Clamp(page, 1, 10000);
        var data = repository.Page(page);
        var props = new TodoIndexProps
        {
            Client = client, Total = repository.Count, ForceSsrFailure = failSsr,
            Todos = inertia.Scroll(data, metadata: new ScrollMetadata { CurrentPage = page, PreviousPage = page > 1 ? page - 1 : null, NextPage = data.NextPage }),
            Stats = inertia.Defer((_, _) => Task.FromResult<object?>(new TodoStats(repository.Count)), "stats"),
        };
        if (client == "react") return inertia.Render(Pages.ReactTodos, props);
        return inertia.Render(Pages.VueTodos, props);
    }

    [HttpPost("", Name = "Todos.store")]
    public IActionResult Store(string client, [FromBody] TodoInput input)
    {
        if (!ModelState.IsValid) return Back(client);
        repository.Add(input.Title, input.Done);
        inertia.Flash("message", "Task created");
        return Back(client);
    }

    [HttpPut("{id:long}", Name = "Todos.update")]
    public IActionResult Update(string client, long id, [FromBody] TodoInput input)
    {
        if (!ModelState.IsValid) return Back(client);
        if (!repository.Update(id, input.Title, input.Done)) return NotFound();
        inertia.Flash("message", "Task updated");
        return Back(client);
    }

    [HttpDelete("{id:long}", Name = "Todos.destroy")]
    public IActionResult Destroy(string client, long id)
    {
        if (!repository.Delete(id)) return NotFound();
        inertia.Flash("message", "Task deleted");
        return Back(client);
    }

    [HttpPost("session", Name = "Todos.login")]
    public async Task<IActionResult> Login(string client, [FromBody] LoginInput input)
    {
        if (!ModelState.IsValid) return Back(client);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, input.Name)], CookieAuthenticationDefaults.AuthenticationScheme)));
        inertia.Flash("message", "Signed in");
        return Back(client);
    }

    private LocalRedirectResult Back(string client) => LocalRedirect($"/{client}/todos");
}
