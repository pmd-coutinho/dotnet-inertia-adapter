using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace InertiaNet.Tests.Helpers;

/// <summary>
/// In-memory ITempDataDictionary for unit tests. No session required.
/// </summary>
public sealed class FakeTempData : ITempDataDictionary
{
    private readonly Dictionary<string, object?> _data = new();

    public object? this[string key]
    {
        get => _data.TryGetValue(key, out var v) ? v : null;
        set => _data[key] = value;
    }

    public int Count => _data.Count;
    public bool IsReadOnly => false;
    public ICollection<string> Keys => _data.Keys;
    public ICollection<object?> Values => _data.Values;

    public void Add(string key, object? value) => _data[key] = value;
    public void Add(KeyValuePair<string, object?> item) => _data[item.Key] = item.Value;
    public void Clear() => _data.Clear();
    public bool Contains(KeyValuePair<string, object?> item) => _data.Contains(item);
    public bool ContainsKey(string key) => _data.ContainsKey(key);
    public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, object?>>)_data).CopyTo(array, arrayIndex);
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _data.GetEnumerator();
    public void Keep() { }
    public void Keep(string key) { }
    public void Load() { }
    public object? Peek(string key) => _data.TryGetValue(key, out var v) ? v : null;
    public bool Remove(string key) => _data.Remove(key);
    public bool Remove(KeyValuePair<string, object?> item) => _data.Remove(item.Key);
    public void Save() { }
    public bool TryGetValue(string key, out object? value) => _data.TryGetValue(key, out value);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _data.GetEnumerator();
}

/// <summary>
/// Factory that returns a shared FakeTempData instance.
/// </summary>
public sealed class FakeTempDataFactory : ITempDataDictionaryFactory
{
    private readonly FakeTempData _tempData = new();

    public FakeTempData TempData => _tempData;

    public ITempDataDictionary GetTempData(HttpContext context) => _tempData;
}
