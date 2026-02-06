# Product Context

## Purpose
The OdbDesignInfo Client exists to provide CAM engineers, PCB designers, and manufacturing specialists with a powerful, desktop-based analytics dashboard for ODB++ design data. It bridges the gap between raw, complex ODB++ files and actionable engineering intelligence.

## Problem Solved
Traditional ODB++ viewers are often:
- **Static**: Just rendering images without data interconnectivity.
- **Slow**: Struggling with large datasets (thousands of components/nets).
- **Disconnected**: Lacking integration with other tools like 3D viewers.
- **Platform Dependent**: Often Windows-only and using dated UI frameworks.

This client solves these issues by acting as a "connected" workstation that leverages a dedicated backend (OdbDesignServer) for parsing, while providing a modern, responsive, cross-platform UI for interrogation.

## Core Functionality
- **Hierarchical Interrogation**: Users can drill down from Components -> Nets -> Pins -> Vias.
- **Hybrid Connectivity**: Uses REST for metadata and gRPC for high-performance bulk data streaming.
- **Cross-Probing**: Seamlessly syncs selection state with a companion 3D Viewer via local IPC (Named Pipes).
- **Advanced Visualization**: Uses virtualized TreeDataGrids to render massive datasets (10k+ rows) at 60fps.

## User Experience Goals
- **Responsiveness**: Operations should feel instant. Large lists must scroll smoothly.
- **Clarity**: Data should be presented in clear, sortable, and filterable grids.
- **Continuity**: The "Connected" state should be resilient, handling server restarts or network blips gracefully.
- **Modernity**: A consistent, beautiful Fluent UI across Windows, macOS, and Linux.
