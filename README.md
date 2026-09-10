# samedis-care-dotnet

Shared .NET libraries for the tools around [Samedis.care](https://samedis.care).

This repository holds the access layer for the Samedis.care API V4, which until now
existed as a copy inside every tool under `samedis-care-tools`. The goal is that each
tool consumes the same tested auth, HTTP and filter logic from a NuGet package instead
of from copy-paste.

## Packages

| Package | Purpose |
| --- | --- |
| `SamedisCare.Api` | Auth (Ident Services OAuth), HTTP, gridfilter/sort/pagination, resource routing, record lookup |
| `SamedisCare.Helper` | Logging, dates, CSV, text encodings, config, database access — no HTTP |
| `SamedisCare.Mail` | Sending mail over SMTP, Microsoft Graph or the Gmail API |

`SamedisCare.Mail` is separate rather than part of `SamedisCare.Helper` on purpose: MailKit,
Microsoft.Graph, Google.Apis.Gmail and Azure.Identity together weigh more than everything
else in the family, and most tools send no mail. They should not have to carry a Graph client
to get a logger.

### Layout

Infrastructure is grouped by concern; the classes that mirror API payloads are grouped by
API version and surface, so it stays visible which class belongs to which endpoint family
and a future v5 can be added alongside instead of colliding.

| Namespace | Contents |
| --- | --- |
| `SamedisCare.Api.Auth` | `Authenticate` — Ident Services OAuth |
| `SamedisCare.Api.Http` | `RequestData` (GET/POST/PUT, uploads, 429 retry), `HttpSettings` |
| `SamedisCare.Api.Query` | `FilterBuilder` for `gridfilter=...` payloads |
| `SamedisCare.Api.Routing` | `ITenantScope`, `TenantScope`, `KeyLookup` |
| `SamedisCare.Api.Lookup` | `ResourceLookup`, `Cascades`, `Records`, `Regulatory`, `LookupUnavailableException` |
| `SamedisCare.Api.Common` | `Ids`, `JsonApi`, `Capability`, `ApiEnvelope` |
| `SamedisCare.Api.V4.Public` | `Inventories`, `Issues`, `Trainings`, `Staffs`, `Positions`, `Departments`, `DepartmentInfo`, `CatalogValues` |
| `SamedisCare.Api.V4.Common` | `Tenant` — appears identically across several surfaces |
| `SamedisCare.Helper.Logging` | `ISyncLog`, `ConsoleSyncLog`, `FileSyncLog`, `NullSyncLog`, `LogFormat` |
| `SamedisCare.Helper.Text` | `Csv`, `Strings`, `Numbers`, `TextEncodings` |
| `SamedisCare.Helper.Data` | `Database`, `Rows` |
| `SamedisCare.Helper.IO` | `Files` |
| `SamedisCare.Helper.Config` | `ConfigStore` |
| `SamedisCare.Mail` | `Mailer`, `MailMessage`, `MailSettings` |

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

## Finding existing records

`ResourceLookup` performs the lookups and remembers hits **and** misses; `Cascades` holds
the resolution order per resource. A hit on a stronger key is final — never fall through to
a weaker one, or an update moves the row's `external_id` onto a different record and the
unique index on `(tenant_id, external_id)` rejects it.

```csharp
var lookup = Cascades.For(client, scope, "inventories");
var id = Cascades.Inventory(lookup, row.SamedisId, row.ExternalId, row.DeviceNumber);
```

Movement data resolves by Mongo id or by `external_id`, with one exception noted below.

### Two different mechanisms

Both exist and they are not interchangeable:

- **`{resource}/via/{field}/{value}`** — an exact find on one field. Needs the route mounted
  on the resource *and* the field declared as a via field by the model.
- **`gridfilter`** — works on **every field of the document** (`gridfilter_fields` is
  `fields.keys + relation_fields`, not a curated list), including dotted paths into an
  embedded hash such as `regulatory.udi_id`. No route or whitelist to satisfy.

So a field can be filterable without being resolvable by the via route. That is exactly the
situation for device models.

### Which via keys a resource accepts

Not documented in the specs under `doc/v4`, so this was read off the server:

Probed against production with a value that does not exist, so the status tells the two
cases apart: **404 `record_not_found_error`** means the field is supported, **500** means it
is not, and a **404 with no JSON body** means the route is not mounted at all.

| Resource | via route | accepted via fields | probe |
| --- | --- | --- | --- |
| `inventories` | yes | `device_number`, `external_id` | 404 not-found |
| `staffs` | yes | `external_id`, `employee_no`, `email` | 404 not-found |
| `positions` | yes | `title`, `external_id` | 404 not-found |
| `device_locations` | yes | `external_id` | not probed |
| `issues` | yes | `external_id` | 404 not-found |
| `incidents` | yes | `incident_number`, `external_id` | 404 not-found |
| `contracts` | yes | `id`, `contract_number` | not probed |
| `trainings` | yes | `briefing_number` — **no `external_id`** | **500** |
| `departments` | yes | **none** | **500** |
| `device_models` | no (sync endpoint) | — | 404, no JSON |

**A via lookup on an unsupported field answers 500, not 4xx.** `ResourceLookup` therefore
throws `LookupUnavailableException` on any 5xx instead of reporting "no such record": the two
outcomes lead to opposite actions, and reading a server error as absence makes a sync
duplicate every record it touches. A 404 stays a real answer.

Three traps:

- **`trainings` has no `external_id` at all.** `Briefing` does not include the `ExternalId`
  concern, so the field does not exist on the model and appears in no serializer in either
  spec. Trainings resolve by `briefing_number`. This is the one movement resource that
  breaks the id-or-external_id rule.
- **`departments` mounts the route but accepts no field.** The model carries no
  `external_id` and the controller does not permit one, so a `code` sent under that key is
  silently dropped. Departments resolve by title.
- **`device_models` has no via route on the endpoint a sync uses.** It exists only on the
  MDM endpoint (`.../tenants/{id}/mdm/device_models`) — `config/routes/v4.rb:317`, inside
  `namespace :mdm`; the tenant route at line 235 mounts `concerns: :changelogs` and nothing
  else. `external_id` is still writable and still filterable, so the cascade matches it with
  a gridfilter instead. That is also the only form that works in enterprise mode, where no
  via route is mounted on anything.
- **A merged-away device model is unreachable by id, permanently.** Device models can be
  merged (`Catalog#merge_device_model_not_self`), and the merge **hard-destroys** the source:
  its inventories move to the survivor, and its title, manufacturer and `external_id` die
  with it. Nothing records where it went — `Catalog::VIA_FIELDS` is `%i[external_id]` and no
  `merged_catalog*` field exists on the model — so a historic id is a plain 404 and
  `ResourceLookup.ById` returns null, not the survivor. A sync that treats that miss as "not
  imported yet" will create a duplicate of a model somebody deliberately merged away. See
  [samedis-care-issues#2347](https://github.com/Samedis-care/samedis-care-issues/issues/2347)
  for the resolution work; until it is in production, code against the 404.

### The enterprise API has no via route at all

`via/:via_name/:via_value` is mounted on **18 resources of the tenant API** and on **none of
the enterprise ones** — `config/routes/v4_enterprise.rb` carries only `concerns: :changelogs`.
Verified live on 2026-08-30: the same inventory answered 200 through the route under the
tenant path, 404 under the enterprise path, and was found by gridfilter under both.

That is why the mechanism is a property of the scope rather than a decision each call site
makes:

```csharp
TenantScope.Standard(tenantId)              // KeyLookup.Route
TenantScope.Enterprise(tenantId, clientId)  // KeyLookup.Filter
```

`Cascades.For` reads it from the scope and `ResourceLookup.ByUniqueField` dispatches, so a
sync moved to the enterprise API changes its scope and nothing else. `ByVia` stays available
where a caller knows the route exists.

`KeyLookup` is deliberately separate from `IsEnterprise`: today the two agree, but one is a
path family and the other is which routes are mounted, and a release could change either
without the other.

**Why this needed a switch rather than tolerance.** A route that is not mounted answers 404,
and 404 is the one status that means "no such record". Left alone, every `ByVia` on the
enterprise API would have resolved to null silently and each cascade would have dropped to
its weakest key — for inventories the device number, which a source may have reassigned to a
different device. The two 404s are told apart by their body: the application's carries
`meta.msg.error = record_not_found_error`, the router's is a bare `{"status":404}` with no
envelope. `ApiEnvelope.HasEnvelope` is that test.

### Device models: the scope is not optional

`filter[scope]` is documented with `default: public_and_tenant`, but **omitting the parameter
yields the tenant's own catalogs only.** Verified against production on the same tenant:

| request | `meta.total` |
| --- | --- |
| no `filter[scope]` | 13 |
| `filter[scope]=tenant` | 13 |
| `filter[scope]=public_and_tenant` | 29,942 |
| `filter[scope]=public` | 29,929 |

A device-model lookup that leaves it out misses all public master data and answers "does not
exist" for devices that are plainly there — so every gridfilter and regulatory step in
`Cascades.DeviceModel` carries it. Note also that a serialized attribute is not automatically
filterable: `gridfilter` covers the *document's* fields, and `device_form_title` — present in
the response — is not one, so filtering on it raises `gridfilter_error`.

### Device models

`external_id` on a catalog only ever resolves a model the tenant created itself: catalogs
are largely public master data shared across facilities, and the unique index is on
`(tenant_id, external_id)`. The regulatory identifiers are what travels with the device, so
they carry the lookup:

```csharp
var lookup = Cascades.For(client, scope, "device_models");
var id = Cascades.DeviceModel(
    lookup, row.CatalogId, row.Title, row.Manufacturer,
    Regulatory.Identifiers(udiId: row.UdiId, eudamedId: row.EudamedId, emtecId: row.EmtecId),
    externalId: row.ExternalId);
```

Order: `id` → `external_id` → each regulatory identifier as listed → title + type-plate
manufacturer → title + responsible manufacturer.

**A regulatory identifier is not unique in production.** Measured on the live catalog:

| lookup | matches | note |
| --- | --- | --- |
| `eudamed_id=04260192090319` | 4 | four catalog entries, all "elisa 300" |
| `eudamed_id=04045928000134` | 2 | "Perfusor Space" **and** "Perfusor Space PCA" |
| `emtec_id=217308` | 1 | "CX50 Ultrasound System" |

The second is the dangerous one: two different models under one UDI-DI, so taking the first
can attach a device to the wrong model. Each identifier is therefore tried **together with
the title** first — which narrows that case from 2 to exactly 1 — and only then on its own.
Where several records still match, the first is taken so the sync can proceed and the key is
appended to `ResourceLookup.AmbiguousMatches` for the caller to log.

### Regulatory identifiers

`ByRegulatory(label, value)` sends `filter[regulatory][{label}]`, documented in **both** the
standard and the enterprise spec. Only labels in `Regulatory.Labels` are accepted; the
server slices an unknown one away and then returns **no records**, which is
indistinguishable from "does not exist" — the one answer that makes a sync create a
duplicate. `Regulatory.Require` therefore rejects bad labels client-side.

The twelve labels are grouped by what they are good for as a key:

| Group | Labels | Use as a cascade key |
| --- | --- | --- |
| `Regulatory.DeviceIdentifiers` | `udi_id`, `eudamed_id`, `emtec_id`, `emtec_code`, `eudamed_di` | yes, in that order |
| `Regulatory.NomenclatureCodes` | `emdn_code`, `umdns_code`, `gmdn_code` | no — many models share one code |
| `Regulatory.Classifications` | `ce`, `ecri_risk_level`, `us_fda`, `eu_mdr` | no — risk classes, not identifiers |

`udi_id` and `eudamed_id` are UDI-DIs and name one model; `emtec_id`/`emtec_code` name one
emtec catalogue entry; `eudamed_di` is a Basic UDI-DI covering a device family, so it may
legitimately match several models — hence last.

The single-value `filter[udi]` shortcut, which matches `udi_id` **or** `eudamed_id` in one
request, is deliberately unused: it appears twice in `public.yaml` and **not at all** in
`enterprise.yaml`, so a cascade built on it would resolve nothing in enterprise mode and
silently create duplicates.

## The log format is a contract

`FileSyncLog` writes the lines that samedis-care-log-monitor reads, from another repository.
The format therefore lives in one place, `LogFormat`, and both sides go through it:

```csharp
LogFormat.Compose(at, LogFormat.Levels.Error, message)   // yyyy-MM-dd HH:mm:ss ERROR …
LogFormat.TryParse(line, out var entry)
LogFormat.FileName(DateTime.Now)                          // Logfile_2026-08-30.log
```

It used to be a const in the writer and a regular expression in the reader, with no test over
the round trip. That is worse than it sounds because of how the reader fails: a line it cannot
match is not an error to it, it is the continuation of the entry above. A format that had
drifted would have folded every `ERROR` into the text before it and reported a clean run — a
monitor gone blind looks exactly like a run with no problems.

`LogFormat.FileName` matters for the same reason. The tools built the name with
`ToShortDateString()`, which follows the machine's culture, so the same tool produced
`Logfile_30.08.2026.log` on one host and `Logfile_2026-08-30.log` on the next — and the monitor
carried six candidate date formats to find either.

## An empty config section means defaults

`ConfigStore.Load` fills every section that came back null before it returns, so a section
header with nothing under it behaves the same as one that is absent:

```yaml
auth:
  # noch nichts eingetragen
samedis:
  uri: "https://sync.samedis.care"
```

Without that, `config.Auth.Uri` throws. YamlDotNet does not treat an empty section like a
missing key — it *sets* the property, and the value it sets is null, overwriting the
initialiser on the config class. So the two spellings behaved differently, and since a
half-filled config.yml is the normal state while setting a tool up, the tools died on a bare
`NullReferenceException` naming nothing. The care each of them takes over YAML *syntax*
errors — line, column, a hint about Windows paths — had no counterpart here.

This is on by default, unlike `ignoreUnmatchedProperties`, because there is no third
behaviour to pick: a missing section already means defaults, so the only question is whether
the empty spelling agrees. Pass `fillNullSections: false` to see the file as it is.

What it fills: any readable and writable reference-typed property that is null and whose
type has a public parameterless constructor, plus arrays, created empty. It walks into the
value, into the elements of a sequence and into the values of a dictionary, so a null nested
under a section (`mail.smtp`), inside a list element (`tenants[].actimed_cust_ids`) or under
a dictionary value is filled too.

A section declared as a collection *interface* — `IList<T>`, `IReadOnlyList<T>`,
`ICollection<T>`, `IEnumerable<T>`, `ISet<T>`, `IDictionary<K,V>`, `IReadOnlyDictionary<K,V>`
— is filled with the obvious concrete type. Any other interface-typed section stays null:
picking an implementation there would be a decision for the consumer, not for this library.

What it leaves alone: **strings**, because a null string means "not configured" and an empty
one does not; properties without a setter; types with no parameterless constructor, which it
cannot build; dictionary *keys*; and a list element written as a bare `-`, which is an empty
entry rather than an empty section.

Also left alone, and the reason is *not* "it cannot be null": a `Nullable<T>` such as `int?`
is a value type that can be null, and a scalar written as `retries:` does lose its declared
default. There is nothing to restore it from, and null on a nullable scalar is a legitimate
value the way it is for a string.

Filling stops at 64 levels. That guards a config *type* whose shape is unbounded — `class A`
holding a `B` that holds an `A` — which the cycle check on instances cannot catch, because
each fill creates a fresh object it has never seen. Without the cap that shape overflowed the
stack on an entirely empty config file, and a `StackOverflowException` cannot be caught: the
process died silently, which is worse than the `NullReferenceException` this replaces.

A tool that keeps its own loader can apply it to an object it deserialized itself:

```csharp
var cfg = MyDeserializer.Deserialize<AppConfig>(yaml);
ConfigStore.FillNullSections(cfg);   // before anything dereferences a section
```

`spl-sync` and `fluke-sync` need exactly that: their `ConfigStore.Load` decrypts DPAPI
secrets via `cfg.Auth.ClientSecret` before returning, so an empty `auth:` throws inside the
loader itself.

## Sending mail

One call, three transports, chosen by configuration:

```csharp
var mailer = new Mailer(config.Mail, log, "MyTool");
await mailer.SendAsync(
    new MailMessage(from, mailer.Recipients(), subject, htmlBody, textBody),
    "daily report");
```

`MailSettings` is the shape the tools' `config.yml` already had, so an existing file keeps
working. A failed send is reported through `ISyncLog` and returns false rather than throwing:
sending a report is not what a sync is for, and a mail server that is down should not lose a
run that has already done its work.

`MailMessage` carries an HTML body plus an optional plain-text alternative;
`MailMessage.PlainText` builds the text-only case, which writes no empty HTML part — some
clients display the alternative they are given and show a blank mail.

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

### Testing a change in a consuming tool before publishing

A consumer can resolve the package from a local folder feed, so a library change can be
tried out end to end without merging or publishing anything. The consumer's `.csproj`
stays exactly as it will be committed, which means what you test is what ships.

```bash
# 1. build the package into a local feed
dotnet pack SamedisCare.Dotnet.sln -c Release -p:Version=0.3.0-rc.22 -o ~/.nuget/samedis-local

# 2. NuGet caches by version, so drop the cached copy before re-restoring
rm -rf ~/.nuget/packages/samediscare.{api,helper,mail}/0.3.0-rc.22

# 3. in the consuming tool
dotnet restore --force && dotnet build -c Release
```

The consumer needs a `nuget.config` pointing at `~/.nuget/samedis-local`. Keep that file
**out of version control** — CI and other developers resolve from nuget.org and would fail
on a path that only exists on one machine.

Step 2 is easy to forget: without it a repacked package of the same version is ignored and
the build keeps using the stale one. Alternatively use a `ProjectReference` for a faster
loop — that avoids packing and cache clearing entirely and lets you debug into the library
source, but it changes the consumer's `.csproj`, so it must never be committed.

Publish only when the consumer's CI needs to go green.

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
