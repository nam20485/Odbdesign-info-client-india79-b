# OdbDesignInfoClient — Salvage Plan: Server Reconnection & Plan Parity

**Date:** 2026-09-13
**Branch:** `nam20485` (builds clean: 0 errors)
**Method:** Three parallel research passes — (1) inventory of the app's connection layer, (2) ground-truth from the known-working prototype client (`odbdesign-3d-client-prototype`) plus the server repo (`OdbDesign`) **with live probes against the running server**, (3) full plan-docs-vs-implementation gap analysis.
**Live server (running, verified today):** `https://debian13vm.tail11ba79.ts.net` (Tailscale, k3s + Traefik) — REST 200 OK with credentials; gRPC `:50051` plaintext h2c reporting `SERVING`.

---

## Executive Summary

The app fails to connect for **four concrete, verified reasons** — none of them mysterious. The server moved from a local Docker deployment to a remote Tailscale host with an HTTPS-only Traefik ingress and enforced HTTP Basic auth; the client still points at `http://localhost:8888`, reads the wrong environment-variable names for credentials, and two of its response-shape assumptions no longer match the server. Fixing connectivity is a **small, contained change** (~6 files, mostly config + parsing).

Separately, the implementation is roughly **half of what the plan specified**, and some of what exists is dead-on-arrival — most notably, the data cache that was supposed to prevent re-fetching on every tab switch **never actually hits**, because its expiration timestamps are never set after a fetch. That single bug is the cause of the "fetches the design for every tab open" symptom you observed.

Recommended sequence:

| Phase | Theme | Size |
|---|---|---|
| **1** | Restore connectivity (config, auth, parsing, dead route) | S |
| **2** | Correctness quick wins (cache bug, error surfaces, reconnect refresh) | S–M |
| **3** | Plan-parity features (TreeDataGrid hierarchy, missing tabs, deep-linking, cross-probe, stackup from matrix) | L |
| **4** | Engineering quality (tests, StyleCop, versioning, perf, docs) | M |

---

# Part 1 — Getting It Working Again

## 1.1 Root-cause diagnosis (all verified live, 2026-09-13)

### Root cause 1 — The server moved; the client didn't (primary)

The app defaults to `http://localhost:8888` (`src/OdbDesignInfoClient/appsettings.json` → `Server:Host=localhost`, `RestPort=8888`, `UseHttps=false`; hardcoded fallback in `ServerConnectionConfig.cs:11-21` and `ServiceCollectionExtensions.cs:26`). Nothing listens on this workstation.

The server now runs remotely on `debian13vm.tail11ba79.ts.net` (Tailscale IP `100.118.225.119`) behind k3s + Traefik:

| Transport | Working address | Notes |
|---|---|---|
| REST | `https://debian13vm.tail11ba79.ts.net` (TLS :443 via Traefik) | Direct `:8888` is loopback-only on the server host — not reachable remotely |
| gRPC | `http://debian13vm.tail11ba79.ts.net:50051` | Plaintext h2c (LoadBalancer service); verified `Odb.Grpc.OdbDesignService/HealthCheck → SERVING` |

Server-side commit `43f2221` (2026-09-10) introduced this HTTPS-only ingress topology — the topology break.

### Root cause 2 — Auth: right scheme, wrong env-var names (and no UI)

The server enforces HTTP Basic auth per-route on all data endpoints (`DesignsController.cpp` / `FileModelController.cpp`; verified live: no credentials → **401**). The app *has* a working Basic-auth pipeline (`AuthHeaderHandler` → `Authorization: Basic …`), but `BasicAuthService.cs:33-34` reads:

- `ODB_AUTH_USERNAME` / `ODB_AUTH_PASSWORD`

