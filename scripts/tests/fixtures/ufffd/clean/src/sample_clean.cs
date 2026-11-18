// Fixture: clean sample, zero U+FFFD characters (pure ASCII).
// Self-test for scripts/scan-ufffd.ps1 and scripts/scan-ufffd.sh (clean case).
// Expected result: no output line for this file, exit code 0.
// Layout note: this fixture directory acts as a pseudo repo root, hence the src/ subfolder.
namespace Fixtures.Ufffd;

public static class SampleClean
{
    public const string Intact = "plain ascii text";
}
