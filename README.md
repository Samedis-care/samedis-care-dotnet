# samedis-care-dotnet

Gemeinsame .NET-Bibliotheken für die Tools rund um [Samedis.care](https://samedis.care).

Dieses Repo bündelt die Zugriffsschicht auf die Samedis.care API V4, die bisher in jedem
Tool unter `samedis-care-tools` als Kopie lag. Ziel ist, dass jedes Tool dieselbe geprüfte
Auth-, HTTP- und Filter-Logik über ein NuGet-Paket bezieht statt über Copy-Paste.

## Pakete

| Paket | Zweck |
| --- | --- |
| `SamedisCare.Api` | Auth (Ident Services OAuth), HTTP, Gridfilter/Sort/Pagination, Resource-Routing |

## Installation

```bash
dotnet add package SamedisCare.Api
```

## Resource-Routing

`ITenantScope` kapselt den einzigen strukturellen Unterschied zwischen der normalen Welt
und der Enterprise-Welt ("Service-Welt"): das Pfad-Prefix. Payload und Response sind für
die gemeinsam unterstützten Resourcen identisch — ein Sync tauscht also nur den Scope,
nicht seine Mapping-Logik.

```csharp
using SamedisCare.Api.Routing;

// Normale Welt
var scope = TenantScope.Standard(tenantId);
scope.Resource("inventories");
// -> /api/v4/tenants/{tenantId}/inventories

// Enterprise, client-bezogen (Spiegel der normalen Welt)
var ent = TenantScope.Enterprise(tenantId, clientId);
ent.Resource("inventories");
// -> /api/v4/enterprise/tenants/{tenantId}/clients/{clientId}/inventories

// Enterprise, einrichtungsübergreifendes Aggregat (überwiegend read-only)
var agg = TenantScope.EnterpriseTenant(tenantId);
agg.Resource("issues");
// -> /api/v4/enterprise/tenants/{tenantId}/issues
```

Die Enterprise-Welt unterstützt bewusst **weniger** Resourcen als die normale
(kein `staffs`, `trainings`, `device_types`, `positions`, `profit_centers`; mehrere
Resourcen nur lesend). Ein Consumer muss das berücksichtigen — `ITenantScope` baut Pfade,
es garantiert nicht deren Existenz.

## Entwicklung

```bash
dotnet restore SamedisCare.Dotnet.sln
dotnet build SamedisCare.Dotnet.sln -c Release
dotnet test SamedisCare.Dotnet.sln -c Release
```

Zielframework ist `net8.0` (alle Consumer laufen darauf). Paketversionen werden zentral
in `Directory.Packages.props` verwaltet — in den `.csproj` steht bewusst keine Version.

## Release

Ein Versions-Tag löst Build, Tests, Pack und den Push nach nuget.org aus:

```bash
git tag v0.1.0
git push origin v0.1.0
```

Die Veröffentlichung nutzt [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
(OIDC, kurzlebiger Key) statt eines gespeicherten API-Keys. Details und die einmalige
Einrichtung stehen als Kommentar in `.github/workflows/release.yml`.

## Lizenz

MIT — siehe [LICENSE](LICENSE).
