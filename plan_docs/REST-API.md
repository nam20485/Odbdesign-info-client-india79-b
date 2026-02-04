\# OdbDesignServer REST API Documentation



\## Table of Contents



1\. \[Overview](#overview)

2\. \[Architecture](#architecture)

3\. \[Authentication](#authentication)

4\. \[Base Configuration](#base-configuration)

5\. \[Core Endpoints](#core-endpoints)

6\. \[Data Models](#data-models)

7\. \[Feature Types Reference](#feature-types-reference)

8\. \[Error Handling](#error-handling)

9\. \[TypeScript Integration Guide](#typescript-integration-guide)

10\. \[Performance Considerations](#performance-considerations)



---



\## Overview



The OdbDesignServer provides a RESTful API for accessing ODB++ (Open Database) PCB design data. The server exposes board designs, steps, layers, symbols, and geometric features through JSON endpoints backed by Protocol Buffer serialization.



\*\*Key Features:\*\*



\- Full ODB++ design hierarchy navigation

\- Layer-by-layer feature extraction

\- Symbol library access

\- Component and netlist queries

\- Attribute metadata retrieval

\- Health monitoring endpoints



\*\*Technology Stack:\*\*



\- \*\*Framework\*\*: Crow (C++ HTTP framework)

\- \*\*Serialization\*\*: Protocol Buffers → JSON via `google::protobuf::util::MessageToJsonString()`

\- \*\*Authentication\*\*: HTTP Basic Auth

\- \*\*Data Format\*\*: JSON (derived from protobuf schemas)



---



\## Architecture



\### Request Flow



```

Client Request

&nbsp;   ↓

Crow HTTP Server (Port 8888)

&nbsp;   ↓

FileModelController::steps\_layers\_features\_route\_handler()

&nbsp;   ↓

FeaturesFile (C++ Model)

&nbsp;   ↓

IProtoBuffable::to\_json()

&nbsp;   ↓

to\_protobuf() → Protocol Buffer Message

&nbsp;   ↓

google::protobuf::util::MessageToJsonString()

&nbsp;   ↓

JsonCrowReturnable<T> (Response Wrapper)

&nbsp;   ↓

JSON Response to Client

```



\### Key Components



1\. \*\*Controllers\*\* (`FileModelController.h/cpp`): HTTP endpoint handlers

2\. \*\*Data Models\*\* (`FeaturesFile.h`): C++ domain objects

3\. \*\*Protocol Buffers\*\* (`featuresfile.proto`, `common.proto`, `enums.proto`): Schema definitions

4\. \*\*Serialization\*\* (`IProtoBuffable.h`): JSON conversion interface



---



\## Authentication



All endpoints (except health checks) require HTTP Basic Authentication.



\*\*Headers:\*\*



```http

Authorization: Basic <base64(username:password)>

```



\*\*Example (curl):\*\*



```bash

curl -u username:password http://localhost:8888/filemodels/design1

```



\*\*TypeScript (fetch):\*\*



```typescript

const headers = new Headers({

&nbsp; Authorization: 'Basic ' + btoa('username:password'),

&nbsp; 'Content-Type': 'application/json',

});



fetch('http://localhost:8888/filemodels/design1', { headers });

```



---



\## Base Configuration



\### Environment Variables



```bash

\# .env or .env.local

VITE\_API\_BASE\_URL=http://localhost:8888

VITE\_API\_USERNAME=your\_username

VITE\_API\_PASSWORD=your\_password

```



\### Server URLs



\- \*\*Development\*\*: `http://localhost:8888`

\- \*\*Production AWS\*\*: `http://default-ingress-1165108808.us-west-2.elb.amazonaws.com`

\- \*\*Local Network\*\*: `http://precision5820:8081`



---



\## Core Endpoints



\### File Models



\#### List All Designs



```

GET /filemodels

```



\*\*Response:\*\*



```json

\["design1", "design2", "design3"]

```



\*\*Description:\*\* Returns array of available design names.



---



\#### Get Design Details



```

GET /filemodels/{name}

```



\*\*Parameters:\*\*



\- `name` (path): Design identifier



\*\*Response:\*\* Complete design metadata including steps, layers, symbols, and attributes.



---



\### Steps \& Layers



\#### List Steps



```

GET /filemodels/{name}/steps

```



\*\*Response:\*\* Array of step names (e.g., `\["pcb", "panel"]`)



---



\#### List Layers in Step



```

GET /filemodels/{name}/steps/{step}/layers

```



\*\*Parameters:\*\*



\- `name` (path): Design name

\- `step` (path): Step name



\*\*Response:\*\* Array of layer metadata objects



---



\#### \*\*Get Layer Features\*\* ⭐ \*\*PRIMARY ENDPOINT\*\*



```

GET /filemodels/{name}/steps/{step}/layers/{layer}/features

```



\*\*Parameters:\*\*



\- `name` (path): Design name (e.g., `"design1"`)

\- `step` (path): Step name (e.g., `"pcb"`)

\- `layer` (path): Layer name (e.g., `"top\_copper"`)



\*\*Response:\*\* `FeaturesFile` object (see \[Data Models](#data-models))



\*\*Example Request:\*\*



```bash

curl -u user:pass http://localhost:8888/filemodels/myboard/steps/pcb/layers/top\_copper/features

```



---



\### Symbols



\#### List Symbols



```

GET /filemodels/{name}/symbols

```



\*\*Response:\*\* Array of symbol directory names



---



\#### Get Symbol Features



```

GET /filemodels/{name}/symbols/{symbol}/features

```



\*\*Parameters:\*\*



\- `name` (path): Design name

\- `symbol` (path): Symbol name (e.g., `"pad\_round\_d60"`)



\*\*Response:\*\* `FeaturesFile` object (same structure as layer features)



---



\### Design Queries



\#### Get Design Components



```

GET /designs/{name}/components

```



\#### Get Design Nets



```

GET /designs/{name}/nets

```



\#### Get Design Parts



```

GET /designs/{name}/parts

```



\#### Get Design Packages



```

GET /designs/{name}/packages

```



---



\### Health Checks



\#### Liveness Probe



```

GET /healthz/live

```



\#### Readiness Probe



```

GET /healthz/ready

```



\#### Startup Probe



```

GET /healthz/started

```



\*\*Response (all):\*\* `200 OK`



---



\## Data Models



\### FeaturesFile (Top-Level Response)



\*\*Description:\*\* Container for all geometric features on a layer or within a symbol.



\*\*TypeScript Interface:\*\*



```typescript

interface FeaturesFile {

&nbsp; units: string; // "mm" or "inch"

&nbsp; id: number; // Unique file ID

&nbsp; path: string; // Original file path

&nbsp; directory: string; // Parent directory

&nbsp; numFeatures: number; // Total feature count

&nbsp; featureRecords: FeatureRecord\[]; // Array of geometric shapes

&nbsp; symbolNamesByName: { \[name: string]: SymbolName }; // Symbol lookup

&nbsp; symbolNames: SymbolName\[]; // Symbol definitions

}

```



\*\*JSON Example:\*\*



```json

{

&nbsp; "units": "mm",

&nbsp; "id": 1,

&nbsp; "path": "/steps/pcb/layers/top\_copper/features",

&nbsp; "directory": "layers/top\_copper",

&nbsp; "numFeatures": 1523,

&nbsp; "featureRecords": \[

&nbsp;   /\* ... \*/

&nbsp; ],

&nbsp; "symbolNamesByName": {

&nbsp;   "r100": {

&nbsp;     /\* SymbolName object \*/

&nbsp;   }

&nbsp; },

&nbsp; "symbolNames": \[

&nbsp;   /\* ... \*/

&nbsp; ]

}

```



---



\### FeatureRecord (Geometric Shape)



\*\*Description:\*\* Individual PCB feature (pad, line, arc, surface, text).



\*\*Type Enum:\*\*



\- `0` = Arc

\- `1` = Pad

\- `2` = Surface

\- `4` = Text

\- `5` = Line



\*\*Common Fields:\*\*



```typescript

interface FeatureRecordBase {

&nbsp; type: number; // 0-5 (see enum above)

&nbsp; sym\_num?: number; // Symbol index (if applicable)

&nbsp; polarity?: Polarity; // "POSITIVE" | "NEGATIVE"

&nbsp; dcode?: number; // D-code reference

&nbsp; id?: number; // Feature unique ID

&nbsp; orient\_def?: OrientDef; // Orientation metadata

&nbsp; orient\_def\_rotation?: number; // Rotation in degrees

&nbsp; attributeLookupTable?: { \[key: string]: string }; // Custom attributes

}

```



\*\*Polarity Enum:\*\*



```typescript

enum Polarity {

&nbsp; POSITIVE = 'POSITIVE', // Additive material

&nbsp; NEGATIVE = 'NEGATIVE', // Subtractive (cutout)

}

```



---



\### SymbolName



\*\*Description:\*\* Symbol definition with name and bounding box.



\*\*TypeScript Interface:\*\*



```typescript

interface SymbolName {

&nbsp; name: string; // Symbol identifier (e.g., "r100", "pad\_round\_d60")

&nbsp; index: number; // Symbol index for reference

&nbsp; xSize?: number; // Bounding box width

&nbsp; ySize?: number; // Bounding box height

&nbsp; xMin?: number; // Bounding box min X

&nbsp; yMin?: number; // Bounding box min Y

&nbsp; xMax?: number; // Bounding box max X

&nbsp; yMax?: number; // Bounding box max Y

}

```



---



\## Feature Types Reference



\### 1. Line (type = 5)



\*\*Description:\*\* Straight line segment with width.



\*\*Fields:\*\*



```typescript

interface LineFeature extends FeatureRecordBase {

&nbsp; type: 5;

&nbsp; xs: number; // Start X coordinate

&nbsp; ys: number; // Start Y coordinate

&nbsp; xe: number; // End X coordinate

&nbsp; ye: number; // End Y coordinate

&nbsp; sym\_num?: number; // Line width symbol reference

}

```



\*\*JSON Example:\*\*



```json

{

&nbsp; "type": 5,

&nbsp; "xs": 10.5,

&nbsp; "ys": 20.3,

&nbsp; "xe": 45.8,

&nbsp; "ye": 20.3,

&nbsp; "sym\_num": 12,

&nbsp; "polarity": "POSITIVE"

}

```



\*\*Rendering:\*\* Draw line from (xs, ys) to (xe, ye) with width from symbol definition.



---



\### 2. Pad (type = 1)



\*\*Description:\*\* Footprint pad or via with symbol shape.



\*\*Fields:\*\*



```typescript

interface PadFeature extends FeatureRecordBase {

&nbsp; type: 1;

&nbsp; x: number; // Center X coordinate

&nbsp; y: number; // Center Y coordinate

&nbsp; apt\_def\_symbol\_num?: number; // Symbol index for pad shape

&nbsp; apt\_def\_resize\_factor?: number; // Scale multiplier (default: 1.0)

}

```



\*\*JSON Example:\*\*



```json

{

&nbsp; "type": 1,

&nbsp; "x": 15.24,

&nbsp; "y": 10.16,

&nbsp; "apt\_def\_symbol\_num": 3,

&nbsp; "apt\_def\_resize\_factor": 1.0,

&nbsp; "polarity": "POSITIVE",

&nbsp; "dcode": 22

}

```



\*\*Rendering:\*\*



1\. Lookup symbol from `apt\_def\_symbol\_num` in `symbolNames` array

2\. Fetch symbol features via `/symbols/{symbol\_name}/features`

3\. Scale by `apt\_def\_resize\_factor`

4\. Translate to (x, y) position



---



\### 3. Arc (type = 0)



\*\*Description:\*\* Circular arc segment.



\*\*Fields:\*\*



```typescript

interface ArcFeature extends FeatureRecordBase {

&nbsp; type: 0;

&nbsp; x: number; // Start X coordinate

&nbsp; y: number; // Start Y coordinate

&nbsp; xc: number; // Center X coordinate (relative or absolute)

&nbsp; yc: number; // Center Y coordinate (relative or absolute)

&nbsp; cw?: boolean; // Clockwise direction (default: false)

&nbsp; sym\_num?: number; // Arc width symbol reference

}

```



\*\*JSON Example:\*\*



```json

{

&nbsp; "type": 0,

&nbsp; "x": 10.0,

&nbsp; "y": 15.0,

&nbsp; "xc": 12.0,

&nbsp; "yc": 15.0,

&nbsp; "cw": true,

&nbsp; "sym\_num": 8,

&nbsp; "polarity": "POSITIVE"

}

```



\*\*Rendering:\*\* Arc from (x, y) around center (xc, yc) with direction `cw`.



---



\### 4. Text (type = 4)



\*\*Description:\*\* Text annotation.



\*\*Fields:\*\*



```typescript

interface TextFeature extends FeatureRecordBase {

&nbsp; type: 4;

&nbsp; x: number; // Insertion X coordinate

&nbsp; y: number; // Insertion Y coordinate

&nbsp; font?: string; // Font name

&nbsp; xsize?: number; // Character width

&nbsp; ysize?: number; // Character height

&nbsp; width\_factor?: number; // Width scaling

&nbsp; text?: string; // Text content

}

```



\*\*JSON Example:\*\*



```json

{

&nbsp; "type": 4,

&nbsp; "x": 25.4,

&nbsp; "y": 30.0,

&nbsp; "font": "standard",

&nbsp; "xsize": 1.5,

&nbsp; "ysize": 2.0,

&nbsp; "width\_factor": 1.0,

&nbsp; "text": "U1",

&nbsp; "polarity": "POSITIVE"

}

```



---



\### 5. Surface (type = 2)



\*\*Description:\*\* Filled polygon region (copper pour, pad shape, etc.).



\*\*Fields:\*\*



```typescript

interface SurfaceFeature extends FeatureRecordBase {

&nbsp; type: 2;

&nbsp; contourPolygons: ContourPolygon\[]; // Array of polygons (islands \& holes)

}

```



\*\*ContourPolygon Structure:\*\*



```typescript

interface ContourPolygon {

&nbsp; type: ContourPolygonType; // "ISLAND" | "HOLE"

&nbsp; xStart: number; // Starting X coordinate

&nbsp; yStart: number; // Starting Y coordinate

&nbsp; polygonParts: PolygonPart\[]; // Segments making up the polygon

}



enum ContourPolygonType {

&nbsp; ISLAND = 'ISLAND', // Solid filled region

&nbsp; HOLE = 'HOLE', // Cutout within island

}



interface PolygonPart {

&nbsp; segments?: Segment\[]; // Line segments

&nbsp; arcs?: ArcPart\[]; // Arc segments

}



interface Segment {

&nbsp; xe: number; // End X coordinate

&nbsp; ye: number; // End Y coordinate

}



interface ArcPart {

&nbsp; xe: number; // End X coordinate

&nbsp; ye: number; // End Y coordinate

&nbsp; xc: number; // Center X (relative to start)

&nbsp; yc: number; // Center Y (relative to start)

&nbsp; cw: boolean; // Clockwise direction

}

```



\*\*JSON Example:\*\*



```json

{

&nbsp; "type": 2,

&nbsp; "polarity": "POSITIVE",

&nbsp; "contourPolygons": \[

&nbsp;   {

&nbsp;     "type": "ISLAND",

&nbsp;     "xStart": 10.0,

&nbsp;     "yStart": 10.0,

&nbsp;     "polygonParts": \[

&nbsp;       {

&nbsp;         "segments": \[

&nbsp;           { "xe": 20.0, "ye": 10.0 },

&nbsp;           { "xe": 20.0, "ye": 20.0 },

&nbsp;           { "xe": 10.0, "ye": 20.0 }

&nbsp;         ]

&nbsp;       }

&nbsp;     ]

&nbsp;   },

&nbsp;   {

&nbsp;     "type": "HOLE",

&nbsp;     "xStart": 12.0,

&nbsp;     "yStart": 12.0,

&nbsp;     "polygonParts": \[

&nbsp;       {

&nbsp;         "segments": \[

&nbsp;           { "xe": 18.0, "ye": 12.0 },

&nbsp;           { "xe": 18.0, "ye": 18.0 },

&nbsp;           { "xe": 12.0, "ye": 18.0 }

&nbsp;         ]

&nbsp;       }

&nbsp;     ]

&nbsp;   }

&nbsp; ]

}

```



\*\*Rendering Algorithm:\*\*



1\. Start at `(xStart, yStart)`

2\. Iterate through `polygonParts`:

&nbsp;  - For each `segment`: draw line to `(xe, ye)`, update current position

&nbsp;  - For each `arc`: draw arc from current position to `(xe, ye)` around `(xc, yc)`

3\. Close polygon automatically (last point connects to start)

4\. Apply winding rules:

&nbsp;  - `ISLAND`: Fill region

&nbsp;  - `HOLE`: Subtract from island



---



\## Error Handling



\### HTTP Status Codes



| Code  | Description           | Common Causes                                 |

| ----- | --------------------- | --------------------------------------------- |

| `200` | Success               | Request completed successfully                |

| `401` | Unauthorized          | Missing or invalid Basic Auth credentials     |

| `404` | Not Found             | Design, step, layer, or symbol does not exist |

| `500` | Internal Server Error | Server-side processing failure                |



\### Error Response Format



```json

{

&nbsp; "error": "Design 'invalid\_name' not found",

&nbsp; "status": 404

}

```



\### TypeScript Error Handling



```typescript

async function fetchLayerFeatures(

&nbsp; design: string,

&nbsp; step: string,

&nbsp; layer: string

): Promise<FeaturesFile> {

&nbsp; const url = `${API\_BASE\_URL}/filemodels/${design}/steps/${step}/layers/${layer}/features`;



&nbsp; const response = await fetch(url, {

&nbsp;   headers: {

&nbsp;     Authorization: 'Basic ' + btoa(`${username}:${password}`),

&nbsp;   },

&nbsp; });



&nbsp; if (!response.ok) {

&nbsp;   if (response.status === 401) {

&nbsp;     throw new Error('Authentication failed. Check credentials.');

&nbsp;   }

&nbsp;   if (response.status === 404) {

&nbsp;     throw new Error(`Layer '${layer}' not found in step '${step}'`);

&nbsp;   }

&nbsp;   throw new Error(`HTTP ${response.status}: ${response.statusText}`);

&nbsp; }



&nbsp; return response.json();

}

```



---



\## TypeScript Integration Guide



\### 1. Install Dependencies



```bash

npm install axios  # or use fetch API

```



\### 2. Create API Client (`src/api/odbDesignClient.ts`)



```typescript

import { FeaturesFile } from '../models/odb';



const API\_BASE\_URL = import.meta.env.VITE\_API\_BASE\_URL || 'http://localhost:8888';

const AUTH =

&nbsp; 'Basic ' + btoa(`${import.meta.env.VITE\_API\_USERNAME}:${import.meta.env.VITE\_API\_PASSWORD}`);



export async function getLayerFeatures(

&nbsp; design: string,

&nbsp; step: string,

&nbsp; layer: string

): Promise<FeaturesFile> {

&nbsp; const response = await fetch(

&nbsp;   `${API\_BASE\_URL}/filemodels/${design}/steps/${step}/layers/${layer}/features`,

&nbsp;   { headers: { Authorization: AUTH } }

&nbsp; );



&nbsp; if (!response.ok) throw new Error(`HTTP ${response.status}`);

&nbsp; return response.json();

}



export async function getSymbolFeatures(design: string, symbol: string): Promise<FeaturesFile> {

&nbsp; const response = await fetch(`${API\_BASE\_URL}/filemodels/${design}/symbols/${symbol}/features`, {

&nbsp;   headers: { Authorization: AUTH },

&nbsp; });



&nbsp; if (!response.ok) throw new Error(`HTTP ${response.status}`);

&nbsp; return response.json();

}



export async function listDesigns(): Promise<string\[]> {

&nbsp; const response = await fetch(`${API\_BASE\_URL}/filemodels`, { headers: { Authorization: AUTH } });



&nbsp; if (!response.ok) throw new Error(`HTTP ${response.status}`);

&nbsp; return response.json();

}

```



\### 3. Use in React Components



```typescript

import { useEffect, useState } from 'react';

import { getLayerFeatures } from '../api/odbDesignClient';

import { FeaturesFile } from '../models/odb';



export function BoardViewer() {

&nbsp; const \[features, setFeatures] = useState<FeaturesFile | null>(null);

&nbsp; const \[error, setError] = useState<string | null>(null);



&nbsp; useEffect(() => {

&nbsp;   getLayerFeatures('myboard', 'pcb', 'top\_copper')

&nbsp;     .then(setFeatures)

&nbsp;     .catch(err => setError(err.message));

&nbsp; }, \[]);



&nbsp; if (error) return <div>Error: {error}</div>;

&nbsp; if (!features) return <div>Loading...</div>;



&nbsp; return (

&nbsp;   <div>

&nbsp;     <h2>Layer Features ({features.numFeatures} total)</h2>

&nbsp;     {features.featureRecords.map((feature, idx) => (

&nbsp;       <div key={idx}>Feature type: {feature.type}</div>

&nbsp;     ))}

&nbsp;   </div>

&nbsp; );

}

```



\### 4. Rendering Features with Three.js



```typescript

import \* as THREE from 'three';

import { FeatureRecord, LineFeature } from '../models/odb';



function renderLine(feature: LineFeature, scene: THREE.Scene) {

&nbsp; const geometry = new THREE.BufferGeometry().setFromPoints(\[

&nbsp;   new THREE.Vector3(feature.xs, feature.ys, 0),

&nbsp;   new THREE.Vector3(feature.xe, feature.ye, 0),

&nbsp; ]);



&nbsp; const material = new THREE.LineBasicMaterial({ color: 0xff0000 });

&nbsp; const line = new THREE.Line(geometry, material);

&nbsp; scene.add(line);

}

```



---



\## Performance Considerations



\### Large Feature Files



\- Layer files can contain \*\*10,000+ features\*\*

\- Surface polygons may have \*\*1,000+ segments\*\*

\- JSON responses can exceed \*\*10 MB\*\*



\### Optimization Strategies



1\. \*\*Lazy Loading\*\*: Fetch layers on-demand (user selects visible layers)

2\. \*\*Pagination\*\*: Request feature subsets (if server supports `?offset=\&limit=`)

3\. \*\*Web Workers\*\*: Parse JSON in background thread

4\. \*\*Geometry Caching\*\*: Store parsed Three.js geometries in IndexedDB

5\. \*\*Level of Detail (LOD)\*\*: Simplify polygons at low zoom levels

6\. \*\*Viewport Culling\*\*: Only render features within camera frustum



\### Example: Chunked Processing



```typescript

async function loadFeaturesInChunks(

&nbsp; design: string,

&nbsp; step: string,

&nbsp; layer: string,

&nbsp; onChunk: (features: FeatureRecord\[]) => void

) {

&nbsp; const data = await getLayerFeatures(design, step, layer);

&nbsp; const CHUNK\_SIZE = 1000;



&nbsp; for (let i = 0; i < data.featureRecords.length; i += CHUNK\_SIZE) {

&nbsp;   const chunk = data.featureRecords.slice(i, i + CHUNK\_SIZE);

&nbsp;   onChunk(chunk);

&nbsp;   await new Promise((resolve) => setTimeout(resolve, 0)); // Yield to UI

&nbsp; }

}

```



---



\## Appendix: Protocol Buffer Schema References



\### Source Files (OdbDesign Repository)



\- \*\*`featuresfile.proto`\*\*: FeaturesFile, FeatureRecord definitions

\- \*\*`common.proto`\*\*: ContourPolygon, PolygonPart structures

\- \*\*`enums.proto`\*\*: Polarity, UnitType, ContourPolygonType enums

\- \*\*`symbolname.proto`\*\*: SymbolName structure



\### Serialization Path



```

C++ Model (FeaturesFile)

&nbsp; → to\_protobuf() \[IProtoBuffable]

&nbsp; → Protocol Buffer Message

&nbsp; → google::protobuf::util::MessageToJsonString()

&nbsp; → JSON String

&nbsp; → JsonCrowReturnable<T>

&nbsp; → HTTP Response

```



---



\## Support \& Contributing



\- \*\*OdbDesign Repository\*\*: https://github.com/nam20485/OdbDesign

\- \*\*Server Issues\*\*: https://github.com/nam20485/OdbDesign/issues

\- \*\*Client Issues\*\*: https://github.com/nam20485/board-shape-view-client/issues



---



\*\*Last Updated:\*\* January 2025  

\*\*API Version:\*\* 0.9  

\*\*Protocol:\*\* ODB++ 8.1+



