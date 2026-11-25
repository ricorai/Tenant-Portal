# Tenant-Portal (Rental Wi-Fi Captive Gateway & 30-Day Subscription Manager)

> **Host:** ThinkPad L520 Server Node (Windows 11 Pro 64-bit · .NET Framework 4.8)  
> **Target:** 4 Rental Units Upstairs · Tenda F3 (Wireless N300 in AP Mode)  
> **Subnet:** `192.168.10.0/24` (Static gateway IP `192.168.10.1:3000`)  
> **Rate:** ₱150.00 / 30 Days per device · 30 Mbps Down / 15 Mbps Up (SQM fair-share)  
> **Binary:** Native C# Standalone Daemon (`28,160 bytes` / **~27.5 KB** · Zero external runtimes)

---

## 1. Codebase Architecture

```text
Tenant-Portal/
├── assets/
│   └── screenshots/              # UI previews and captures
├── data/
│   ├── schema.sql                # SQLite schema for authoritative leases & audit logs
│   ├── leases.example.json       # Template JSON transaction store
│   └── leases.json               # Live auto-persisting database (gitignored)
├── docs/
│   └── SESSION_MAP.md            # Topology, progression & design milestones
├── history/                      # Local design mockups & generative concepts (gitignored)
├── src/
│   ├── TenantGateway.cs          # Native C# HTTP daemon (REST API, static files, SendARP MAC detector)
│   └── build.bat                 # Zero-dependency compilation script using OS csc.exe
├── web/
│   ├── index.html                # Tenant Mobile Bento Splash (100% offline-ready, no CDNs)
│   ├── admin.html                # Mom's Zero-Jargon Admin Dashboard (big touch buttons, 1-tap approve)
│   ├── css/
│   │   └── portal.css            # Ryos Default Theme (#F4F4F6 / #FFFFFF / #0E0E0E)
│   ├── js/
│   │   ├── portal.js             # Tenant frontend logic (countdown, GCash QR, PIN relink)
│   │   └── admin.js              # Mom's admin actions (live sync, 1-tap approve, cash tenant add)
│   └── images/
│       ├── gcash_qr.example.png  # Sanitized template QR code
│       └── gcash_qr.png          # Active payment QR asset
├── run_gateway_hidden.vbs        # Detached VBS launcher for ThinkPad L520
├── TenantGateway.exe             # Compiled native executable (34,816 bytes)
└── README.md
```

---

## 2. 🛡️ Security & Privacy Notice (Open-Source Standards)

This repository is a public reference implementation of an offline-first Wi-Fi captive gateway and 30-day subscription manager. To protect privacy and adhere to secure development practices:

* **No Live Secrets in Git:** Active tenant lease databases (`data/leases.json`), personal InstaPay GCash QR assets (`web/images/gcash_qr_live.png`), and live mobile numbers are strictly gitignored.
* **Sanitized Template Placeholders:** Clean templates (`data/leases.example.json`, `web/images/gcash_qr.example.png`, `09XX-XXX-XXXX`) are provided for study, deployment, and testing.
* **Production Setup:** When deploying for your own property, copy `data/leases.example.json` to `data/leases.json`, drop your payment QR asset into `web/images/gcash_qr.png`, and update the account display strings in `web/index.html` and `web/js/portal.js`.

---

## 3. Compilation & Verification (Four-Point Handshake)

Built using the pre-installed standard Windows .NET C# compiler (`csc.exe`):

```cmd
cd src
build.bat
```

* **Point 1 (Path):** `Tenant-Portal\TenantGateway.exe`
* **Point 2 (Compile & Size):** Exit code `0`, `0 warnings`, exact size: **`28,160 bytes`** (**~27.5 KB**).
* **Point 3 (Behavior):** Serves static assets on `http://localhost:3000/`, intercepts captive portal canaries, auto-detects physical MAC addresses via Win32 `SendARP` (`iphlpapi.dll`), and exposes the complete REST API.
* **Point 4 (Clean Teardown):** Pure console daemon cleanly terminable via `Ctrl+C` or PID kill with zero background resident bloat left behind.

