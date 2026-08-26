# Mountain Everest 60 — Custom RGB Software (Chat Export)

## Can I write custom software for the Mountain Everest 60's RGB?

**Official software:** Mountain's driver is **Base Camp** — supports per-key RGB, macros, remapping, Razer Chroma sync. Reviewers noted it's buggy (freezes on lighting/macro tabs), Windows-only, capped at 5 profiles with no per-game auto-switching.

**Existing open-source implementation:** [BaseCamp-Linux](https://github.com/ramisotti13-eng/BaseCamp-Linux) by ramisotti13-eng — unofficial Linux companion app covering Everest Max, Everest 60, Makalu mice, and DisplayPad. Actively maintained (GPL v3 + Non-Commercial license). Someone has already reverse-engineered the HID protocol.

**Options to build your own:**
1. Fork/extend BaseCamp-Linux (protocol already solved)
2. Sniff USB traffic (Wireshark + USBPcap) and replicate HID feature reports yourself
3. Check for QMK/VIA compatibility (unlikely uplift here, but worth a check on other boards)

## Would the Linux fork give enough info to write Windows software? What tech stack?

Yes — the keyboard is a standard HID device (VID `0x3282`, PID `0x0005` ANSI / `0x0006` ISO), so the protocol isn't OS-specific. Cloned the repo and inspected `devices/everest60/controller.py` directly:

```
Report size: 65 bytes (Report ID 0x00 + 64 bytes data)
Magic bytes [2..4] = 0x46 0x23 0xEA on every command

SetMode         cmd=0x16   [5]=0x01  [9]=effect code
SendModeDetails cmd=0x17   [7]=speed×25 [8]=brightness×25 [9]=color_mode
                           [12..14]=color1 RGB [15..17]=color2 RGB
Custom Begin    cmd=0x34   [5]=brightness×25 [6]=0xC0
Custom Map      cmd=0x35   [5]=0x0E(more)/0x0A(last), then 14× (hw_index,R,G,B) = 56 bytes
Custom End      cmd=0x36
Latch/Commit    cmd=0x1a   [5]=effect code
```

Effect codes: Static=0x01, Wave=0x02, Tornado=0x03, Breathing=0x04, Reactive=0x05, Matrix=0x06, Custom=0x07, Yeti=0x08, Off=0x09.

Bottom row hardware LED indices (LCTL, LWIN, LALT, SPC, RALT, FN, ←, ↓, →):
`[105, 106, 107, 110, 113, 115, 119, 120, 121]`

**License note:** GPL v3 + Non-Commercial. Fine to read/port for personal use; can't reuse the code directly if monetizing — would need to re-derive via USB sniffing instead.

**Recommended stack:** Native C# (WPF or WinUI 3) + **HidSharp** for feature report read/write. Reasoning: HID access from Node/Electron requires native bindings (`node-hid`) that are more fragile for a background/tray driver; .NET gives better system tray, autostart, and low resource footprint for something meant to run persistently like Base Camp does.

## Goal: keep the rest of the RGB config untouched, use the bottom row as a progress bar (battery/volume/brightness)

**Key finding:** The `0x35` custom map packet is per-LED addressed (`hw_index, r, g, b` tuples), not a full-framebuffer overwrite — you can write just the LEDs you want changed.

**Limitation:** Per-LED addressing only works in **Custom mode** (`0x07`). Firmware effects (Wave, Tornado, Breathing, Rainbow) are generated on-device with no way to poke individual LEDs mid-animation. So:
- ✅ Achievable: static custom per-key layout everywhere, with the bottom row overlaid/updated independently.
- ❌ Not achievable: a firmware effect running elsewhere while the bottom row is driven separately.

**Design:**
1. Push a full 64-key + 44-side-LED static Custom-mode map once at startup (the user's chosen "background" look).
2. On each battery/volume/brightness change, send only a 9-entry `0x35` packet for the bottom row.
3. Test whether the full `0x34`→`0x35`→`0x36`→`0x1a` sequence is needed every update, or whether steady-state updates can skip straight to `0x35` (+ maybe `0x1a`).

```csharp
static readonly byte[] BottomRowHw = { 105, 106, 107, 110, 113, 115, 119, 120, 121 };

byte[] BuildMapPacket(IEnumerable<(byte hw, byte r, byte g, byte b)> entries, bool isLast)
{
    var buf = new byte[65]; // report ID 0x00 + 64 bytes
    buf[1] = 0x35;
    buf[2] = 0x46; buf[3] = 0x23; buf[4] = 0xEA; // magic
    buf[5] = isLast ? (byte)0x0A : (byte)0x0E;
    int pos = 9;
    foreach (var (hw, r, g, b) in entries)
    {
        buf[pos] = hw; buf[pos+1] = r; buf[pos+2] = g; buf[pos+3] = b;
        pos += 4;
    }
    return buf;
}
```

## What about writing custom wave effects and sending updates at 30fps?

**Good news:** Everest 60 has **no separate flash-persist step** (`has_persist=False` in the source, unlike the Everest Max). The `0x34→0x35→0x36→0x1a` sequence is a volatile "apply to live LED buffer" operation — flash wear from updating every frame is not a concern.

**Real constraint: USB control-transfer overhead.** A full-board update = 108 LEDs / 14 per packet = 8 map packets + begin + end + latch = **11 HID feature report writes per frame**. At 30fps (33ms budget), this is tight if each write takes 2–5ms via `HidD_SetFeature`.

**Recommendations:**
1. **Send deltas, not full frames** — keep a software-side 108-LED framebuffer, diff against the previous frame, only send `0x35` packets for LEDs that changed. A wave effect typically only touches a handful of LEDs per frame → usually 1 packet.
2. **Test whether `0x34`/`0x36`/`0x1a` need resending every frame**, or only once when entering Custom mode — steady-state might just need `0x35` deltas.
3. **Run HID writes on a dedicated thread**, not the UI thread.
4. **Benchmark actual write latency on your hardware** before committing to 30fps — drop to 15–20fps if writes spike, or limit simultaneously-animating LEDs.
5. Keep animation and progress-bar writes on the same queue/thread to avoid racing writers.

---
*Source repo used for protocol reverse-engineering: [github.com/ramisotti13-eng/BaseCamp-Linux](https://github.com/ramisotti13-eng/BaseCamp-Linux) (GPL v3 + Non-Commercial)*
