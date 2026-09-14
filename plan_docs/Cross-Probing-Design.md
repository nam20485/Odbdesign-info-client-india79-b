# Cross-Probing Design — OdbDesignInfoClient ⇄ OdbDesign3DClient (prototype)

**Date:** 2026-09-14 (rev 2 — shared-library decision incorporated)
**Status:** Proposed (companion to [OdbDesignInfoClient-Salvage-Plan.md](OdbDesignInfoClient-Salvage-Plan.md), Phase 3 item 6)
**Peer repos:**
- This app (the "grid viewer"): `OdbDesignInfoClient` — `/home/nam20485/src/github/nam20485/Odbdesign-info-client-india79-b`
- 3D viewer: `OdbDesign3DClient` — `/home/nam20485/src/github/nam20485/odbdesign-3d-client-prototype` (github: `intel-agency/odbdesign-3d-client-prototype`)

---

## 1. Goal

When a component or net is selected in either app, the matching feature is selected/highlighted in the other app, live. Both apps must be able to **fire** selection events and **handle** incoming ones. Events only route between apps that have the **same design loaded**.

Out of scope for v1: pin-level selection in the 3D scene (pins aren't pickable there today), multi-select sets, camera scripting. The contract reserves room for them.

## 2. What each side already has (verified)

### OdbDesignInfoClient (this repo)
- `CrossProbeService` (`src/OdbDesignInfoClient.Services/CrossProbeService.cs`) — **named-pipe client** to `"OdbDesignViewerPipe"`, newline-delimited JSON, duplex stream.
  - Outbound: ad-hoc `select`/`highlight` messages (`:86-110`) — snake_case, no design identity, no source id. Throwaway format (no live peer ever spoke it); superseded by the shared library.
  - Inbound: parses `event`/`entity_type`/`entity_id` → raises `SelectionReceived` (`:157-179`) — **zero subscribers** today.
  - Single 5s connect attempt, no retry (`:45`); pipe name hardcoded (`:13`) while `appsettings.json CrossProbe:*` is dead config.
- Row VMs already have the entities: components by `refDes`, nets by `name`, pin rows with component context (`ComponentRowViewModel`, `NetRowViewModel`, `PinRowViewModel`).

### OdbDesign3DClient (prototype)
- **Component selection (click)**: `IFeatureSelectionService.FeatureClicked` event carrying `ComponentFeatureInfo` (`Name` = RefDes) — `FeatureSelectionService.cs:100-153`; 3D highlight via material swap (`:214-249`).
- **Net selection (click)**: `INetPickingService.PickAt` → `NetPickHit(NetName, LayerName, …)` → `HandleNetSelected` (`MainViewModel.cs:222-227`) → inspector only (no 3D net highlight).
- **Programmatic inbound hooks already exist**: `MainViewModel.SelectComponentByName(refDes)` (`MainViewModel.cs:2575-2592`, used by the `--select-component` CLI flag) and `ComponentInspectorViewModel.SelectNetByName(netName)` (`ComponentInspectorViewModel.cs:91-97`). Gap: programmatic component select updates state but **does not apply the 3D highlight** (`ApplyHighlights` is private to the hit-test path, `FeatureSelectionService.cs:146-149`) — must be fixed for inbound cross-probe.
- **Design identity**: `DesignSelectionViewModel.SelectedDesign.Name` / `SelectedStep.Name` (singleton VM, `[ObservableProperty]`); `SelectionChanged`/`LayersLoaded` events carry `(DesignName, StepName)` — ready-made design-change signals. Comparison convention is `OrdinalIgnoreCase`.
- **Addressable data**: `ComponentDetailIndex.ByName` (refDes → `ComponentDetail` with `Pins`, `Nets`) — component, net, and refDes+pin composite all resolvable.
- **IPC**: none of any kind. Eventing pattern: DI singletons exposing `EventHandler<T>`, View marshals to UI thread via `Dispatcher.UIThread.Post`. DI/lifecycle: registrations in `UI/App.axaml.cs:108-220`, cleanup convention in `MainWindow.Closed`.

## 3. Transport & topology

**Decision: named pipe (duplex), newline-delimited JSON, UTF-8. 3D viewer hosts; grid viewer connects.**

- `System.IO.Pipes` works on all three target platforms (Unix domain socket under the hood on Linux/macOS) — both apps are .NET 10, no new dependencies beyond the shared library.
- Duplex `PipeDirection.InOut` means **one connection carries both directions** — each side simultaneously "fires and handles" over the same stream. No second connection needed.
- Prototype = `NamedPipeServerStream` host: matches this repo's existing client code, and supports the auto-launch story (grid viewer launches viewer exe → viewer hosts → viewer-independent startup order). 1:1 connection in v1 (single server instance, reconnectable); multi-client is a future iteration.
- Pipe name from config on both sides, default `OdbDesign.CrossProbe.v1`.
- **The entire wire contract, framing, gating rules, and pipe transports ship once in a shared library** (section 5) that both apps reference — the contract cannot drift because it is only defined once.

## 4. SelectionEvent contract (v1)

Every message is one JSON object per line. Common envelope:

```json
{
  "protocolVersion": 1,
  "messageType": "handshake | selectionEvent | deselect | designChanged | ping | pong",
  "sourceApp": "OdbDesignInfoClient | OdbDesign3DClient",
  "designName": "sample_design",
  "stepName": "step",
  "sequence": 42
}
```

`selectionEvent` adds:

```json
{
  "selection": {
    "selectionType": "Component | Net | Pin",
    "selectionId": "MTG1"
  },
  "action": "Selected | Cleared"
}
```

Field semantics:

| Field | Rule |
|---|---|
| `protocolVersion` | Receiver drops messages with unknown version (logs) — forward compatibility from day one |
| `sourceApp` | **Echo suppression, primary rule**: a receiver ignores any event whose `sourceApp` equals its own app id |
| `designName` | Both ends must have this design loaded or the event is dropped (logged, status-bar notice). Comparison `OrdinalIgnoreCase` (both repos' convention) |
| `stepName` | Informational in v1: ODB++ component/net files are step-scoped, but both apps effectively operate on the primary step. Handshake warns on mismatch; routing gates on design only |
| `sequence` | Per-app monotonic counter, for logging/ordering/diagnostics |
| `selectionType` | `Component` \| `Net` \| `Pin` (camelCase strings) |
| `selectionId` | Globally-unique-within-design id — see uniqueness rules below |

**selectionId uniqueness rules (per design):**

| selectionType | selectionId | Uniqueness | Status |
|---|---|---|---|
| `Component` | refDes, e.g. `MTG1` | Unique per design (prototype indexes `ByName` refDes, OrdinalIgnoreCase) | v1 |
| `Net` | net name, e.g. `GND` | Unique per design | v1 |
| `Pin` | composite `"{refDes}:{pinName}"`, e.g. `MTG1:1` | Pin names alone are NOT unique; composite is | v2 (contract reserved now) |

**Handshake** (sent by both ends immediately on connect):

```json
{"protocolVersion":1,"messageType":"handshake","sourceApp":"…","designName":"…","stepName":"…","sequence":0,
 "capabilities":["component","net"]}
```

- Mismatched design → connection stays up but events are dropped with a one-time user-visible notice ("Connected to 3D viewer, but it has *X* loaded — load *Y* to cross-probe"). Each end re-sends `designChanged` whenever its loaded design changes, so the gate re-evaluates without reconnecting.

**Echo suppression, secondary rule (the important one):** applying a remote selection programmatically must **not** raise an outbound event. Only user-initiated selection (row click, scene click) fires. `sourceApp` filtering is the belt; this is the suspenders — both are required to prevent select-storms.

**Deselect:** clicking empty space / clearing a row sends `{"messageType":"deselect"}`; receiver clears its highlight. Also suppressed from re-emit.

## 5. Shared contract library — `OdbDesign.CrossProbing`

**Decision (2026-09-14): the contract lives in a NuGet-consumable library that any client references. Apps implement library interfaces; models are defined in the library. Nothing about the protocol is hand-copied between apps.**

### Rev 1 — library project inside this repo

- New project `src/OdbDesign.CrossProbing/` (`OdbDesign.CrossProbing.csproj`, namespace `OdbDesign.CrossProbing`), added to `OdbDesignInfoClient.sln`; tests in `tests/OdbDesign.CrossProbing.Tests/`.
- Target `net10.0`, **zero UI/app dependencies** (no Avalonia, no CommunityToolkit, no server-client refs) — this discipline is what makes the later extraction mechanical. The only allowed dependencies are BCL + a logging abstraction (`Microsoft.Extensions.Logging.Abstractions`).
- Neutrally named (`OdbDesign.*`, not `OdbDesignInfoClient.*`) so it reads correctly from both apps and in its eventual standalone repo.

### Library contents

| Area | Contents |
|---|---|
| `Contract/` | The models: `CrossProbeMessage` (envelope), `SelectionEvent` (`SelectionType` + `SelectionId`), enums `CrossProbeMessageType`, `SelectionAction`, `SelectionType`; `CrossProbeApps` static ids (`OdbDesignInfoClient`, `OdbDesign3DClient`) |
| `Protocol/` | The single serialization authority: `CrossProbeProtocol` (line framing, camelCase/string-enums `JsonSerializerOptions`, tolerant read: unknown fields ignored, unknown `messageType`/`protocolVersion` → drop-with-reason); `protocolVersion` constant |
| `Rules/` | Pure, unit-tested policy the apps delegate to: `DesignMatchGate` (drop-if-design-differs, `OrdinalIgnoreCase`), `EchoSuppressor` (`ShouldProcess(sourceApp)` + the no-re-emit apply-mode state machine), sequence tracker |
| `Transport/` | `NamedPipeServerTransport` / `NamedPipeClientTransport`: accept/connect loops, duplex stream read/write, reconnect callbacks — all pipe mechanics in one place |
| `ICrossProbePeer` + `CrossProbePeerBase` | The app-facing service: `ConnectAsync`/`DisconnectAsync`, `IsConnected`, `ConnectionChanged`, `SelectionReceived`, `SendSelectionEventAsync`, `SendDeselectAsync`, `NotifyDesignChangedAsync`. Base class wires transport + protocol + rules together |
| `ICrossProbeHost` | **The interface each app implements** to plug itself in: `AppId`, `GetLoadedDesignName()`, `GetLoadedStepName()`, `Capabilities`, and `BeginApplyingRemoteSelection()`/`EndApplyingRemoteSelection()` (re-emit guard hooks). UI-thread dispatch stays in the app (host implementation), keeping the library dispatcher-free |

Division of responsibility: **library owns the wire and the rules; app owns identity, design state, UI marshaling, and wiring selection events to/from its own selection model.**

### Distribution

- **Rev 1 (library in this repo):** this app references it with a plain `ProjectReference`. The prototype consumes it as a **NuGet package**: `dotnet pack` → publish to the local NuGet feed (`local-nuget-service`), prototype adds `<PackageReference Include="OdbDesign.CrossProbing" Version="0.1.x" />`. (Fallback if feed friction: transient `dotnet pack` + local folder source.) Rebuild-and-republish on contract changes while both repos are in active dev.
- **Later phase (user-planned):** extract `src/OdbDesign.CrossProbing` into its own repo (e.g. `intel-agency/odbdesign-crossprobing`); the package id doesn't change, so both apps switch from project/folder references to feed references with **zero code changes**. From then on it versions independently (SemVer; breaking contract changes = major bump + `protocolVersion` bump).

## 6. OdbDesignInfoClient side — work items

1. **Create `src/OdbDesign.CrossProbing` library + tests** (per section 5): contract round-trip, framing, gate/suppressor rules, transport tests against an in-proc pipe pair.
2. **Rework `CrossProbeService` into a thin adapter** over the library: implement `ICrossProbeHost` (app id, loaded design/step from `MainViewModel` state, `Dispatcher.UIThread` marshaling, re-emit guard) and delegate to `CrossProbePeerBase` with the client transport. The old ad-hoc message format is deleted — no live peer ever spoke it. Keep the `ICrossProbeService` surface the ViewModels know, or migrate them to `ICrossProbePeer` directly (prefer the latter while ViewModels are being touched for firing/handling anyway).
3. **Config**: honor `CrossProbe:PipeName` / `CrossProbe:AutoConnect` from `appsettings.json` (currently dead, `CrossProbeService.cs:13`); default pipe name to the contract default.
4. **Reconnect loop**: replace the single 5s attempt (`:45`) with the library transport's reconnect + backoff; expose `ConnectionChanged` for the UI.
5. **Outbound firing**: in `ComponentsTabViewModel`/`NetsTabViewModel`, on user-driven grid selection change → build `SelectionEvent` (Component/refDes or Net/name) → `SendSelectionEventAsync`. The host's apply-mode guard ensures programmatic selection never emits.
6. **Inbound handling**: `MainViewModel` subscribes `SelectionReceived` → `Dispatcher.UIThread.Post` → verify design (gate) → navigate to the target tab (reuse the deep-link machinery from Salvage Plan A4/B7 — same code path) → select + scroll to + flash the row.
7. **Viewer indicator**: bind the `ViewerIndicator` ellipse (`MainWindow.axaml:102-108`) to `ConnectionChanged` + design-match state (Salvage Plan B3).
8. **Auto-launch (optional flag)**: `CrossProbe:LaunchViewer` + executable path → start prototype exe as child process, then connect; kill-or-leave on exit configurable.

## 7. OdbDesign3DClient (prototype) side — work items

1. **Consume `OdbDesign.CrossProbing`** from the local NuGet feed; register a singleton `CrossProbePeer` (library base class + **server** transport) in `UI/App.axaml.cs` `ConfigureServices`; implement `ICrossProbeHost` (app id, `DesignSelectionViewModel.SelectedDesign/SelectedStep` as design identity, `Dispatcher.UIThread` marshaling, re-emit guard). Start in `OnFrameworkInitializationCompleted`; cleanup in `MainWindow.Closed` per house convention.
2. **Fix programmatic highlight**: add a public selection API (e.g. `IFeatureSelectionService.SelectComponentByName(refDes)`) that routes through `ApplyHighlights`/`RestoreHighlights` so inbound selection visibly highlights in 3D — today `SelectComponentByName` updates state only (`FeatureSelectionService.cs:146-149` private path).
3. **Outbound firing**: subscribe `FeatureSelectionService.FeatureClicked` → emit `selectionEvent` (Component/refDes, design from `DesignSelectionViewModel.SelectedDesign.Name`); subscribe the net-pick path (`HandleNetSelected`) → emit Net/name. Host apply-mode guard prevents re-emit when applying remote selections. Single-select today → one event at a time; contract's `Pin` type reserved until pin picking exists.
4. **Inbound handling**: on Component → `SelectComponentByName` (highlight-fixed version) + optional camera focus (the old client's `zoom_to_fit` idea); on Net → `Inspector.SelectNetByName` (net 3D highlight doesn't exist yet — v2 candidate alongside pin picking); on `deselect` → `ClearSelection()`.
5. **Design identity**: handshake + `designChanged` wired to `DesignSelectionViewModel.SelectionChanged/LayersLoaded` (already carry `DesignName`/`StepName`).

## 8. Phasing & acceptance

| Milestone | Scope | Acceptance test |
|---|---|---|
| **M0 — Library rev 1** | `OdbDesign.CrossProbing` project + tests + packed to local feed; contract round-trip green | Library unit tests pass in both repos' CI; prototype builds against the package |
| **M1 — Contract + components** | Pipe plumbing via library, handshake, design gating, Component events both directions, deselect, no-re-emit guards, prototype highlight fix | Click component in 3D → row selects/flashes in grid (right tab, correct design only); click row in grid → component highlights in 3D; rapid alternate clicking causes no loop |
| **M2 — Nets + resilience** | Net events both directions, `designChanged`, reconnect with backoff, viewer indicator + mismatch notice, config honored | Net click in grid → net shown in inspector; net pick in 3D → net row selected; load different design in one app → events stop routing + notice appears; kill/restart either app → auto-reconnects |
| **M3 — Extensions & extraction** | Pin selection type (`refDes:pinName`) once prototype has pin picking, multi-select sets, camera/focus options; **extract library to its own repo/feed-published package** | Pin row click → pin-level highlight in 3D; multi-row select → multi-highlight; both apps consume the extracted package with zero code change |

## 9. Test strategy

- **Library unit tests (`tests/OdbDesign.CrossProbing.Tests/`)** — the bulk lives here, written once: contract serialization round-trips; framing (multi-line, partial lines); version-drop and unknown-field tolerance; `DesignMatchGate`; `EchoSuppressor` state machine; transport against an in-proc pipe pair (server + client streams in one test process).
- **Per-app unit tests**: each app's `ICrossProbeHost` implementation and VM wiring (e.g., "applying remote selection does not emit", "user click emits correct `SelectionEvent`") using the library's in-proc pipe pair or a fake transport.
- **Integration**: a tiny "echo peer" console app (or xUnit fixture hosting a fake peer) asserting fire→handle end-to-end per repo; this satisfies the original Spec's "dummy viewer" test-app idea without needing both GUIs running.
- **Manual smoke**: the M1/M2 acceptance tests above.

## 10. Decisions

1. **Who hosts** — prototype hosts (default chosen: preserves this repo's client code, supports auto-launch). Alternative: whoever starts first hosts, other connects.
2. **Pin type in v1 contract** — reserved but unused (prototype can't pick pins yet).
3. **Step-level gating** — design-level only in v1, `stepName` informational (mismatch warns, doesn't block).
4. **Shared contract library — DECIDED (2026-09-14)**: contract, protocol, rules, and transports live in `OdbDesign.CrossProbing`; apps implement library interfaces (`ICrossProbeHost`) and reference the models from the lib. Rev 1 is a library project in this repo (project reference here, NuGet-packaged to the local feed for the prototype); later phases extract it to a standalone repo without renaming, so consumers migrate with zero code change. ~~Duplicate the ~100-line contract in each repo for v1~~ (rejected: drift by construction).
