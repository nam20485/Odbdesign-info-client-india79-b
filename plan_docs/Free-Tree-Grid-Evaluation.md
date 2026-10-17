# Free / Open-Source Hierarchical Tree-Grid Evaluation for Avalonia 12

**Project:** OdbDesignInfoClient
**Scope:** Phase 3 "A1" — Component→Pins and Net→features hierarchical data grids with UI virtualization for 10k+ rows, preserved expansion state, and custom cell templates (e.g. a "navigate to net" hyperlink).
**Target framework:** .NET 10, **Avalonia 12.0.3** (Avalonia / .Desktop / .Themes.Fluent / .Fonts.Inter = 12.0.3; Avalonia.Controls.DataGrid = 12.0.0).
**Date:** 2026-09-14
**Author:** AI technical evaluation

---

## 1. Executive summary

The original plan assumed `Avalonia.Controls.TreeDataGrid`. As of Avalonia v12 (released 2026-04-07) that package is a **commercial, license-key-gated product** and its NuGet dependency (`AvaloniaUI.Licensing`) makes the build **hard-fail with `error AVLIC0001`** when no valid Pro/Enterprise key is present. The last free (MIT) release train was **11.1.x**, which targets Avalonia 11 and does **not** load under Avalonia 12 (verified: it was never compiled for the 12.x line; Avalonia 12 changed the binding/column APIs). We want to avoid buying a license, so TreeDataGrid-as-shipped-by-AvaloniaUI is off the table.

This project's needs are modest and well-served by a **permissively-licensed community fork of TreeDataGrid**. Two MIT-licensed forks are actively maintained and were **verified in this evaluation to compile against Avalonia 12.0.3** with the exact `HierarchicalTreeDataGridSource` / `HierarchicalExpanderColumn` / `TemplateColumn` API the plan needs.

### Final recommendation (ranked)

| Rank | Option | Verdict |
|---|---|---|
| **1 (recommended)** | **MIT TreeDataGrid fork: `wieslawsoltes/TreeDataGrid`** (packages `TreeDataGrid.Core` + `TreeDataGrid.Controls.Avalonia` v12.0.0.7) | Real tree-grid, MIT, Avalonia-12-native, verified compiles vs 12.0.3, most active maintenance. Lowest-risk path to the original plan. |
| **2 (near-equivalent drop-in)** | **MIT TreeDataGrid fork: `fidarit/TreeDataGrid.Avalonia`** v12.0.0 | Direct git fork of the original open-source repo; keeps the **exact** assembly name (`Avalonia.Controls.TreeDataGrid.dll`) and namespaces from the plan. Verified compiles vs 12.0.3. Slightly older last-activity than #1. |
| **3 (fallback, zero new deps)** | Self-built hierarchy (flattened list + indent + expander toggle) on the existing free `Avalonia.Controls.DataGrid` | No new package, but **DataGrid is deprecated** in v12 and you must hand-roll virtualization-safe flattening + expansion state. ~4-6 days. |
| **4** | `TreeView` + right-hand detail grid / `ItemsControl`+`Expander` | Free and trivial, but TreeView is not column-aligned tabular data and has known virtualization limits at 10k+ rows. Poor fit for a "data grid" requirement. |
| **5 (rejected)** | `Semi.Avalonia.TreeDataGrid` / `Material.Avalonia.TreeDataGrid` / `DynamicTreeDataGrid` / `CodeWF…` | These are **themes/helpers that depend on the commercial `Avalonia.Controls.TreeDataGrid`** (or are pinned to Avalonia 11), so they do **not** escape the license gate. Not viable without a key. |

**Bottom line:** Adopt option **1** (`wieslawsoltes/TreeDataGrid`) — swap the (never-added) commercial package reference for `TreeDataGrid.Core` + `TreeDataGrid.Controls.Avalonia`, register its Fluent theme, and build `HierarchicalTreeDataGridSource<ComponentRowViewModel>` / `<NetRowViewModel>` with a `HierarchicalExpanderColumn` over the existing `Pins` / `Features` collections. If the team instead wants byte-for-byte API compatibility with the original plan (same assembly + namespaces), option **2** (`fidarit/TreeDataGrid.Avalonia`) is a one-line equivalent. Integration estimate for either: **~1.5-2.5 days** including tests and polish.

---

## 2. Premise verification — is the Avalonia 12 TreeDataGrid license situation real?

**Yes, confirmed against primary sources.**

