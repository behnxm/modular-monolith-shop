# ADR 0011: Strict JSON Deserialization and Request Size Limits

## Status
Accepted

## Context
APIs exposed to the public internet are vulnerable to mass assignment attacks, payload bombing (Denial of Service via massive HTTP bodies), and unexpected payload injection. By default, `System.Text.Json` ignores extraneous JSON properties that do not match the target C# model, which can mask client integration errors and permit silent payload pollution.

Additionally, default Kestrel limits allow request bodies up to 30 MB, creating easy DoS vectors if malicious clients stream large JSON payloads into memory.

## Decision
I applied strict serialization and ingress limits in `Program.cs`:

1. **Strict JSON Deserialization**:
   - `options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;`
   - Any client request sending unmapped JSON properties is immediately rejected with HTTP 400 Bad Request and an RFC 7807 ProblemDetails payload indicating the exact invalid property name.
   - `options.SerializerOptions.MaxDepth = 64;` prevents deeply nested JSON stack overflow attacks.
   - `GlobalExceptionHandler` intercepts `JsonException` and `BadHttpRequestException` to ensure 400 Bad Request with ProblemDetails is returned rather than 500 Internal Server Error.

2. **Strict Request Body Size Limits (64 KB)**:
   - Configured Kestrel server limits:
     ```csharp
     builder.WebHost.ConfigureKestrel(options =>
     {
         options.Limits.MaxRequestBodySize = 64 * 1024; // 64 KB
         options.AddServerHeader = false;
     });
     ```
   - Configured form options multipart limit:
     ```csharp
     builder.Services.Configure<FormOptions>(options =>
     {
         options.MultipartBodyLengthLimit = 64 * 1024;
     });
     ```
   - Payloads exceeding 64 KB are rejected at the TCP socket layer before reading into memory, returning HTTP 413 Payload Too Large.

## Consequences
### Positive
- Prevents mass assignment, payload tampering, and body size DoS attacks.
- Clients are immediately alerted to typos in property names during development.
- Verified by automated tests in `AuthorizationMatrixTests.cs`.

### Negative
- Endpoints requiring file uploads cannot use standard endpoints (this API has no file upload requirements by deliberate scope).
