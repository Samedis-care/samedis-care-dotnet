# samedis-care-dotnet

Shared .NET libraries for the tools around [Samedis.care](https://samedis.care).

This repository holds the access layer for the Samedis.care API V4, which until now
existed as a copy inside every tool under `samedis-care-tools`. The goal is that each
tool consumes the same tested auth, HTTP and filter logic from a NuGet package instead
of from copy-paste.

## Packages

| Package | Purpose |
| --- | --- |
| `SamedisCare.Api` | Auth (Ident Services OAuth), HTTP, gridfilter/sort/pagination, resource routing |

### What `SamedisCare.Api` covers today

- **Transport**: `Authenticate`, `RequestData` (GET/POST/PUT, issue uploads, 429 retry), `HttpSettings`
- **Query**: `FilterBuilder` for `gridfilter=...` payloads
- **Routing**: `ITenantScope` / `TenantScope`
- **Resources**: `Tenant`, `Inventories`, `Issues`, `Staffs`, `Positions`, `Departments`
  — `Positions` and `Departments` also carry the generic `Find…Id` / `FindOrCreate…` helpers
- **Diagnostics**: `ISyncLog` plus an optional GET dump via `RequestData.TestMode`

Not in the library on purpose: CSV/Excel handling, LDAP, SAP, database access, and
per-tool config shapes. Those belong to the consuming tool.

## Installation

```bash
dotnet add package SamedisCare.Api
```

Targets `net8.0`, `net9.0` and `net10.0`. A project on any of those frameworks can
consume the package; a lower target framework can never consume a higher one, which is
why `net8.0` stays in the package for as long as a consumer needs it.

## Resource routing

`ITenantScope` encapsulates the only structural difference between the normal world and
the enterprise world ("service world"): the path prefix. Payload and response are
identical for the resources both worlds support, so a sync only swaps the scope — never
its mapping logic.

```csharp
using SamedisCare.Api.Routing;

// Normal world
var scope = TenantScope.Standard(tenantId);
scope.Resource("inventories");
// -> /api/v4/tenants/{tenantId}/inventories

// Enterprise, client-scoped (mirror of the normal world)
var ent = TenantScope.Enterprise(tenantId, clientId);
ent.Resource("inventories");
// -> /api/v4/enterprise/tenants/{tenantId}/clients/{clientId}/inventories

// Enterprise, cross-facility aggregate (mostly read-only)
var agg = TenantScope.EnterpriseTenant(tenantId);
agg.Resource("issues");
// -> /api/v4/enterprise/tenants/{tenantId}/issues
```

The enterprise world deliberately supports **fewer** resources than the normal one
(no `staffs`, `trainings`, `device_types`, `positions`, `profit_centers`; several
resources are read-only). Consumers must account for that — `ITenantScope` builds paths,
it does not guarantee they exist.

## Development

```bash
dotnet restore SamedisCare.Dotnet.sln
dotnet build SamedisCare.Dotnet.sln -c Release
dotnet test SamedisCare.Dotnet.sln -c Release
```

Running the full test suite locally requires the .NET 8, 9 and 10 runtimes, because the
tests execute once per target framework.

Package versions are managed centrally in `Directory.Packages.props` — the `.csproj`
files deliberately carry no versions.

## Documentation language

All documentation in this repository is written in **English**: README, XML doc comments,
code comments, and workflow comments. The package is public, so its documentation is too.

## Release

Pushing a version tag builds, tests, packs and publishes to nuget.org:

```bash
git tag v0.1.0
git push origin v0.1.0
```

Publishing uses [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
(OIDC, short-lived key) rather than a stored API key. The one-time setup is documented as
a comment at the top of `.github/workflows/release.yml`.

## License

MIT — see [LICENSE](LICENSE).
