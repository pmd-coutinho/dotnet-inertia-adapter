using System.Numerics;
using InertiaNet.Core;
using Microsoft.AspNetCore.Mvc;

public enum LargeCode : long { Safe = 42, Unsafe = long.MaxValue }
public sealed class LargeProps
{
    public long Id { get; set; }
    public ulong Unsigned { get; set; }
    public BigInteger Huge { get; set; }
    public int Count { get; set; }
    public decimal Price { get; set; }
    public LargeCode Code { get; set; }
}
[Route("numbers")]
public class NumbersController
{
    [HttpGet("{id:long}")] public object Show(long id) => null!;
    public void Page(IInertiaService inertia) { inertia.Render("Numbers", new LargeProps()); }
}
