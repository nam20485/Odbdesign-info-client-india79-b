# Task: move design reads to gRPC, refresh vendored protos, then conform

Source of truth: `docs/component-connectivity-client-handoff.md` (§5b, §7).

Your `ComponentDetailBuilder.cs` keying is already correct — `ResolveComponent` takes
`uint Index` with no `byId` path (`:429-435`), and `byName` uses `StringComparer.Ordinal`
(`:158`). Nothing to fix there. Two items remain.

## Part A — REST → gRPC for design reads (largest item, own PR)

The server's derived connectivity contract ships **gRPC only**; REST is frozen to security
fixes. Design reads currently go through `src/OdbDesignInfoClient.Services/Api/IOdbDesignRestApi.cs`
+ `Api/Dtos/`, while gRPC codegen is already wired in
`src/OdbDesign.ProductModel/OdbDesign.ProductModel.csproj` (`Protobuf` items from `:32`).
So the transport exists; the read path just doesn't use it.

Sequence: switch the read path → delete the `Api/Dtos/` shapes it makes dead → do not grow
them. Verify with `dotnet build` and the existing test suite.

## Part B — refresh vendored protos

`protoc/grpc/service.proto` is missing four shipped features: `GetStandardFonts`,
`RequestLoadDesign`, `LoadStatus`, `include_normalized_lists`. Replace `protoc/` from the
server repo's `OdbDesignLib/protoc/` + `OdbDesignServer/protoc/grpc/`.

Policy (D7): the server owns the protos; clients receive updates as a push. No drift check
exists or is planned — refreshing is a manual step, and `Connectivity` will arrive the same way.

## Part C — conformance test (blocked until server PR #595 merges)

Same as the 3D client: compare against `sample_design` goldens only; respect `"fidelity"`.

## Do not
Same three prohibitions as the 3D client: no cross-collection reference resolution beyond
the existing `ResolveComponent`, no `attributeLookupTable["ID"]`, no keying on `ComponentRecord.Id`.
