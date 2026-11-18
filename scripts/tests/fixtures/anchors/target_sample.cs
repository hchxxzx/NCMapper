// Fixture target for scripts/check-doc-anchors.py self-test (self-contained sample).
// The case_*.cs files here point at THIS file; it holds no anchor reference of its own.
namespace Fixtures.Anchors;

public class TargetSample
{
    public int DeclaredAtLineSeven { get; set; }

    public string Describe(int value)
    {
        var doubled = value * 2;
        return doubled.ToString();
    }

    public string LongBody(int value)
    {
        var a = value + 1;
        var b = a + 1;
        var c = b + 1;
        var d = c + 1;
        var e = d + 1;
        var f = e + 1;
        var g = f + 1;
        return g.ToString();
    }

    public void Tail()
    {
    }
}
