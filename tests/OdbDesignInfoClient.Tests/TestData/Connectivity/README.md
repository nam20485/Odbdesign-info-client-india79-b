# Connectivity conformance test data

Inputs and ground truth for `ProductModel/ProductModelConformanceTests.cs`
(OdbDesign plan milestone **M3.3** — client conformance harness, followup F2).

## Layout

```
sample_design/
  sample_design.golden.json        ground truth, byte-identical copy of OdbDesign's
                                   OdbDesignTests/Fixtures/Connectivity/sample_design.golden.json
  top-components.binpb             captured server ComponentsFile  (comp_+_top)
  bottom-components.binpb          captured server ComponentsFile  (comp_+_bot)
  eda-data.binpb                   captured server EdaDataFile      (step eda/data)
  manifest.json                    provenance: server design name, step, endpoint,
                                   capture date, sha256 of each .binpb
```

## Provenance

- **Goldens** are generated in the OdbDesign repo by
  `scripts/gen-connectivity-golden.py` straight from the raw ODB++ sources of the
  fixture designs (merged via OdbDesign PR #595). A golden's identity for a pin is
  the *component-file* TOP record: `index` is the 0-based CMP ordinal within its
  own side's file, `pins[].ordinal` the TOP ordinal, `pinNumber` the TOP
  `pin_index`, and `netOrdinal` the TOP `net_num` — i.e. the eda/data NET-record
  ordinal (`null` = unconnected). Regeneration and drift propagation are
  server-side concerns (decision D7); never edit these files locally.
- **Captures** were taken 2026-10-15 by the 3D client's
  `tools/OdbDesign3DClient.FixtureCapture --mode connectivity`
  (odbdesign-3d-client-prototype) against the live server: one cached `GetDesign`
  per design, then the step's `comp_+_top` / `comp_+_bot` ComponentsFile and
  eda/data EdaDataFile serialized to bytes. The tests reassemble those into the
  `Design.FileModel` shape and read them through the production
  `ProductModelReader`, so the harness runs over real server data, not
  hand-built fixtures. To recapture from this repo, fetch `GetDesign` over gRPC
  (e.g. via the integration-test harness or the 3D client's tool) and re-serialize
  the three messages; update `manifest.json` sha256 fields to match.

## Why only sample_design

The server repo carries four fixture goldens; this client checks in **only the
public-domain design** (`sample_design`). The zero-connectivity controls of
handoff §8b (Panel-g7162, 200-40628: legitimately all-unconnected boards) and the
sampled-fidelity branch run in the 3D client's harness, which shares this
component builder through `src/OdbDesign.ProductModel` — the join under test is
the same code.
