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

### Layout

Infrastructure is grouped by concern; the classes that mirror API payloads are grouped by
API version and surface, so it stays visible which class belongs to which endpoint family
and a future v5 can be added alongside instead of colliding.

| Namespace | Contents |
| --- | --- |
| `SamedisCare.Api.Auth` | `Authenticate` — Ident Services OAuth |
| `SamedisCare.Api.Http` | `RequestData` (GET/POST/PUT, uploads, 429 retry), `HttpSettings` |
| `SamedisCare.Api.Query` | `FilterBuilder` for `gridfilter=...` payloads |
| `SamedisCare.Api.Routing` | `ITenantScope`, `TenantScope` |
| `SamedisCare.Api.Logging` | `ISyncLog`, `ConsoleSyncLog`, `FileSyncLog`, `NullSyncLog` |
| `SamedisCare.Api.Common` | `Helper`, `Dates`, `Capability`, `ApiEnvelope` |
| `SamedisCare.Api.V4.Public` | `Inventories`, `Issues`, `Staffs`, `Positions`, `Departments`, `DepartmentInfo` |
| `SamedisCare.Api.V4.Common` | `Tenant` — appears identically across several surfaces |

`Positions` and `Departments` also carry the generic `Find…Id` / `FindOrCreate…` helpers.
`DepartmentInfo` bundles the title, code and cost centre a department upsert needs. It holds
no knowledge of where those values came from, so a CSV, LDAP or database source can all fill
it — only the code that *reads* a particular import format belongs in the consuming tool.

The specs the model classes are validated against live in [`doc/v4/`](doc/v4):
`public.yaml`, `enterprise.yaml`, `internal.yaml`, `my.yaml`, `mdm.yaml`.

Not in the library on purpose: CSV/Excel handling, LDAP, SAP, database access, per-tool
config shapes, and anything that terminates the process. Those belong to the consuming
tool — `Capability.Probe` reports whether a resource is readable, it does not decide
whether the run should stop.

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