while this workstation (per the working prototype's convention, set in `~/.config/environment.d/50-api-keys.conf`) exports:

- `ODBDESIGN_REST_USERNAME` / `ODBDESIGN_REST_PASSWORD`

So the app sends **no** credentials and every data request 401s. There is also no login UI anywhere — env vars are the only entry point.

### Root cause 3 — Response-shape drift breaks the Stackup tab

Live server responses vs. what the client parses:

| Endpoint | Server returns today | Client expects | Result |
|---|---|---|---|
| `GET /designs` | `{"filearchives":[{name, type, loaded}]}`, **204 when empty** | Envelope handled (`DesignService.cs:99-156`) | ✅ (204 handling untested) |
| `GET /filemodels/{n}/steps` | `{"steps":["step"]}` | `List<string>` + envelope fallback (`:893-915`) | ✅ |
| `GET /filemodels/{n}/steps/{s}/layers` | `{"layers":["board-outline.doc", …]}` | `List<string>` directly (`DesignService.cs:593`) | ❌ **JsonException → Stackup tab silently empty/stuck** |
| `GET /designs/{n}/components` | Bare array; `side:"Top"` (mixed case); `package.pinsByName` extra field | `List<ComponentDto>` (`refDes, partName, index, side, package{…}, part{…}`) | ✅ (case-insensitive enums already on) |
| `GET /designs/{n}/nets` | Bare array `[{name, index, pinConnections[…]}]` | `List<NetDto>` | ✅ |
| `GET /designs/{n}/drilltools` | **404 — route does not exist** | `ApiResponse<string>` (`IOdbDesignRestApi.cs:68`) | ❌ (moot — method is a stub returning `[]` anyway) |

Server commits driving the drift: `93fd699`/`60b00a6` (`/designs` clipped-view + cache), `27ec8ce` (ETag/304 conditional GETs), `24eba31` (background `RequestLoadDesign`).

### Root cause 4 — Step-name fallback is wrong

`MainViewModel.cs:275,293` falls back to step `"pcb"` when the steps list is empty — but e.g. `sample_design`'s only step is literally named `"step"`; any layers call with `"pcb"` returns *"step: 'pcb' not found"*. The normal path uses the real first step, so this only bites when the steps fetch failed — i.e., exactly when you're already debugging a broken connection.

### Non-issues (checked so you don't have to)

- **gRPC message size**: `ConnectionService.cs:161-166` already sets 100 MB receive — the ~27 MB `GetDesign` response fits. gRPC silently falls back to REST on failure anyway (`DesignService.cs:353-360`).
- **gRPC auth**: the server's gRPC endpoint currently requires none, so the client's missing `CallCredentials` is not a connectivity blocker (still a plan-parity item — Part 2, A10).
- **Unloaded designs**: `loaded:false` designs still serve `/designs/{n}/components` with 200 + data (verified live) — so no load-first flow is *required*. `POST /designs/{n}/load` (202, background warm-up) is an optional enhancement.
- **ETag/304**: only triggers if the client sends `If-None-Match`, which it doesn't. Non-issue today.
- **TLS**: the Traefik cert for `*.ts.net` is publicly valid; default `HttpClient` validation works (verified via curl).

## 1.2 The working connection recipe (ground truth)

This is exactly what the working prototype does (`odbdesign-3d-client-prototype/src`), all verified live today:

- **REST**: `https://debian13vm.tail11ba79.ts.net` + `Authorization: Basic base64(ODBDESIGN_REST_USERNAME:ODBDESIGN_REST_PASSWORD)`. Polly: 3 retries, exponential + jitter, transient-only. 30s timeout.
- **gRPC**: `http://debian13vm.tail11ba79.ts.net:50051`, plaintext; keepalive 30s ping/10s timeout; 150 MB max message; gzip. Custom `OdbDesignService/HealthCheck` (the standard `grpc.health.v1.Health` does **not** know this service name).
- **Endpoints actually used by the prototype**: `GET /designs`, `GET /filemodels/{n}/steps`, `GET /filemodels/{n}/steps/{s}/layers`, `GET /filemodels/{n}/matrix/matrix`, plus gRPC `GetDesign`, `GetLayerFeaturesStream`, `GetLayerFeaturesBatchStream`, `GetLayerSymbols`, `GetStandardFonts`, `RequestLoadDesign`, `HealthCheck`.

## 1.3 Required changes (file by file)

| # | File | Change | Why |
|---|---|---|---|
| 1 | `src/OdbDesignInfoClient/appsettings.json` | `Host=debian13vm.tail11ba79.ts.net`, `UseHttps=true`, `RestPort=443`, `GrpcPort=50051` | Point at the live deployment (RestPort can be ignored when UseHttps+Host are set, but keep it consistent) |
| 2 | `BasicAuthService.cs:33-34` | Read `ODBDESIGN_REST_USERNAME`/`ODBDESIGN_REST_PASSWORD` first, fall back to `ODB_AUTH_USERNAME`/`ODB_AUTH_PASSWORD` | Align with the workstation's exported names; keep backwards compat |
| 3 | `DesignService.cs:593` | Parse layers envelope-tolerantly (`{"layers":[…]}` or bare array) — mirror the existing steps fallback at `:893-915` | Fixes Stackup tab |
| 4 | `DesignService.cs:99-156` | Treat `204 No Content` on `/designs` as "empty list", not parse failure | Server returns 204 when no designs are loaded |
| 5 | `IOdbDesignRestApi.cs:68` + `DesignService.cs:815+` | Remove `/designs/{n}/drilltools` route + stub (or park behind a feature flag) | Route 404s on the current server; method is an empty stub anyway |
| 6 | `ServiceCollectionExtensions.cs` / `App.axaml.cs:73-78` | Apply `Server:TimeoutSeconds` to the `HttpClient` (currently dead config); implement the env-var override the doc comment at `:20-22` already promises | Config hygiene while in there |
| 7 | `MainViewModel.cs:275,293` | Remove the `"pcb"` fallback — surface an error if steps are unavailable instead of guessing | Wrong-step 404s masquerade as other failures |

**Follow-up (small, recommended in the same PR):** default `--auto-connect` to on (the Spec's acceptance criteria says "connects upon launch"; currently CLI-flag-only, `App.axaml.cs:110-124`).

## 1.4 Verification checklist

1. `dotnet build OdbDesignInfoClient.sln` — stays 0 errors.
2. With `ODBDESIGN_REST_*` env vars set: `dotnet run --project src/OdbDesignInfoClient -- --auto-connect` connects, `/designs` lists ~14 archives, Components/Nets tabs populate for `sample_design`.
3. Stackup tab shows 26 layers for `sample_design` / step `step`.
4. Log shows gRPC initialized (`Available: true`) and `GetDesign` used for components/nets; kill port 50051 path → REST fallback still works.
5. Unauthenticated run (env vars cleared) shows a **visible 401 error** in the UI (this currently requires the Phase 2 error-surface work to be meaningful — at minimum verify a log entry, not a silent empty list).
6. Re-enable one skipped integration test in `tests/OdbDesignInfoClient.IntegrationTests/ServerIntegrationTests.cs` against the live server as a smoke test.

---

# Part 2 — Plan vs. Implementation: Gap Analysis & Remaining Work

## 2.1 What the plan says about caching and tab lifecycle (your key concern)

The plan is unambiguous — data must be fetched **once per first tab activation** and **not** re-fetched on tab switches:

> **Architecture §4.2 (line 70):** "If the user switches tabs from 'Components' to 'Nets' and back, the data is **not re-fetched immediately, preserving the scroll position and expanded state**."

> **Architecture §5.1 (line 110):** "Each tab is **lazy-loaded; data is fetched only when the tab is first activated**."

> **API-Integration Guide §6.3:** "The DesignService **should cache the Step list and Layer matrix to avoid hitting the REST API on every tab switch**."

> **Implementation Spec, Phase 3:** "expanding a Component row triggers a lazy-load of its connected Nets and Pins" — lazy-load at the *child-row* level too.

> **Architecture §5.1 (Command Bar):** design change "triggers a global 'Context Change' event, clearing all grids and reloading"; "Refresh: **Force**-reloads data from the server" — i.e., re-fetch only on explicit user action.

## 2.2 The actual behavior — and the one bug that causes it

The symptom (re-fetch on every tab open) is **not** just missing caching — the cache layer exists (commit `8db396f "implement caching"`) but is **dead code for components/nets/stackup**:

- `DesignService.cs:47-49` initializes `_componentCacheRefresh`, `_netCacheRefresh`, `_stackupCacheRefresh` to `DateTime.MinValue`.
- After a successful fetch, **only `_designCacheRefresh` is ever updated** (line ~231, in `GetDesignsAsync`). The other three are assigned nowhere except the resets inside `ClearCache()` (`:882-884`).
- Therefore `IsComponentCacheExpired()` etc. (`:810-812`) are **always true**, and the cache lookups at `:310-313`, `:443-446`, `:578-580` **never hit**. Every Components/Nets/Stackup tab activation performs a full server fetch. Only the design-list cache works.
- Compounding it: `ClearCache()` (`:877`) has **zero callers** — no invalidation on design change, disconnect, or reconnect — and doesn't clear `_stackupCache` even when called.
- And even with a working cache: `MainViewModel.OnSelectedTabIndexChanged` re-fires `LoadCurrentTabDataAsync` on **every** switch (`MainViewModel.cs:262-268`); tab VMs have no loaded-once guard (`ComponentsTabViewModel.cs:56-75`); and `ApplyFilter` rebuilds the entire `ObservableCollection` on every load/keystroke (`ComponentsTabViewModel.cs:102-116`), destroying scroll position and expansion state — the exact things §4.2 requires preserving. Search is not debounced (Spec Phase 3 requires it).
- `docs/PR9-Review-Resolution.md` §5 claims this was "Already Fixed … each cache type already has its own timestamp" — the timestamps exist, but the fix is ineffective because they're never written.

## 2.3 A) Planned but entirely missing

| # | Planned item | Plan reference | Evidence it's missing |
|---|---|---|---|
| A1 | **TreeDataGrid hierarchical grids** (Component→Pins, Net→Features child rows) | Spec Phase 3; Arch §4.3; tech-stack.md | All grids are flat `DataGrid`; `Avalonia.Controls.TreeDataGrid` not referenced in any csproj. Child-row VMs exist (`ComponentRowViewModel.Pins`, `NetRowViewModel.Features`) but are never rendered |
| A2 | **5 of 11 planned tabs**: Pins, Vias, Step Hierarchy, Symbols Library, EDA Data | Arch §5.2; Dev-Plan Phase 4 | Only Components, Nets, Stackup, Drill Tools, Packages, Parts exist (`MainWindow.axaml:119-138`) |
| A3 | **Drill Tools / Packages / Parts data retrieval** | Arch §5.2 | `DesignService.cs:815-872` stubs return `[]` with TODOs; three tabs render permanently empty |
| A4 | **Deep-linking / hyperlink cells** (click Net name → switch tab, filter, flash row, IPC) | Spec Features §4; Arch §5.3 | Commands exist (`PinRowViewModel.NavigateToNet`, `NetFeatureRowViewModel.NavigateToComponent`) but bound to nothing in any `.axaml`; non-functional |
| A5 | **Viewer→Client cross-probe wiring** (peer = `odbdesign-3d-client-prototype`; see [Cross-Probing-Design.md](Cross-Probing-Design.md)) | Spec Phase 5; Arch §4.4 | `CrossProbeService.SelectionReceived` raised (`CrossProbeService.cs:171`) with **zero subscribers**; no `Dispatcher.UIThread` marshaling; incoming selection parsed then discarded; prototype has zero IPC code |
| A6 | **Viewer auto-launch + connection retry** | Arch §4.4 Lifecycle; Dev-Plan Phase 5 | Single 5-second connect attempt (`CrossProbeService.cs:45`), no launch, no retry |
| A7 | **ThemeService (Dark/Light + manual override)** | Spec Phase 4 | Only `RequestedThemeVariant="Default"` (`App.axaml:4`) |
| A8 | **Step selection UI / step hierarchy** | Dev-Plan Phase 2; Arch §5.2 | No step selector; hardcoded first/`"pcb"` (`MainViewModel.cs:275,293`) |
| A9 | **gRPC streaming data plane** (server-streaming components/nets) | Spec Phase 2; API Guide §5.1 | Only unary `GetDesign` used (`DesignService.cs:353-360, 488-495`); no `ResponseStream` usage anywhere. *Note: the plan's streaming RPCs don't exist in the server's proto either — see Deviations C4* |
| A10 | **gRPC authentication** (`CallCredentials.FromInterceptor`) | API Guide §2, §6.1 | gRPC channel has no credentials; auth exists only on REST |
| A11 | **Auto-connect on launch** | Spec Acceptance Criteria | CLI-flag only (`App.axaml.cs:110-124`) |
| A12 | **Server capabilities auto-discovery** | Spec Features §1 | Only a gRPC HealthCheck probe (`ConnectionService.cs:171-175`) |
| A13 | **Performance instrumentation & tests** (20k-net profiling, scroll/search latency, `Log.Information("Loaded {Count} nets in {Duration}ms")`) | Spec Phase 6 + Test Cases; Logging section | None present |
| A14 | **Integration tests / TestContainers / dummy IPC viewer** | Spec Phase 6; Arch §6 | 2 skipped `[Fact(Skip=…)]` placeholders; Testcontainers referenced but unused |
| A15 | **Swagger-driven Refit generation** | Spec Swagger/OpenAPI section | `IOdbDesignRestApi` hand-written; no swagger yaml in repo (server repo has `swagger/odbdesign-server-0.9-swagger.yaml`) |
| A16 | **File upload support** (`POST` upload) | API Guide §4.2; Spec Swagger coverage | No upload method or UI |
| A17 | **Dynamic column visibility; regex filter** | Spec Requirements line 99, Features §5 | Fixed columns; `string.Contains` only (`ComponentsTabViewModel.cs:104-109`) |
| A18 | **ADRs in /docs** | Spec Documentation | None exist |
| A19 | **StyleCop enforcement** | Spec Acceptance; tech-stack Code Quality | No analyzer package or config anywhere |

## 2.4 B) Implemented but below plan quality

| # | Issue | Plan requirement | Actual (evidence) |
|---|---|---|---|
| B1 | **Cache never hits — refetch every tab activation** (§2.2 above) | Arch §4.2; API Guide §6.3 | `DesignService.cs:47-49` timestamps never set on write; `ClearCache()` uncalled; collection rebuild destroys scroll/expansion; no debounce |
| B2 | **Error handling swallowed; error surface is dead code** | Spec Logging ("Error: Unhandled exceptions, API 500…"), Connectivity resilience | `ViewModelBase.SetError/HasError` never called anywhere; tab `LoadAsync` try/finally without catch; fire-and-forget `_ = LoadCurrentTabDataAsync()` (`MainViewModel.cs:258,266`) → unobserved task exceptions, silent failure |
| B3 | **Connection indicator is fake** | Spec Features §1 (Green/Red dot) | `ConnectionIndicator` Ellipse static Gray, no binding (`MainWindow.axaml:23-29`); `ViewerIndicator` same (`:102-108`) |
| B4 | **Status bar below plan** | Arch §5.1 (metrics "Loaded 15,000 components in 200ms", IPC status, server version) | Only `StatusMessage` + hardcoded `"v1.0.0"` (`MainWindow.axaml:111-114`) |
| B5 | **Reconnect doesn't refresh data** | Spec Acceptance ("connection restored … data should refresh automatically") | `MonitorHealthAsync` restores state (`ConnectionService.cs:226-243`) but `MainViewModel.OnConnectionStateChanged` only updates text (`MainViewModel.cs:112-126`) |
| B6 | **DesignService breaks its own abstraction** | Arch §4.2 ("abstracts the underlying transport") | Casts interface to concrete: `if (_connectionService is not ConnectionService …) throw` (`DesignService.cs:340-343, 475-478`) — gRPC path untestable with Moq |
| B7 | **Deep-link navigation silently no-ops** | Arch §5.3 steps 5-6 (load tab, highlight/flash, IPC) | `OnNavigated` only acts "if tab has data loaded" (`MainViewModel.cs:137-151`); no load, no highlight |
| B8 | **Init runs VM code off the UI thread** | — | `_ = Task.Run(async () => await mainViewModel.InitializeAsync(…))` (`App.axaml.cs:120-130`) mutates observables without dispatcher affinity |
| B9 | **Stackup data largely fabricated** | Arch §5.2 item 7 (thickness, material, dielectric from matrix) | `GetStackupAsync` fetches only layer names; Thickness always "0.000 mm", Material empty; layer Type guessed by substring heuristics (`DesignService.cs:591-607, 791-807`). **The real data exists** at `/filemodels/{n}/matrix/matrix` (verified live: `type`, `stackIndex`, `depthMm`, `color`, `polarity` per layer) — the prototype already consumes it |
| B10 | **Test coverage far below plan** | 80% Core coverage; tests for all ViewModels | 26 facts total: 19 DesignService REST-parsing, 5 config-URL formatting, 2 skipped. **Zero** tests for any ViewModel, ConnectionService, CrossProbeService, NavigationService, AuthHeaderHandler, or the gRPC path |
| B11 | **XML-doc enforcement undermined** | Spec Documentation; tech-stack | `Directory.Build.props:20` sets `NoWarn CS1591` (docs otherwise largely present) |
| B12 | **Central version management is dead config** | Spec Phase 1 | `ManagePackageVersionsCentrally=false`; props versions (Avalonia 11.1.5, Refit 8.0.0) diverge from actual csprojs (Avalonia 12.0.3, Refit 10.1.6); `Avalonia.Diagnostics 11.3.16` mixed with Avalonia 12.0.3 |
| B13 | **Dead/ignored config** | — | `CrossProbe:PipeName`/`AutoConnect` never read (pipe name hardcoded, `CrossProbeService.cs:13`); `Server:TimeoutSeconds` never applied; README says port 5000, actual default 8888 |
| B14 | **Design-list refresh is N+1** | Control-plane should be lightweight | `GetDesignsAsync` sequentially awaits `GetStepsAsync` per design on every refresh (`DesignService.cs:179-229`) |
| B15 | **No stale-response guard** | — | Rapid design changes race: fire-and-forget tab loads have no cancellation/sequence check (`MainViewModel.cs:252-268`); an older slow fetch can overwrite a newer tab's data |

## 2.5 C) Deviations (material only — mostly accept, don't churn)

1. **Repo/branch naming** (`OdbDesignInfoClient` vs plan's `OdbDesignClient`) — cosmetic.
2. **4-project layout** (Services split out, separate IntegrationTests) vs plan's 3 — defensible improvement; matches AGENTS.md.
3. **Default port**: Spec says `localhost:5000`; implementation uses 8888/50051 per REST-API.md (the accurate doc). Plan docs are internally inconsistent; fix README, don't chase 5000.
4. **gRPC contract**: API Guide §5.1 describes `GetComponents`/`GetNets` streaming RPCs that **don't exist in the server's proto** (server offers unary `GetDesign` + layer-feature streams). The client's fetch-whole-design approach is a pragmatic response to the real contract. Recommendation: update the plan doc, and treat A9 as "use layer-feature streams where they genuinely help (large layers), not as a rewrite".
5. **Tab build order**: Parts/Packages/DrillTools shells were built before Pins/Vias/Step/Symbols/EDA — Dev-Plan statuses remain all "Pending".
6. `/healthz/{live,ready,started}` (matches REST-API.md) vs Spec's `/health` — REST-API.md wins.

**History note:** implementation essentially stopped in Feb 2026 (PR #9 REST/JSON work); May 2026 activity was placeholder text and housekeeping. `REST-API-Client-Implementation-Plan.md` is the one plan doc that *was* executed faithfully (DTOs, deserialization, error handling, tests all present).

---

# Part 3 — Prioritized Backlog

## Phase 1 — Restore connectivity (S; do first, ~6 files)

1. Config: point `appsettings.json` at `https://debian13vm.tail11ba79.ts.net` + gRPC `:50051`; implement promised env override.
2. Auth: read `ODBDESIGN_REST_USERNAME/PASSWORD` (fall back to `ODB_AUTH_*`).
3. Layers envelope-tolerant parsing (Stackup tab fix).
4. 204-on-empty handling for `/designs`.
5. Remove dead `drilltools` route; remove `"pcb"` step fallback.
6. Apply `Server:TimeoutSeconds`; default auto-connect on.
7. Run the §1.4 verification checklist.

## Phase 2 — Correctness quick wins (S–M; highest value-per-line in the whole plan)

1. **Fix the cache bug**: set `_componentCacheRefresh`/`_netCacheRefresh`/`_stackupCacheRefresh` after each successful fetch (3 lines); call `ClearCache()` on design change/disconnect/reconnect and make it clear `_stackupCache`; move to per-entry timestamps.
2. **Load-once per tab**: add an `IsLoaded` guard to tab VMs so tab switches don't even consult the service; Refresh command bypasses it (that's the plan's "force-reload").
3. **Surface errors**: call `SetError` from tab loads; catch in `LoadAsync` instead of fire-and-forget swallowing; bind `HasError` to an error banner in each tab view.
4. **Reconnect → reload**: on Connected-after-Reconnecting, re-run `LoadCurrentTabDataAsync` (+ `ClearCache`).
5. **Stale-response guard**: CancellationTokenSource per design selection; cancel on change.
6. **Bind the connection/viewer indicators** to actual state (B3).
7. Debounce search input (Spec Phase 3).

## Phase 3 — Plan-parity features (L; order by user value)

1. **TreeDataGrid upgrade + hierarchy** (A1): swap flat DataGrids to `HierarchicalTreeDataGridSource<T>`; render Component→Pins and Net→PinConnections child rows; virtualization (plan requires 10k+ rows).
2. **Deep-linking** (A4): hyperlink cells in XAML; `OnNavigated` loads the target tab if needed, filters, highlights/flashes the row, sends the IPC highlight message (Arch §5.3).
3. **Real Stackup from `/filemodels/{n}/matrix/matrix`** (B9): thickness (`depthMm`), type, stack order, polarity — replaces the fabricated data; also fixes the layers-shape dependency properly.
4. **Wire the three stub tabs** (A3): Packages/Parts/DrillTools from `/designs/{n}/{packages|parts}` (both live-verified 200); DrillTools needs a server-side source decision (route doesn't exist — check tools file under `/filemodels/{n}/steps/{s}/…` or park the tab).
5. **Missing tabs** (A2): Pins, Vias (both derivable from `GetDesign` data client-side), Step Hierarchy (steps endpoint), Symbols Library (`/filemodels/{n}/symbols` — declared but unused), EDA Data (`/filemodels/{n}/steps/{s}/eda_data`).
6. **Cross-probing both directions** (A5/A6) — **fully designed in [Cross-Probing-Design.md](Cross-Probing-Design.md)**. Peer app is the existing **`odbdesign-3d-client-prototype`** (not a hypothetical viewer exe): named-pipe duplex JSON contract (`SelectionEvent` with `sourceApp`/`selectionType`/`selectionId` + design-match gating), prototype hosts the pipe, both apps fire *and* handle. Prototype-side work items included there (it has zero IPC today but already has `FeatureClicked`, `SelectComponentByName`, and `SelectNetByName` to build on). Milestones: M1 components, M2 nets + resilience, M3 pins/multi-select.
7. **Step selector UI** (A8).
8. **Login UI / credentials entry** (completes the auth story beyond env vars; also A10 gRPC credentials if the server ever enforces them).
9. **ThemeService** (A7).
10. **Preserve scroll/expansion across tab switches** (Arch §4.2) — filter as a view over cached rows instead of rebuilding collections.

## Phase 4 — Engineering quality (M; parallelizable)

1. ViewModel + ConnectionService + CrossProbeService + gRPC-path unit tests (B10); un-skip integration tests vs live server or Testcontainers (A14).
2. StyleCop (A19); remove `CS1591` suppression (B11); central package versions actually enforced (B12).
3. Perf instrumentation + thresholds (A13); status-bar metrics (B4).
4. Fix `DesignService`→`ConnectionService` concrete cast (B6) — extract an `IGrpcDesignClient` interface.
5. ADRs for: transport strategy (REST vs gRPC vs streams), remote-server topology + auth, TreeDataGrid (A18); update Dev-Plan statuses; fix README port (B13).
6. Optional: Swagger-driven Refit generation from the server repo's `swagger/odbdesign-server-0.9-swagger.yaml` (A15) — kills this whole class of contract drift permanently.

---

## Appendix — Key evidence references

**This repo (client):**
- Config/defaults: `src/OdbDesignInfoClient/appsettings.json`, `App.axaml.cs:73-78`, `ServerConnectionConfig.cs:11-21`, `ServiceCollectionExtensions.cs:26,33-87`
- Auth: `Api/BasicAuthService.cs:33-34`, `Api/AuthHeaderHandler.cs:29-36`
- REST surface: `Api/IOdbDesignRestApi.cs:14-86`
- Parsing/DTOs/cache: `DesignService.cs:32-50, 99-156, 231, 310-313, 340-343, 443-446, 475-478, 578-593, 810-812, 815-884, 893-915`
- Tab lifecycle: `MainViewModel.cs:112-126, 137-151, 158-192, 252-268, 270-293`; `ComponentsTabViewModel.cs:56-75, 102-116`
- Cross-probe: `CrossProbeService.cs:13, 45, 171`
- Tests: `tests/OdbDesignInfoClient.Tests/DesignServiceTests.cs` (19), `ServerConnectionConfigTests.cs` (5); `tests/OdbDesignInfoClient.IntegrationTests/ServerIntegrationTests.cs` (2 skipped)
- Cache-fix claim that didn't fix: `docs/PR9-Review-Resolution.md` §5

**Working prototype** (`odbdesign-3d-client-prototype/src`):
- `Desktop/appsettings.json:4,7` (endpoints), `Core/Configuration/OdbDesignSettings.cs:35-36`, `Core/Services/Implementations/RestDesignListService.cs:58,126,174,221`, `BasicAuthHandler.cs:19-26`, `GrpcChannelFactory.cs:34-62,69`

**Server** (`OdbDesign`, branch `development`):
- Ports: `OdbDesignLib/App/OdbDesignArgs.h:33-35`; auth: `OdbDesignLib/App/BasicRequestAuthentication.cpp:48-64` (per-route enforcement in `DesignsController.cpp`/`FileModelController.cpp`)
- API spec: `swagger/odbdesign-server-0.9-swagger.yaml` (v0.9, BasicAuth, ETag/304, 19 data GETs)
- Break-inducing commits: `43f2221` (Traefik HTTPS-only ingress), `27ec8ce` (ETag), `24eba31` (background load), `93fd699`/`60b00a6` (design cache/clipped view)

**Live probes (2026-09-13, from this workstation):** `/designs` → 200 envelope (14 archives, `sample_design` loaded); `/designs` without auth → 401; `…/sample_design/steps` → `{"steps":["step"]}`; `…/steps/step/layers` → `{"layers":[26 names]}`; `…/matrix/matrix` → rich layer objects; `/designs/sample_design/components` → 200 bare array (also 200 for an `loaded:false` design); `/designs/{n}/drilltools` → 404; `POST /designs/{n}/load` → 202; gRPC `:50051` → `HealthCheck: SERVING`.
