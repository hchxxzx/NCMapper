// Fixture: exactly ONE U+FFFD replacement character (byte sequence EF BF BD) on line 9.
// Self-test for scripts/scan-ufffd.ps1 and scripts/scan-ufffd.sh (hit case).
// Expected output: this file, line 9, column 42.
// Layout note: this fixture directory acts as a pseudo repo root, hence the src/ subfolder.
namespace Fixtures.Ufffd;

public static class SampleHit
{
    public const string Damaged = "before�after";
}
