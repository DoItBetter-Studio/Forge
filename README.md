# Glyphborn.Echo

> Proprietary Software — DoItBetter Studio

**Glyphborn.Echo** is the audio engine component of the Glyphborn ecosystem.

Development began in August 2025 as part of DoItBetter Studio’s long-term effort to build a modular, deterministic game engine and editor suite.

---

## Overview

Glyphborn.Echo is responsible for:

- Audio playback and mixing
- Sound effect management
- Music streaming and looping
- Audio channel control
- Volume routing and balancing
- Runtime audio state handling

Glyphborn.Echo defines how sound behaves within the engine while remaining independent of world structure, rendering, and gameplay logic.

---

## Architectural Role

The Glyphborn ecosystem is intentionally modular.

Glyphborn.Echo handles:

✔ Audio playback  
✔ Music and loop control  
✔ Channel management  
✔ Runtime audio coordination  

Glyphborn.Echo does **not** handle:

✖ Rendering  
✖ World or spatial data  
✖ Game rules or combat systems  
✖ Networking  
✖ UI  

Audio logic is isolated to ensure clarity, testability, and clean dependency boundaries.

Glyphborn.Echo integrates with other systems but does not depend on them for core functionality.

---

## Design Principles

Glyphborn.Echo follows the engineering principles established by DoItBetter Studio:

- **Deterministic Audio State** — Predictable behavior across runtime sessions  
- **Modular Architecture** — Independent repository and versioning  
- **Separation of Concerns** — No world or rendering coupling  
- **Extensibility** — Designed for future audio system expansion  
- **Testable Core Logic** — Clear abstraction around playback providers  

---

## Ecosystem Integration

Glyphborn.Echo integrates with:

- Atlas — World and spatial systems  
- Mapper — Tooling and editor workflows  
- Glyphborn — Core runtime and gameplay systems  

Each component is developed independently to allow controlled iteration and long-term scalability.

---

## Project Status

Glyphborn.Echo is currently in active development.

The broader Glyphborn engine will eventually be rebranded and released as:

**Damascus — The Steel Editor Suite**

Until official release, this repository is publicly visible for transparency and portfolio purposes but is not open source.

---

## Ownership & License

Copyright © 2025–2026 DoItBetter Studio

All rights reserved.

This software and associated documentation are proprietary intellectual property of DoItBetter Studio.

No license is granted to use, copy, modify, distribute, sublicense, reverse engineer, or create derivative works without prior written permission.

DoItBetter Studio reserves the right to relicense this software under an open-source license upon official release.