---

## 4. Core Features & UX Workflows

### A. Tenant Mobile Splash (`index.html`)
* **100% Offline-Ready:** Zero CDN dependencies, uses system UI fonts and embedded inline SVGs so pages load instantly even when the tenant has ₱0 mobile load.
* **Unit Selection:** Single-tap selection for Rooms 1, 2, 3, and 4.
* **The "No Mobile Data" 5-Minute Emergency Pass:**
  * One-tap `[ ⏱️ Need internet to open GCash? Get 5-Minute Pass ]`
  * Activates 5 minutes of 1 Mbps connection so tenants can open GCash, transfer ₱150, and submit their reference.
  * Capped at 5 daily attempts per MAC to prevent abuse.
* **GCash Payment Box:**
  * Clean vector QR code with instant `[ 📥 Save QR to Photos ]` helper.
  * `[ 📋 Copy Number ]` button for Express Send to `0917-XXX-XXXX` (Rico R.).
  * Physical reference notice: *"Printed QR code also posted on 2nd floor hallway wall."*
* **13-Digit Reference Submission:** Validates GCash reference, moves to pending state, assigns a recovery PIN, and starts temporary pass while Mom verifies.
* **4-Digit Recovery PIN Re-Link:**
  * Protects against iOS/Android randomized MAC addresses.
  * `[ 🔑 Already Paid? Re-link Device with 4-Digit PIN ]` modal lets tenants swap active lease to new MAC address without door-knocking (max 2 swaps/month).
* **Active Status Display:** Visiting `http://wifi.portal` or `index.html` displays real-time countdown (`28d 14h 22m 30s remaining`) and assigned recovery PIN.

### B. Mom's Zero-Jargon Admin Dashboard (`admin.html`)
* **PIN Protected:** Simple 4-digit PIN gate (Default: `1234`).
* **High Contrast, Big Buttons:** Designed specifically for Mom's phone.
* **Pending GCash Queue:** Shows incoming GCash reference numbers with a big 1-tap green `[ ✅ Approve 30 Days for Room X ]` button.
* **Add Cash Tenant:** Form for tenants who hand cash directly to Mom (`[ 💵 Grant 30 Days Cash Pass ]`), immediately generating a 4-digit PIN for Mom to give the tenant.
* **Live Status Table:** Lists all 4 rooms, remaining days/hours, recovery PINs, and a 1-tap `[ Turn Off ]` revoke button.

---

## 5. REST API Specification

| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `GET` | `/api/status?mac=...` | Returns lease state (`ACTIVE`, `PENDING`, `EXPIRED`, `EMERGENCY_PASS`), remaining days/hours, PIN, and daily passes used. |
| `POST` | `/api/emergency-pass` | Grants 5-min temporary pass at 1 Mbps; enforces max 5 passes/day. |
| `POST` | `/api/submit-payment` | Submits 13-digit GCash ref # for a room; sets status to `PENDING`. |
| `POST` | `/api/relink-pin` | Re-links active lease to a new randomized MAC using Room # and 4-digit PIN. |
| `GET` | `/api/admin/pending` | Mom's pending verification queue (Requires `X-Admin-PIN`). |
| `GET` | `/api/admin/leases` | Lists all 4 unit leases, countdowns, and PINs. |
| `POST` | `/api/admin/approve` | Mom approves pending reference; sets status to `ACTIVE` for 30 days. |
| `POST` | `/api/admin/add-cash` | Mom grants 30-day cash pass; generates recovery PIN. |
| `POST` | `/api/admin/revoke` | Revokes active pass for a MAC. |
| `GET` | `/api/detect-mac` | Returns client IP and detected MAC address. |

---

## 6. Deployment on ThinkPad Server Node

To run on the ThinkPad server node:

```cmd
git clone https://github.com/ricorai/Tenant-Portal.git
cd Tenant-Portal
TenantGateway.exe --port 3000
```

*(To bind directly to HTTP port 80 for captive portal interception, run in an elevated command prompt: `TenantGateway.exe --port 80`)*