- **Avalonia 12 release** — 2026-04-07: <https://avaloniaui.net/blog/avalonia-12>. "Avalonia itself remains entirely open-source under MIT." TreeDataGrid is *not* part of the free framework.
- **NuGet package notice** (all v11.2.0+): *"As of 11.2.0 this package requires an Avalonia Accelerate license: <https://avaloniaui.net/accelerate>."* The current package `Avalonia.Controls.TreeDataGrid 12.3.1` declares a dependency on **`AvaloniaUI.Licensing (>= 3.1.2)`** — this is the package that emits the build-time `AVLIC0001` error. <https://www.nuget.org/packages/Avalonia.Controls.TreeDataGrid/12.3.1>
- **Avalonia docs (TreeDataGrid control)**: *"This control is available as part of Avalonia Pro or higher."* Setup instructions require adding `<AvaloniaUILicenseKey Include="YOUR_LICENSE_KEY" />` to the `.csproj`. <https://docs.avaloniaui.net/controls/data-display/structured-data/treedatagrid/> and the product page <https://avaloniaui.net/tree-data-grid> ("part of the Avalonia Pro and Enterprise tiers. Activation requires … a valid Pro or Enterprise licence.")
- **The original FOSS TreeDataGrid remains MIT but is dead-frozen on Avalonia 11.** GitHub issue "Upcoming License Change" (2024-09-14) and the October-2025 blog *Building a sustainable future for Avalonia* confirm the split: *"The original TreeDataGrid remains available … All are MIT licensed and can be used, forked, or maintained by anyone"* but *"no longer maintained and hasn't been compiled for 12.x previews."* <https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid/issues/307> , <https://avaloniaui.net/blog/building-a-sustainable-future-for-avalonia> , Avalonia discussion #20748 <https://github.com/AvaloniaUI/Avalonia/discussions/20748>
- **Repo state:** the upstream `AvaloniaUI/Avalonia.Controls.TreeDataGrid` GitHub repo is now **archived** (last push 2025-10-13, MIT license). <https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid>
- **The free "Table" promise:** the Oct-2025 blog commits to a new read-only open-source "Table" control; Avalonia **12.1** (2026-07-08) shipped **`TableView`** — free, MIT, columns + cell templates + row recycling — **but it has no hierarchy/expand-collapse** and is read-only, so it does **not** satisfy Phase 3 "A1". <https://avaloniaui.net/blog/release-12-1>
- **Community tier is not an escape hatch for this project:** the April-2026 "Retiring Accelerate" blog moves the free Community tier to **non-commercial use only**. OdbDesignInfoClient is a commercial desktop product, so the free tier would not cover it. <https://avaloniaui.net/blog/retiring-accelerate>

> **Note on the last free version:** `Avalonia.Controls.TreeDataGrid` 11.1.0 was the final MIT binary release (targets Avalonia 11). Because Avalonia 12 is a major release with breaking control/binding changes, the 11.1 package is **very unlikely to load under 12.0.3** — Avalonia's own maintainers state it was never compiled for 12.x. Do not plan around pinning 11.1.0; use an Avalonia-12-native MIT fork instead.

---

## 3. Comparison table (all options, all criteria)

Legend: ✅ yes · ⚠️ partial/unclear · ❌ no

