# Task: move design reads to gRPC, refresh vendored protos, then conform

Source of truth: `docs/component-connectivity-client-handoff.md` (§5b, §7).

Your `ComponentDetailBuilder.cs` keying is already correct — `ResolveComponent` takes
`uint Index` with no `byId` path (`:429-435`), and `byName` uses `StringComparer.Ordinal`
(`:158`). Nothing to fix there. Two items remain.

## Part A — REST → gRPC for design reads (largest item, own PR) — DONE (F3)

Done in the F3 transport-switch PR: every content read (design matrix, step headers,
EDA data, layer names, symbols, components/nets/packages/parts/drill tools) now derives
from the cached gRPC `Design.FileModel`. REST keeps the discovery plane only
(`/filemodels`, `/filemodels/{name}/steps`, `/healthz/*`) — the per-design step-name
list has no cheap gRPC equivalent (`GetDesign` downloads the full file model, which is
prohibitive for list views). The per-resource REST fallbacks for
components/nets/packages/parts were deleted per D5, and with them the hand-mirrored
`Api/Dtos/` shapes (including the transitional `MatrixDto`).

**Transitional exception (recorded 2026-10-14, parity-rebuild PR #22):** ahead of this
transport switch, the client added REST reads for `matrix/matrix`, `steps/{step}/stephdr`,
and `steps/{step}/eda_data` (existing server routes), plus the hand-mirrored
`Api/Dtos/MatrixDto.cs` — the parity rebuild needed authoritative stackup/EDA values
before the switch landed. **Resolved by the F3 switch:** the already-downloaded gRPC
`Design.FileModel` carries all three sources (`FileArchive.matrixFile`,
`StepDirectory.stephdrfile`, `StepDirectory.edatafile`), the routes and `MatrixDto` are
deleted, and no other `Api/Dtos/` growth occurred in the meantime.

## Part B — refresh vendored protos — DONE (F6 folded into F3)

Replaced `protoc/` from the server repo's `OdbDesignLib/protoc/` +
`OdbDesignServer/protoc/grpc/` in the F3 PR. The refresh gained `GetStandardFonts`,
`RequestLoadDesign`, `LoadStatus`, and `include_normalized_lists` in
`protoc/grpc/service.proto` (plus the cosmetic `cc_enable_arenas` option on the lib
protos). `include_normalized_lists` stays unset everywhere: the product-model path
reconstructs collections from the FileModel.

Policy (D7): the server owns the protos; clients receive updates as a push. No drift check
exists or is planned — refreshing is a manual step, and `Connectivity` will arrive the same way.

## Part C — conformance test (blocked until server PR #595 merges)

Same as the 3D client: compare against `sample_design` goldens only; respect `"fidelity"`.

## Do not
Same three prohibitions as the 3D client: no cross-collection reference resolution beyond
the existing `ResolveComponent`, no `attributeLookupTable["ID"]`, no keying on `ComponentRecord.Id`.
