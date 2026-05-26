# Visio-Ready Diagram Bundle

This folder contains one Mermaid source file per architecture view so each diagram can be imported into Visio as its own page.

## Files

- 01-system-context.mmd
- 02-runtime-container-view.mmd
- 03-ui-composition-model.mmd
- 04-domain-communication-model.mmd
- 05-atp-capability-derivation-pipeline.mmd
- 06-compliance-telemetry-control-loop.mmd
- 07-end-to-end-user-journey-sequence.mmd
- render-mermaid-to-svg.ps1

## Fast Path: Convert to SVG then Open in Visio

1. Install Mermaid CLI if needed:

```powershell
npm install -g @mermaid-js/mermaid-cli
```

2. From this folder, render all diagrams:

```powershell
powershell -ExecutionPolicy Bypass -File .\render-mermaid-to-svg.ps1 -InputDir . -OutputDir .\svg
```

3. In Visio:
- File -> Open -> pick each .svg file in the svg folder.
- Optional edit mode: right-click imported graphic -> Group -> Ungroup. Repeat once to convert into editable Visio primitives.

## No CLI Available? Use draw.io

1. Open https://app.diagrams.net/
2. Arrange -> Insert -> Advanced -> Mermaid
3. Paste the content from a .mmd file.
4. Export as SVG.
5. Open SVG in Visio.

## Suggested Visio Page Order

1. System Context
2. Runtime Container View
3. UI Composition Model
4. Domain Communication Model
5. ATP Capability Derivation Pipeline
6. Compliance Telemetry Control Loop
7. End-to-End User Journey Sequence
