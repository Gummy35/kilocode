# C# SDK Generation Documentation

## Generation Chain

This document records the exact process used to generate the C# client from the Kilo OpenAPI specification.

### Source Information

| Item | Value |
|------|-------|
| **OpenAPI source file** | `packages/sdk/openapi.json` |
| **OpenAPI version** | 3.1.0 |
| **OpenAPI SHA (at generation)** | `A454191C27D89DBECB23121DA0EDBB596DE5D4561F6F2229AA4C6FDF2B879FBD` (SHA-256) |
| **VS Code source revision** | `2431b51e7b` |
| **Generation date** | 2026-08-10 |

### Generator Information

| Item | Value |
|------|-------|
| **Generator** | Microsoft Kiota |
| **Generator version** | 1.34.1 |
| **Generation command** | `kiota generate -l CSharp -d packages/sdk/openapi.json -o Generated -n KiloVisualStudioExtension.Generated -c KiloClient --clean-output` |
| **Target framework** | .NET Framework 4.8.1 (`net481`) |

### Dependencies

The following NuGet packages are required:

- `Microsoft.Kiota.Bundle` version 2.0.0
- `Microsoft.Kiota.Authentication.Azure` version 2.0.0

### Generation Notes

1. **No servers entry**: The OpenAPI specification does not include a `servers` entry, so the base URL must be set manually when using the client.

2. **Polymorphic types without discriminators**: Multiple schemas in the OpenAPI spec are defined as polymorphic but lack discriminator definitions. This is a known limitation of the source OpenAPI specification.

3. **SSE support**: The generated client's `Event.GetAsync()` method returns a `Stream` which can be used for Server-Sent Events. However, the existing `SseClient.cs` implementation provides better reconnection and heartbeat handling, so it is retained.

4. **Basic Auth**: The generated client does not automatically configure Basic Authentication. Authentication headers must be set manually via the request adapter or per-request configuration.

### Reproducibility

To regenerate the client:

```bash
# Ensure Kiota 1.34.1 is installed
kiota --version  # Should show 1.34.1

# Generate the client
kiota generate -l CSharp -d packages/sdk/openapi.json -o Generated -n KiloVisualStudioExtension.Generated -c KiloClient --clean-output

# Add dependencies to project file
dotnet add package Microsoft.Kiota.Bundle --version 2.0.0
dotnet add package Microsoft.Kiota.Authentication.Azure --version 2.0.0
```

### Constraints

- **No fork dependencies**: All generated code and dependencies are from public NuGet packages and the upstream Kilo OpenAPI specification.
- **Separation of concerns**: Generated code is kept in the `Generated/` directory and should never be manually modified.
- **Deterministic**: Same OpenAPI source + same Kiota version = same output.
