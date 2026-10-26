using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace InertiaNet.Tests.Helpers;

/// <summary>
/// Framework TempData dictionary with an in-memory provider. Reads, Keep, Peek,
/// Remove and Save use the same semantics as production. No session required.
/// </summary>
public sealed class FakeTempData : TempDataDictionary
{
    private readonly MemoryTempDataProvider _provider;

    public FakeTempData() : this(new MemoryTempDataProvider()) { }
    private FakeTempData(MemoryTempDataProvider provider) : base(new DefaultHttpContext(), provider) => _provider = provider;

    /// <summary>Completes this request and loads its saved state in a fresh dictionary.</summary>
    public FakeTempData NextRequest()
    {
        Save();
        return new FakeTempData(_provider);
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> _stored = new(StringComparer.Ordinal);
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>(_stored, StringComparer.Ordinal);
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _stored = new(values, StringComparer.Ordinal);
    }
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
