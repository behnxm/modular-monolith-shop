# ADR 0012: Containerization, Multi-Stage Builds, and Non-Root Execution

## Status
Accepted

## Context
Production container security mandates adherence to the principle of least privilege. Containers running as the default `root` user create severe container breakout risks in the event of an unpatched runtime vulnerability. Furthermore, container image bloat slows down deployment pipelines and expands the CVE attack surface.

## Decision
I designed a hardened, multi-stage `Dockerfile` and `compose.yaml`:

1. **Multi-Stage Build Pipeline**:
   - **Build Stage**: Uses `mcr.microsoft.com/dotnet/sdk:10.0-alpine` for cached layer restoration of `Directory.Build.props`, `Directory.Packages.props`, and all project `.csproj` files before building.
   - **Runtime Stage**: Uses ultra-minimal `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` (~100 MB footprint).
2. **Built-in Non-Root User**:
   - Runs exclusively as `USER $APP_UID` (UID 1654 in standard .NET Alpine images).
   - All published files are chowned: `--chown=$APP_UID:$APP_UID`.
3. **Hardened Container Configuration (`compose.yaml`)**:
   - `security_opt: ["no-new-privileges:true"]`: Prevents child processes from gaining elevated privileges via setuid binaries.
   - `cap_drop: ["ALL"]`: Drops all Linux kernel capabilities.
   - `read_only: true`: Read-only root filesystem prevents writing malicious files to disk.
   - `tmpfs`: Explicit temporary in-memory mounts for `/tmp` and `/app/secrets`.
4. **Built-in Container Healthcheck**:
   - `HEALTHCHECK CMD ["/app/Shop.Host", "--healthcheck"]`: Uses the built-in binary `--healthcheck` switch to probe health without requiring `curl` or `wget` inside the runtime image.

## Consequences
### Positive
- Enterprise-grade container security posture with zero unnecessary root privileges.
- Ultra-small attack surface and minimal image size.
- Fast deployment and startup times.

### Negative
- Read-only root filesystem requires explicit tmpfs mounts for any local scratch data.
