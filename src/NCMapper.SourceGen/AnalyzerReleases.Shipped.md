; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NCMAP001 | NCMapper | Warning | Source field not found
NCMAP002 | NCMapper | Warning | Type incompatible
NCMAP003 | NCMapper | Warning | No properties mapped
NCMAP005 | NCMapper | Warning | Compose field not found
NCMAP006 | NCMapper | Warning | Unmapped target member
NCMAP007 | NCMapper | Info | Unmapped source member
NCMAP008 | NCMapper | Error | Converter type mismatch
NCMAP009 | NCMapper | Warning | Duplicate field mapping
NCMAP010 | NCMapper | Warning | Enum no overlap
NCMAP011 | NCMapper | Info | Non-public type skipped