| # | Option | License | Avalonia 12.0.3 compat | 10k+ virtualization | Arbitrary depth + expand/collapse state | Custom cell templates | Maintenance signal | Effort (this project) |
|---|---|---|---|---|---|---|---|---|
| **1** | `wieslawsoltes/TreeDataGrid` (`TreeDataGrid.Core` + `TreeDataGrid.Controls.Avalonia` v12.0.0.7) | ✅ **MIT** | ✅ Verified compiles (this eval, 0 errors). v12.0.0.7 min-Avalonia = 12.0.0 | ✅ Yes (TreeDataGrid uses recycling + `VirtualizingStackPanel`-equivalent row layout; 11.x was explicitly built for "huge datasets") | ✅ `HierarchicalTreeDataGridSource` + `HierarchicalExpanderColumn` with `IsExpanded` two-way binding, any depth | ✅ `TemplateColumn` (note: v12 fork signature is `TemplateColumn<TModel>` with `IDataTemplate`/resource-key) | ✅ **Last commit 2026-09-05** (this week), 50★, active v12 releases, split Core/UI for Uno porting | **~1.5-2.5 days** |
| **2** | `fidarit/TreeDataGrid.Avalonia` v12.0.0 | ✅ **MIT** | ✅ Verified compiles (this eval, 0 errors). nuspec: deps `Avalonia >= 12.0.1` (compatible with 12.0.3) | ✅ Yes (release notes: "Virtualization enhancements" PR #8; same engine as original) | ✅ Identical API to original plan | ✅ `TemplateColumn<TModel,TValue>` (original signature) | ✅ Direct fork of the FOSS repo; last commit 2026-06-04; 26★; co-credited Steven Kirk (original author) in nuspec authors | **~1-2 days** (truest drop-in) |
| **3** | Self-built hierarchy on existing free `Avalonia.Controls.DataGrid` 12.0.0 | ✅ **MIT** (confirmed by nuspec license) | ✅ Yes (same package the app already references) | ✅ Yes — DataGrid virtualizes rows by default (`DataGridRowsPresenter`) | ⚠️ Manual: flatten + indent column + `IsExpanded` toggle + rebuild flat view; no built-in tree | ✅ `DataGridTemplateColumn` for hyperlink cells | ❌ **DataGrid is deprecated** in v12 (docs: "DataGrid is deprecated! … use TreeDataGrid"); maintenance mode only | **~4-6 days** |
| **4** | `TreeView` + right detail grid, or `ItemsControl`+`Expander` | ✅ MIT (in-box) | ✅ Yes | ⚠️ TreeView nested-visual-tree has depth/scaling issues; not row-recycled like a grid (see Avalonia #6580, PR #14417) | ✅ TreeView does hierarchy natively; no tabular columns | ⚠️ Templatable but not column-aligned | ✅ In-framework | **~2-3 days**, but wrong UI shape for a "grid" |
| **5a** | `Semi.Avalonia.TreeDataGrid` (IRIHI) | ⚠️ MIT theme, **but** depends on commercial package | ❌ pulls `Avalonia.Controls.TreeDataGrid 12.0.0` (needs key) | — | — | — | IRIHI/Semi.Avalonia is reputable (1943★) | Rejected (doesn't avoid license) |
| **5b** | `Material.Avalonia.TreeDataGrid` v3.20.0 | ✅ MIT styles | ❌ styles for the commercial/legacy TreeDataGrid | — | — | — | AvaloniaCommunity | Rejected (theme only, no engine) |
| **5c** | `DynamicTreeDataGrid` (giard-alexandre) | ✅ MIT | ❌ augments Avalonia's TreeDataGrid (11.x era, last commit 2025-04) | — | — | — | Low (4★, stale) | Rejected |
| **5d** | `CodeWF.AvaloniaControls.TreeDataGrid` (dotnet9) | ✅ MIT | ❌ **fixed on the old free `Avalonia.Controls.TreeDataGrid 11.1`** → Avalonia 11 only | ✅ (if it loaded) | — | — | 6★ | Rejected (incompatible with A12) |
| **5e** | `TableView` (Avalonia core, v12.1+) | ✅ MIT | ⚠️ shipped in 12.1, not in 12.0.3 | ✅ row+cell recycling | ❌ **no hierarchy** | ✅ cell templates | ✅ official | Rejected for A1 (no tree) |
| **6** | `TreeDataGridEx` (wieslawsoltes, 11.1.1) | ✅ MIT | ❌ targets Avalonia 11 (its v12 successor is option 1) | ✅ | ✅ | ✅ | ✅ | Rejected in favor of option 1 |
| **7** | Fabulous.Avalonia.TreeDataGrid | ✅ Apache-2.0 | ⚠️ F# wrapper over TreeDataGrid engine | ✅ | ✅ | ✅ | fabulous-dev | Rejected: F#/Fabulous-idiomatic, wrong fit for this C#/MVVM app |

---

## 4. Per-option detail with citations

### Option 1 — `wieslawsoltes/TreeDataGrid` (RECOMMENDED)
- **Repo:** <https://github.com/wieslawsoltes/TreeDataGrid> (MIT, 50★, last commit 2026-09-05, **not a GitHub fork** — it is a maintained standalone tree-grid project).
- **NuGet:** `TreeDataGrid.Core` (12.0.0.7, framework-neutral sources/columns/sorting/selection/expansion) + `TreeDataGrid.Controls.Avalonia` (12.0.0.7, presentation layer). Metapackage id on NuGet is `TreeDataGrid.Controls.Avalonia`. <https://www.nuget.org/packages/TreeDataGrid.Controls.Avalonia/>
- **Avalonia 12 compat:** verified in this evaluation — a scratch .NET 10 project referencing `Avalonia 12.0.3` + `TreeDataGrid.Controls.Avalonia 12.0.0.7` compiled **`HierarchicalTreeDataGridSource<T>` + `HierarchicalExpanderColumn<T>` + `TextColumn<T,TValue>` + `TemplateColumn<T>` with 0 errors** (see §6). Min-Avalonia for v12.0.0.7 = 12.0.0, so 12.0.3 is satisfied.
- **Hierarchy model:** v12.0.0.7 deliberately **splits a framework-neutral `TreeDataGrid.Core` from the Avalonia UI layer** (PR #24, #25) — an architectural maturity signal and a hedge for a future cross-UI port. `HierarchicalExpanderColumn` supports any nesting depth; `IsExpanded` is two-way-bindable so expansion state survives scroll/filter.
- **Virtualization:** the control descends from the same recycling row-presenter lineage as the original TreeDataGrid, which Avalonia shipped as its "capable of displaying huge datasets" grid ("This control is already more performant than its analogues … supports virtualisation … great for displaying lots of data" — Avalonia announcement, <https://avaloniaui.net/blog/announcing-the-release-of-treedatagrid>). 10k+ rows is the design target.
- **API delta to note (only friction vs the original plan):** `TemplateColumn` in this fork is `TemplateColumn<TModel>(header, IDataTemplate cellTemplate, …)` (value-less generic, template supplied as `IDataTemplate` or resource key) — slightly different from the 11.x `TemplateColumn<TModel,TValue>(header, binding, resourceKey)`. This is trivial to adapt. Theme include is `avares://TreeDataGrid.Avalonia/Themes/Fluent.axaml`.
- **Maintenance:** single very active Avalonia-ecosystem maintainer (Wiesław Šoltés — also authors PanAndZoom 479★, VelloSharp, Avalonia.Xaml.Behaviors). Open issues are feature requests + a "Port to Uno" epic, not crashes.

### Option 2 — `fidarit/TreeDataGrid.Avalonia` (best drop-in alternative)
- **Repo:** <https://github.com/fidarit/TreeDataGrid.Avalonia> (MIT, 26★, **a direct fork of `AvaloniaUI/Avalonia.Controls.TreeDataGrid`**, parent = the archived FOSS repo). Last commit 2026-06-04.
- **NuGet:** `TreeDataGrid.Avalonia` v12.0.0. <https://www.nuget.org/packages/TreeDataGrid.Avalonia/> nuspec: `MIT`, authors *"Fidarit Mullayanov, Steven Kirk"*, dependency `Avalonia >= 12.0.1` (net8.0 & net10.0 targets) — i.e. **works with 12.0.3**, and keeps the **original assembly identity `Avalonia.Controls.TreeDataGrid.dll`** and the **original `Avalonia.Controls.Models.TreeDataGrid` namespace**.
- **API:** release 11.3.1→12.0.0 "Migrate to Avalonia 12" (PR #29) plus accumulated perf/virtualization work (PR #8 "Virtualization enhancements", PR #22 "Optimizations"). Because it is the literal fork, `HierarchicalTreeDataGridSource`, `HierarchicalExpanderColumn`, `TextColumn<TModel,TValue>` and `TemplateColumn<TModel,TValue>` keep the exact 11.x signatures — the plan's original code transfers with essentially **no code changes**, only the package reference and theme URI change. Theme include is the original `avares://Avalonia.Controls.TreeDataGrid/Themes/Fluent.axaml`.
- **Verified:** compiled against Avalonia 12.0.3 with **0 errors** in this evaluation (§6).
- **Maintenance:** one external maintainer; less recent activity than option 1, but co-credit with the original author and a clean fork lineage make it the lowest-migration-risk choice.

### Option 3 — Self-built hierarchy on the existing free `Avalonia.Controls.DataGrid`
- **License/compat:** `Avalonia.Controls.DataGrid 12.0.0` is **MIT** (confirmed via nuspec in §6) and is the package the app already references in `src/OdbDesignInfoClient/OdbDesignInfoClient.csproj:25` and uses in `ComponentsTabView.axaml` / `NetsTabView.axaml`. **No new dependency.**
- **How it would work:** keep `ObservableCollection<ComponentRowViewModel>` but wrap it in a **flattened** view-model list where each row carries a `Depth` (indent) and `HasChildren`/`IsExpanded` flag; render a `DataGridTemplateColumn` with a `ToggleButton`/chevron that mutates `IsExpanded` and re-projects the flattened list. This is exactly the "flatten hierarchical data … the virtualizing panel manages all rows directly" pattern Avalonia's own performance doc recommends, and how Avalonia's `TreeView` works internally. <https://docs.avaloniaui.net/docs/app-development/performance>
- **Virtualization:** DataGrid recycles rows via `DataGridRowsPresenter` (`LoadingRow`/`UnloadingRow` recycling events) — <https://api-docs.avaloniaui.net/docs/T_Avalonia_Controls_DataGrid> — so 10k+ rows virtualize as long as the grid is **not** placed inside an infinite-height `StackPanel` (a documented caveat: <https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid/issues/58>).
- **Why only rank 3:** (a) Avalonia docs mark **DataGrid as deprecated** — *"DataGrid is deprecated! For read-only tabular data … use TableView. For advanced editing … use TreeDataGrid"* (<https://docs.avaloniaui.net/controls/data-display/structured-data/datagrid>) — so you'd be investing in a maintenance-mode control; (b) the flatten/re-flatten expansion logic, indentation rendering, and "keep scroll position on expand" behavior are all **hand-rolled** and are the classic source of bugs; (c) sorting + filtering + cross-probe interaction get noticeably more complex on a manually-flattened grid. Estimated ~4-6 days vs ~2 for a fork.

### Option 4 — `TreeView` + detail grid, or `ItemsControl`+`Expander`
- In-box, MIT, no new deps. `TreeView` renders hierarchy natively and lets you bind `IsExpanded`. But it produces **not** column-aligned tabular rows (the requirement is "hierarchical data *grids*"), and Avalonia's `TreeView` builds nested visual trees with documented scaling limits (~400-450 nesting levels; slow with many loaded objects) — <https://github.com/AvaloniaUI/Avalonia/issues/6580> and PR #14417 <https://github.com/AvaloniaUI/Avalonia/pull/14417>. The performance doc lists TreeView as "virtualized by default" but real-world reports (issue #6580, comment: *"The built-in treeview is not virtualized"*) warn about 10k+ nested rows. **Wrong UI shape + uncertain 10k+ scaling → rank 4.** A hybrid (TreeView on left, a real `DataGrid`/`TableView` detail pane on right) is a legitimate product redesign if a tree-grid is ever dropped, but it changes the requested UX.

### Option 5 — community/other suites that do NOT escape the license (rejected)
- `Semi.Avalonia.TreeDataGrid` — IRIHI theme; nuspec depends on **`Avalonia.Controls.TreeDataGrid 12.0.0`** (the commercial engine) → still needs a key. <https://www.nuget.org/packages/Semi.Avalonia.TreeDataGrid/>
- `Ursa` / `Irihi.Ursa` (<https://github.com/irihitech/Ursa.Avalonia>, MIT, 1568★) — code search returns **0** TreeDataGrid references; it does not ship a tree data grid. Good for other controls, not for A1.
- `Material.Avalonia.TreeDataGrid` (AvaloniaCommunity, MIT) — **styles only** for TreeDataGrid; no engine. <https://www.nuget.org/packages/Material.Avalonia.TreeDataGrid/>
- `DynamicTreeDataGrid` (MIT) — helper layer over Avalonia TreeDataGrid, stale (last commit 2025-04, 11.x era).
- `CodeWF.AvaloniaControls.TreeDataGrid` (dotnet9, MIT) — explicitly *"fixed on the free `Avalonia.Controls.TreeDataGrid 11.1`"* → Avalonia 11 only. <https://www.nuget.org/packages/CodeWF.AvaloniaControls.TreeDataGrid/>
- **Licensing note:** no GPL/copyleft problem in this space — all viable candidates are MIT/Apache-2.0. (Flagged for the record: Avalonia's *commercial* TreeDataGrid license is what gates it; the *framework* stays MIT.)

---

## 5. Recommended implementation path (Option 1; Option 2 is a one-line swap)

Goal: Component→Pins and Net→features as expandable tree rows on 10k+ records, expansion state preserved, with a "navigate to net" hyperlink cell (pin rows) and a "navigate to component" hyperlink cell (net-feature rows). The repo already has the right shape for this.

### 5.1 Existing building blocks (already present — reuse as-is)
- **Component rows** — `src/OdbDesignInfoClient.Core/ViewModels/ComponentsTabViewModel.cs`:
  - `ComponentRowViewModel` already exposes `Pins : ObservableCollection<PinRowViewModel>` and an `[ObservableProperty] bool IsExpanded` (≈ line 208-224). These map **1:1** onto `HierarchicalTreeDataGridSource<ComponentRowViewModel>` + `HierarchicalExpanderColumn<ComponentRowViewModel>(..., x => x.Pins, x => x.IsExpanded)`.
  - `PinRowViewModel` exposes `Name`, `Number`, `NetName`, `ElectricalType`, and a `NavigateToNetCommand` (`[RelayCommand] public void NavigateToNet()`).
- **Net rows** — `src/OdbDesignInfoClient.Core/ViewModels/NetsTabViewModel.cs`:
  - `NetRowViewModel` already exposes `Features : ObservableCollection<NetFeatureRowViewModel>` + `[ObservableProperty] bool IsExpanded`.
  - `NetFeatureRowViewModel` exposes `FeatureType`, `Id`, `ComponentRef`, and a `NavigateToComponentCommand`.
- **Views** — `src/OdbDesignInfoClient/Views/ComponentsTabView.axaml` (DataGrid at row 57-74, `<DataGrid.Columns>` with RefDes/Part Name/Package/Side/Rotation/X/Y) and `NetsTabView.axaml` (same pattern). These `DataGrid` elements become `TreeDataGrid` elements bound to a `Source`.
- **App.axaml** — `src/OdbDesignInfoClient/App.axaml` registers `<FluentTheme/>`; add the fork's theme `StyleInclude`.
- **DI** — `src/OdbDesignInfoClient/App.axaml.cs` (ViewModels already registered).

### 5.2 Steps

1. **csproj** — in `src/OdbDesignInfoClient/OdbDesignInfoClient.csproj`, **do not** add the commercial `Avalonia.Controls.TreeDataGrid`. Instead add (Option 1):
   ```xml
   <PackageReference Include="TreeDataGrid.Core" Version="12.0.0.7" />
   <PackageReference Include="TreeDataGrid.Controls.Avalonia" Version="12.0.0.7" />
   ```
   (Option 2 alternative, single line): `<PackageReference Include="TreeDataGrid.Avalonia" Version="12.0.0" />`
   No `AvaloniaUILicenseKey` item is needed — that is the entire point of using an MIT fork. Keep or remove `Avalonia.Controls.DataGrid` (it can stay; the two controls coexist).

2. **Theme registration** — `src/OdbDesignInfoClient/App.axaml`, inside `<Application.Styles>` after `<FluentTheme/>`:
   ```xml
   <StyleInclude Source="avares://TreeDataGrid.Avalonia/Themes/Fluent.axaml"/>
   ```
   (Option 2: `avares://Avalonia.Controls.TreeDataGrid/Themes/Fluent.axaml`).

3. **Add a tree source to each ViewModel** — in `ComponentsTabViewModel.cs`, expose an `ITreeDataGridSource` built from the already-existing `Components` collection. Because the row VMs already carry `IsExpanded` + child collections, this is a small, localized addition (illustrative; namespace for the fork's columns/sources is `Avalonia.Controls.Models.TreeDataGrid`):
   ```csharp
   public ITreeDataGridSource<ComponentRowViewModel> Source { get; }

   // in constructor (after Components is populated / re-created on filter):
   Source = new HierarchicalTreeDataGridSource<ComponentRowViewModel>(Components)
   {
       Columns =
       {
           new HierarchicalExpanderColumn<ComponentRowViewModel>(
               new TextColumn<ComponentRowViewModel, string>("RefDes", x => x.RefDes),
               childrenSelector: x => x.Pins,
               isExpandedSelector: x => x.IsExpanded),
           new TextColumn<ComponentRowViewModel, string>("Part Name", x => x.PartName),
           new TextColumn<ComponentRowViewModel, string>("Package", x => x.Package),
           new TextColumn<ComponentRowViewModel, string>("Side", x => x.Side),
           new TextColumn<ComponentRowViewModel, double>("Rotation", x => x.Rotation),
           new TextColumn<ComponentRowViewModel, double>("X", x => x.X),
           new TextColumn<ComponentRowViewModel, double>("Y", x => x.Y),
           // Hyperlink "navigate to net" cell lives on child PinRowViewModels; see 5.3
       },
   };
   ```
   For the pin-level columns the grid binds the child model type; the expander column drives `Pins`. Because `IsExpanded` is an `[ObservableProperty]`, expansion state is preserved across re-projection/filter and (with a stable source or explicit restore) scroll. `NetsTabViewModel.cs` mirrors this with `NetRowViewModel` + `x => x.Features`.
   > With Option 1's `TemplateColumn<TModel>` shape, template columns take an `IDataTemplate` / resource key rather than a `TValue` binding; with Option 2 the classic `TemplateColumn<TModel,TValue>(header, binding, "ResourceKey")` compiles directly (both verified).

4. **Custom hyperlink cell ("navigate to net")** — for the Pin child rows in Components, and Component child rows in Nets, add a `TemplateColumn` whose `DataTemplate` binds the existing `[RelayCommand]`:
   ```xml
   <DataTemplate x:DataType="vm:PinRowViewModel">
       <TextBlock>
           <Hyperlink Command="{Binding NavigateToNetCommand}">
               <Run Text="{Binding NetName}"/>
           </Hyperlink>
       </TextBlock>
   </DataTemplate>
   ```
   (`NetFeatureRowViewModel` → `NavigateToComponentCommand`/`ComponentRef` is the symmetric case.) This reuses the commands already present in the VMs — no new navigation plumbing.

5. **Swap the view control** — in `ComponentsTabView.axaml` (row 57) and `NetsTabView.axaml`, change `<DataGrid ItemsSource=… Columns=…>` to `<TreeDataGrid Source="{Binding Source}" …>` (XAML-columns style if you prefer to keep column defs in XAML — the fork supports XAML-defined columns on v12; or code-behind `Source` as in step 3). Keep the filter bar, error banner, loading overlay, and status bar unchanged.

6. **Performance guardrails** — ensure the `TreeDataGrid` is inside a sized container (the Grid row is already `*`, i.e. bounded height), **not** inside a `StackPanel` that hands it infinite height, otherwise row virtualization is defeated (documented DataGrid/TreeDataGrid caveat).

7. **Tests** — add unit tests in `tests/OdbDesignInfoClient.Tests/` for the source construction: assert `Source` exposes the right column count, that expanding a component surfaces its `Pins`, and that toggling `IsExpanded` flips child-row visibility. (The VMs already have `IsExpanded` and populated `Pins`/`Features`, so tests are cheap.)

8. **Documentation** — add an ADR in `plan_docs/` recording "use MIT community TreeDataGrid fork instead of commercial Avalonia.Controls.TreeDataGrid" and update the Technology Stack row in `AGENTS.md` if TreeDataGrid is re-listed there.

### 5.3 Effort estimate
| Task | Estimate |
|---|---|
| Package swap + theme registration (steps 1-2) | 0.5 h |
| Add `Source` to both VMs (step 3) | 0.5 day |
| Hyperlink cell templates (step 4) | 2-3 h |
| View swap + column re-wiring (step 5) | 0.5 day |
| Perf guardrail check + virtualization smoke test at 10k rows (step 6) | 2-3 h |
| Unit tests (step 7) | 0.5 day |
| **Total** | **~1.5-2.5 days** |

Risk to watch: these are **community forks** — pin the exact NuGet version, and keep the deprecation-proof architecture (Clean-Arch: source building is in the Core VM layer, so if a fork ever stalls you can swap forks or fall back to option 3 with only the view/VM source wiring changing).

---

## 6. Verification performed in this evaluation (build evidence)

A throwaway .NET 10 library referencing `Avalonia 12.0.3` (the app's exact version) was compiled against each candidate with a `HierarchicalTreeDataGridSource<T>` + `HierarchicalExpanderColumn<T>` + `TextColumn<T,TValue>` + `TemplateColumn` configuration mirroring the Component→Pins use case.

- **Option 1 (`TreeDataGrid.Controls.Avalonia 12.0.0.7`): `0 Error(s)`** — after adapting `TemplateColumn` to the fork's `TemplateColumn<TModel>(header, IDataTemplate/resourceKey)` signature. Assembly: `TreeDataGrid.Avalonia.dll`.
- **Option 2 (`TreeDataGrid.Avalonia 12.0.0`, fidarit): `0 Error(s)`** — with the **original** 11.x signatures intact (`TemplateColumn<Row,string>("Net", x=>x.Detail, "NetCellTemplate")` compiled unchanged). Assembly retains the name `Avalonia.Controls.TreeDataGrid.dll` → true drop-in.
- **`Avalonia.Controls.DataGrid 12.0.0`** nuspec (already in use): `license type="expression">MIT`, dep `Avalonia >= 12.0.0`, **no `AvaloniaUI.Licensing` dependency** → confirmed still free in v12 (but deprecated).
- **`Semi.Avalonia.TreeDataGrid 12.0.0`** nuspec: depends on `Avalonia.Controls.TreeDataGrid 12.0.0` (commercial) → confirms option 5a does not avoid the gate.

This confirms the premise (commercial package pulls `AvaloniaUI.Licensing` → `AVLIC0001`) and confirms that options 1 and 2 satisfy the exact Phase 3 "A1" API needs under Avalonia 12.0.3 while remaining MIT.

---

## 7. Primary-source citations

- Avalonia 12 release: <https://avaloniaui.net/blog/avalonia-12>
- Accelerate licensing model change (free core + commercial Pro, TreeDataGrid history): <https://avaloniaui.net/blog/building-a-sustainable-future-for-avalonia>
- Retiring Accelerate (Community → non-commercial only, Pro/Enterprise tiers): <https://avaloniaui.net/blog/retiring-accelerate>
- Avalonia 12.1 release (new free read-only `TableView`): <https://avaloniaui.net/blog/release-12-1>
- TreeDataGrid control docs (requires Pro, license key item): <https://docs.avaloniaui.net/controls/data-display/structured-data/treedatagrid/>
- TreeDataGrid v12 breaking changes: <https://docs.avaloniaui.net/controls/data-display/structured-data/treedatagrid/breaking-changes-v12>
- DataGrid docs (marked deprecated): <https://docs.avaloniaui.net/controls/data-display/structured-data/datagrid>
- Performance/virtualization guidance (flatten hierarchy, DataGrid recycles rows): <https://docs.avaloniaui.net/docs/app-development/performance>
- FAQ (what is/isn't commercial): <https://docs.avaloniaui.net/tools/faq>
- Commercial TreeDataGrid NuGet (AvaloniaUI.Licensing dep, "requires Accelerate license as of 11.2.0"): <https://www.nuget.org/packages/Avalonia.Controls.TreeDataGrid/12.3.1>
- Original FOSS TreeDataGrid repo (archived, MIT, last commit 2025-10): <https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid>
- Upcoming License Change issue (dual-license announcement, "MIT version remains available"): <https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid/issues/307>
- "Is there a TreeDataGrid compatible with Avalonia 12?" discussion (maintainer options incl. "pull the source into your project" / "fork"): <https://github.com/AvaloniaUI/Avalonia/discussions/20748>
- Call for maintainers (legacy TreeDataGrid): <https://github.com/AvaloniaUI/Avalonia/discussions/19653>
- TreeDataGrid announcement (built for huge datasets, virtualization): <https://avaloniaui.net/blog/announcing-the-release-of-treedatagrid>
- **Option 1 fork:** repo <https://github.com/wieslawsoltes/TreeDataGrid> · NuGet <https://www.nuget.org/packages/TreeDataGrid.Controls.Avalonia/> · core <https://www.nuget.org/packages/TreeDataGrid.Core/>
- **Option 2 fork:** repo <https://github.com/fidarit/TreeDataGrid.Avalonia> · NuGet <https://www.nuget.org/packages/TreeDataGrid.Avalonia/>
- Rejected-theme deps: Semi <https://www.nuget.org/packages/Semi.Avalonia.TreeDataGrid/> · Material <https://www.nuget.org/packages/Material.Avalonia.TreeDataGrid/> · Ursa <https://github.com/irihitech/Ursa.Avalonia> · CodeWF <https://www.nuget.org/packages/CodeWF.AvaloniaControls.TreeDataGrid/> · DynamicTreeDataGrid <https://www.nuget.org/packages/DynamicTreeDataGrid/>
- TreeView scaling issues: <https://github.com/AvaloniaUI/Avalonia/issues/6580> · <https://github.com/AvaloniaUI/Avalonia/pull/14417>
- DataGrid virtualization/recycling: <https://api-docs.avaloniaui.net/docs/T_Avalonia_Controls_DataGrid> · infinite-height caveat <https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid/issues/58>
