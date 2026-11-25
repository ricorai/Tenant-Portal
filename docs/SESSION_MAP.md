# SESSION MAP: Tenant-Portal (#27)
**Session Date:** Sunday, November 22, 2026  
**Status:** Live on GitHub (`main` @ `3f16d46`) · Mockup Suite Ready on Desktop  
**Nodes Active:** Main PC (Studio / Lead) & ThinkPad L520 (`192.168.254.142`, Server Node)

---

## 1. Executive Summary & Topology
The mission of this session was establishing a self-contained, offline-first rental Wi-Fi captive gateway and 30-day subscription system for the 4 rental units upstairs at Tres Rentals (Tagum).

* **Router Node:** Tenda F3 (WAN connected to Globe modem at `192.168.254.120`, LAN subnet `192.168.0.0/24`, Gateway `192.168.0.1`, SSID: `Tres_Rentals_Tagum`). Preferred DHCP DNS set to `192.168.254.142`.
* **Server Node:** ThinkPad L520 (`192.168.254.142`, Windows 11, User `Bea`).
  * Running `TenantGateway.exe` (`31,232 B`, .NET 4.8 `csc.exe`) listening on TCP `80`, TCP `3000`, and native UDP `53` (`*portal` wildcard DNS).
  * Detached runner: `run_gateway_hidden.vbs` via Run key `TresRentals_Gateway`.
* **Sacred Co-Located Workloads Preserved:**
  * `Comrade_Panel` (:8085, Pages `comrade-panel.pages.dev`).
  * `Comrade Zomboid B42.21` (RCON :27015, Playit UDP :16909).
  * `AdGuard Home` disabled (`AdGuardHome_DISABLED`) to leave UDP 53 and TCP 80 unblocked.

---

## 2. Key Milestones & Progression

### Milestone 1: Local Native Shift
* Discarded complex cloud dependencies (Cloudflare Workers / KV); shifted 100% to a local native C# daemon (`TenantGateway.cs`, ~30 KB) backed by atomic `leases.json` and SQLite `schema.sql`.
* Zero npm/node runtimes, zero external CDNs, instantaneous local render.

### Milestone 2: Router Configuration & Port 80 / 53 DNS Binding
* Maintained remote configuration of Tenda F3 via `windows-mcp`:
  * Configured SSID to `Tres_Rentals_Tagum`.
  * Set DHCP Preferred DNS to `192.168.254.142`.
* Implemented built-in UDP DNS responder on Port 53 directly inside `TenantGateway.cs`:
  * Handles `tres-rentals-wifi.portal` and all `*.portal` suffix queries &rarr; answers `192.168.254.142`.
  * Passes all external internet queries (Google, Facebook) directly upstream to `8.8.8.8`.
  * Fixed Windows UDP crash bug with `SIO_UDP_CONNRESET` (`-1744830452`).

### Milestone 3: The "Generative Image First" Workflow
* Established new design methodology:
  1. Capture raw full-height HTML wireframe (`tenant_portal_raw_wireframe.png`).
  2. Use Imagen AI subagent to re-skin into luxury minimalist concept images.
  3. Operator reviews image on Desktop (`C:\Users\ricry\Desktop\`).
  4. Once approved, adapt HTML/CSS/JS/C# 1:1 to match the visual concept.
* Visual Evolution:
  * v1: Dark Bento (`#1C1C1C` / `#252526` / sage green / amber).
  * v2: Scandinavian Editorial Warm Cream (`#FAF8F5` paper canvas, white cards).
  * v3 (Final): Sleek Black & White / Grayscale Palette (`#0E0E0E`, `#626262`, `#B4B4B4`, `#C0C0C0`, `#EDEDED`, `#F5F5F7`).

### Milestone 4: Feature Polish (CreamStore UX Inspiration)
* Inspected `https://creamstore.ph/games/mobile-legends-games` payment layout.
* Updated pricing to **₱200 / Month** per device (`₱200.00 Exact` badge).
* Updated Emergency Pass to **10 minutes** with **1 pass max daily per device** (with UTC+8 calendar rollover).
* 3 Payment Actions:
  * `[ 📥 Save QR ]` &rarr; downloads vector QR to photos for GCash Gallery upload.
  * `[ 📋 Copy Number ]` &rarr; 1-tap clipboard copy of `0917-XXX-XXXX` with toast.
  * `[ 🔗 Open GCash ]` &rarr; mobile deep link (`gcash://`).
* Status pill renamed from *Internet Locked* &rarr; **`● No Internet Access`**.
* 13-digit GCash reference field marked as **`(Optional)`**.

### Milestone 5: Standalone Desktop Mockup Suite
Created an isolated, zero-server test suite in `C:\Users\ricry\Desktop\Tenant_Portal_Mockups\`:
* `1_login_pin.html`: Default splash with room selector and tactile 4-digit PIN login.
* `2_not_connected.html`: Red Wi-Fi icon, optional reference, interactive 10-min timer, and fake reference submit animation (turns to green &rarr; checkmark pop &rarr; blur &rarr; redirects).
* `3_connected.html`: Pulsating green Wi-Fi animation, live 30-day countdown, recovery PIN `4821`, SQM stats.
* `images/`: All generated concept images and raw wireframes.

### Milestone 6: Mathematical Co-Generation Paradigm (Rico's Insight)
* **The Root Problem:** Standard image generators output flat raster bitmaps without bounding boxes or box models. Replicating images by "eyeballing" causes pixel-level mismatches, text clipping on narrow mobile viewports, and spacing collisions.
* **The Solution:** Dual-pass mathematical co-generation (`DESIGN_SPEC.md`): AI subagents generate spatial coordinate manifests (bounding boxes, viewport budgets, font scale equations) alongside the visual render.
* **Layout Geometry Equation:** Formulated $W_{title} + Gap + W_{badge} \le W_{usable}$ ensuring 375px mobile viewports preserve $+55px$ margin safety with zero badge clipping.
* **Render Verification:** Captured and verified unclipped full-screen desktop preview (`html_rendered_preview_v4.png`).

---

## 3. Operational Discipline: Incident Log
* **Incident:** Premature Execution / Gate Skipping (19:13 – 19:19 PHT).
* **Observed Failure:** Concept image was ready in 56s, but agent prematurely refactored the entire stack (HTML/CSS/C#/DB) and resolved git conflicts before presenting the image, causing a 7-minute delay for operator feedback.
* **Invariant Enforced:**
  > **Image-First Hard Gate:** In generative design workflows, image generation is an atomic stop-and-wait gate. The agent must save the image to Desktop, present it immediately, and halt all tool calls. Zero code or git edits permitted until explicit human approval.

---

## 4. Current Repository State (`ricorai/Tenant-Portal`)
* **Local Commit:** `3f16d46` (Clean on `main`).
* **Key Invariants Synchronized:**
  * `DateTimeStyles.AdjustToUniversal` applied across all 4 `DateTime.TryParse` calls.
  * DNS `.portal` suffix wildcard matching.
  * Daily emergency pass rollover reset inside `EvaluateLeaseExpiry`.
  * `run_gateway_hidden.vbs` detached launcher committed.
* **ThinkPad Deployment Status:** Ready for `git pull origin main`, `call src\build.bat`, and `wscript.exe run_gateway_hidden.vbs`.
