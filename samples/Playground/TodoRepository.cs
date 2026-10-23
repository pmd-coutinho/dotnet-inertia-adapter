namespace Playground;

public sealed record TodoRow(long Id, string Title, bool Done);
public sealed record TodoPage(IReadOnlyList<TodoRow> Data, int? NextPage);
public sealed record TodoStats(int Total);
public sealed record UserSummary(long Id, string Name);
public sealed record AuthShared(UserSummary? User);
public sealed record AppShared(string Name);

/// <summary>Small in-memory store so the example runs without a database.</summary>
public sealed class TodoRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<long, TodoRow> _items = Enumerable.Range(1, 16)
        .ToDictionary(id => (long)id, id => new TodoRow(id, $"Example task {id}", false));
    private long _next = 16;
    public int Count { get { lock (_gate) return _items.Count; } }
    public TodoPage Page(int page)
    {
        lock (_gate)
        {
            var rows = _items.Values.OrderByDescending(item => item.Id).Skip((page - 1) * 5).Take(5).ToArray();
            return new(rows, page * 5 < _items.Count ? page + 1 : null);
        }
    }
    public void Add(string title, bool done) { lock (_gate) { var id = ++_next; _items[id] = new(id, title, done); } }
    public bool Update(long id, string title, bool done)
    {
        lock (_gate) { if (!_items.ContainsKey(id)) return false; _items[id] = new(id, title, done); return true; }
    }
    public bool Delete(long id) { lock (_gate) return _items.Remove(id); }
}
