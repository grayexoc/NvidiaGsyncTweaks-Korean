namespace NvidiaGsyncTweaks.Model;

/// <summary>
/// The curated list of hidden NVIDIA display-driver VRR values, grouped into pages.
/// Names, defaults and semantics come straight from the driver (nvlddmkm.sys 621.12): the registry table is built at RVA 16A2A90,
/// every value is a REG_DWORD in the adapter driver key. A missing value = the default shown here.
/// See docs/DRIVER-ANALYSIS.md for how each value was derived.
/// </summary>
internal static class Catalog
{
    public static readonly IReadOnlyList<TweakPage> Pages = BuildPages();

    /// <summary>Settings shown on the LFC page itself (below the main choice) - they feed or reuse the LFC multiplier logic.</summary>
    public static readonly IReadOnlyList<TweakGroup> LfcGroups = BuildLfcGroups();

    public static IEnumerable<string> AllNames =>
        Pages.SelectMany(p => p.Groups).Concat(LfcGroups).SelectMany(g => g.Tweaks).Select(t => t.Name)
             .Append(FrameSplit.ValueName)
             .Distinct(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<TweakGroup> BuildLfcGroups() =>
    [
        new TweakGroup("How fast LFC reacts",
            "The multiplier is chosen from a running estimate of your frame time. These two values decide how that estimate follows your game.",
        [
            Num("vrrDecayCoefficient", "Estimator responsiveness", "Weight of the newest frame in the frame-time estimate. Low = a steadier estimate (fewer multiplier switches near the threshold), high = follows quickly but jumps with every spike.",
                30, 1, 100, "%", step: 5),
            Num("anomalousFrametimeDetectionCooldown", "Hitch filter cooldown", "A frame more than 5x longer than the estimate is treated as a one-off hitch and ignored once; then the filter pauses for this many frames.",
                10, 1, 1000, "frames", TweakRisk.Caution,
                note: "Do not set 0: a permanent drop in frame rate would then never be followed."),
        ]),
        new TweakGroup("Frame generation pacing (experimental)",
            "The same multiplier logic is built into the scheduler for flips that carry a frame count - most likely the pacing of generated frames (DLSS frame generation). " +
            "That would explain why the fix also changes how frame-generated games feel, even far above the LFC range.",
        [
            Pick("enableVrrFlipMetering", "Flip metering", "Splits the time between metered flips into evenly timed sub-flips, using the LFC multiplier. Off = ordinary VRR path.",
                 3, TweakRisk.Experimental,
                 new Choice(3, "On (default)"),
                 new Choice(1, "Only flips that bring their own frame count"),
                 new Choice(0, "Off")),
        ]),
    ];

    private static TweakDef Num(string name, string title, string desc, uint def, double min, double max, string unit,
        TweakRisk risk = TweakRisk.Safe, double step = 1, string? note = null, double scale = 1) =>
        new(name, title, desc, TweakKind.Number, def, risk, Min: min, Max: max, Step: step, Unit: unit, Note: note, Scale: scale);

    /// <summary>One flag of a bitmask register; all rows of the same register share <paramref name="registerDefault"/>.</summary>
    private static TweakDef Flag(string name, uint registerDefault, uint mask, string title, string desc,
        TweakRisk risk = TweakRisk.Safe, string? note = null) =>
        new(name, title, desc, TweakKind.Bit, registerDefault, risk, BitMask: mask, Note: note);

    private static TweakDef Toggle(string name, string title, string desc, uint def, TweakRisk risk = TweakRisk.Safe,
        uint on = 1, uint off = 0, string? note = null) =>
        new(name, title, desc, TweakKind.Toggle, def, risk, OnValue: on, OffValue: off, Note: note);

    private static TweakDef Pick(string name, string title, string desc, uint def, TweakRisk risk, params Choice[] choices) =>
        new(name, title, desc, TweakKind.Choice, def, risk, choices);

    private static IReadOnlyList<TweakPage> BuildPages() =>
    [
        new TweakPage("smooth", "Smoothing & flicker", "Brightness flicker on VRR panels comes from uneven frame times. These values control how the driver evens them out.", "\uE706",
        [
            new TweakGroup("Frame smoothing (deflicker)",
                "The driver can hold back a flip by a few hundred microseconds so that alternating long / short frames become equally long. " +
                "That keeps the panel's refresh steady and reduces OLED brightness flicker, at the price of a little latency.",
            [
                Pick("enableVrrFrameSmoothing", "VRR frame smoothing",
                     "Master switch. Off = raw frame times, lowest latency, possibly more flicker with uneven pacing.",
                     3, TweakRisk.Safe, new Choice(3, "On"), new Choice(0, "Off")),
                Num("vrrDeflickerMaxUs", "Maximum smoothing delay", "Upper limit for the extra delay added to a single frame. 0 disables the smoothing.",
                    3000, 0, 20000, "µs", step: 100,
                    note: "Higher = bigger differences get corrected, but more added latency."),
                Num("vrrMonAdj", "Smoothing strength", "How much of the measured long/short imbalance is corrected per frame.",
                    50, 0, 100, "%", step: 5),
                Num("vrrDeflickerMaxPulsar", "Limit on G-SYNC Pulsar panels", "On panels flagged as Pulsar by NVIDIA's panel database the delay is additionally capped to this share of the shortest frame time.",
                    60, 0, 100, "%", step: 5),
            ]),
        ]),

        new TweakPage("vrr", "VRR behaviour", "When VRR is active, how monitors are detected, and the refresh limits the driver programs.", "Setting",
        [
            new TweakGroup("When VRR is active",
                null,
            [
                Toggle("VRRAlwaysOn", "Keep VRR always active", "Keep G-SYNC / VRR engaged even when no application requests it (desktop, windowed apps).",
                    0, TweakRisk.Caution, note: "The classic cause of OLED flicker on the desktop - leave off unless you know you need it."),
                Pick("forceVrrTearingMode", "Tearing behaviour", "Overrides whether a frame may be shown immediately (tearing) inside the VRR window.",
                     0, TweakRisk.Caution,
                     new Choice(0, "Per application (default)"),
                     new Choice(1, "Always allow tearing"),
                     new Choice(2, "Never tear (always wait for the VRR window)")),
                Toggle("disableOSVRR", "Disable Windows VRR support", "Do not report the 'OS VRR' capability (Windows' variable refresh rate for windowed / DX11 flip-model games). NVIDIA's own G-SYNC path is not affected.",
                    0, TweakRisk.Caution),
            ]),
            new TweakGroup("Monitor detection (Adaptive-Sync)",
                "Which signalling the driver accepts when it decides that a monitor supports VRR.",
            [
                Flag("enableAdaptiveSync", 0x17, 0x03, "DisplayPort Adaptive-Sync", "Detect VRR through the DisplayPort Adaptive-Sync capability (bits 0 and 1 behave identically).",
                     TweakRisk.Caution, "Off: VRR monitors on DisplayPort may no longer be detected."),
                Flag("enableAdaptiveSync", 0x17, 0x10, "HDMI VRR", "Detect VRR through the HDMI vendor-specific data block (HDMI VRR / FreeSync over HDMI).",
                     TweakRisk.Caution),
                Flag("enableAdaptiveSync", 0x17, 0x04, "VRR with multiple displays", "Allow VRR validation when several displays are active in the same group.",
                     TweakRisk.Caution),
                Toggle("enableVrrDisplayValidation", "Strict VRR display validation", "Reject VRR when a display group is not already in the VRR set.",
                    0, TweakRisk.Experimental),
            ]),
            new TweakGroup("Refresh limits and timing",
                "Values the driver uses when it programs the monitor's timing. Wrong values can make a monitor lose its signal - restart the driver and revert if the screen stays black.",
            [
                Num("vrrMaxRRx1k", "VRR maximum refresh cap", "Cap the highest refresh rate used in VRR: the driver pads the vertical blanking so that the panel never runs faster. 0 = no cap.",
                    0, 0, 1_000_000, "Hz", TweakRisk.Caution, 1000, scale: 1000,
                    note: "Too low a value shrinks the VRR window and can break LFC."),
                Num("vrrVblankExtension", "Vertical blank extension", "Extra blanking lines for the slowest VRR frame. Only used when the monitor does not report its minimum refresh rate.",
                    0x3FFF, 0, 65535, "lines", TweakRisk.Caution, 256),
                Pick("GsyncTimingOptimizationPolicy", "G-SYNC timing optimisation", "At modeset the driver can pick a pre-optimised timing from its monitor table.",
                     0, TweakRisk.Experimental, new Choice(0, "Allowed (default)"), new Choice(1, "Disabled")),
                Toggle("bIgnoreBlankingReqForDRR", "VRR on short-blanking modes", "Allow VRR / virtual refresh even when the mode has less than ~25 lines + 280 µs of vertical blanking.",
                    0, TweakRisk.Experimental),
                Num("vblCooldownTimeMs", "VBL policy cooldown", "Minimum time between two re-programmings of the driver's blanking policy.", 10000, 0, 600000, "ms", TweakRisk.Caution, 500),
            ]),
            new TweakGroup("Resource Manager (experimental)",
                "Low-level display RM switches that influence VRR. Rarely useful; effects are only partly understood.",
            [
                Num("RmVrrMaxFrameDeltaOverride", "Wake-up look-ahead limit", "Upper clamp for the look-ahead of the per-head VRR wake-up prediction. 0 = 100 ms (default).",
                    0, 0, 1000, "ms", TweakRisk.Experimental),
                Toggle("RmInternalVrrMclkOn2h1or", "Keep memory clock on (2 heads, 1 output)", "Keep the memory clock pinned for VRR when two heads share one output (very high pixel clocks / merged modes). Value 0 disables it.",
                    1, TweakRisk.Experimental, note: "Absent = on for current GPUs. Off may cause flicker or blanking in those modes."),
            ]),
        ]),

        new TweakPage("cursor", "Cursor & idle", "What happens on a VRR display while the game is not delivering frames (menus, loading screens, very low FPS).", "Pin",
        [
            new TweakGroup("Cursor updates while VRR is idle",
                "VRR would normally freeze the screen until the next game frame arrives - including the mouse cursor. The driver can inject cursor-only refreshes instead.",
            [
                Toggle("enableVrrCursorRelease", "Cursor release", "Keep the mouse cursor moving while the game stalls.", 1, TweakRisk.Caution,
                    note: "Off can make the cursor freeze in menus / loading screens."),
                Num("minVrrCursorFPS", "Minimum cursor update rate", "How often a cursor-only refresh is allowed when no game frame arrives (0 = never).",
                    30, 0, 240, "Hz",
                    note: "Every extra refresh can show as a small brightness step on OLED."),
                Num("vrrCursorMarginUs", "Cursor refresh guard time", "A cursor refresh is only injected if the next game frame is expected later than this margin, so it does not collide with a game frame.",
                    1500, 0, 20000, "µs", step: 100),
            ]),
        ]),

        new TweakPage("detect", "Smart detection", "Experimental: let the driver detect unstable frame times or slideshow-like content and react. Off by default - leave it off unless you want to experiment.", "Repair",
        [
            new TweakGroup("Detection",
                "When detection triggers, the driver switches the panel to a constant maximum frame period instead of following every frame - a way to avoid flicker from chaotic frame times.",
            [
                Pick("vrrSmartDetection", "Smart detection mode",
                     "Which detectors are active. The 'head mask' part is required for any visible effect.",
                     0, TweakRisk.Experimental,
                     new Choice(0, "Off (default)"),
                     new Choice(5, "Slideshow detection"),
                     new Choice(6, "Entropy (jitter) detection"),
                     new Choice(7, "Slideshow + entropy")),
            ]),
            new TweakGroup("Slideshow detection",
                "Triggers when a single frame takes extremely long (loading hitches, pause screens).",
            [
                Num("vrrSlideShowModeThresholdMs", "Slideshow threshold", "A frame longer than this counts as a slideshow.", 1000, 20, 60000, "ms", TweakRisk.Experimental, 50,
                    note: "Ignored on a built-in list of panels with fixed values."),
                Num("vrrSlideShowModePeriodMs", "Slideshow hold time", "How long the mode stays active after the last slow frame.", 2000, 0, 60000, "ms", TweakRisk.Experimental, 100),
            ]),
            new TweakGroup("Entropy (jitter) detection",
                "A running 'jitter score' grows with every frame that is longer than the one before and shrinks again over time. Above the threshold the mode switches on.",
            [
                Num("vrrEntropyThreshold", "Trigger level", "Score above which jitter mode starts (hundredths: 100 = frame time doubled).", 100, 1, 10000, "", TweakRisk.Experimental, 5),
                Num("vrrEntropyDecrease", "Noise floor", "Subtracted every frame (hundredths). Increases smaller than this never accumulate.", 30, 0, 1000, "", TweakRisk.Experimental, 5),
                Num("vrrEntropyDecay", "Decay per frame", "Per-mille multiplier on the score each frame. 1000 = no decay, 900 = -10 % per frame.", 1000, 1, 1000, "‰", TweakRisk.Experimental, 10,
                    note: "0 would disable the detector."),
                Num("vrrEntropyMax", "Maximum score", "Upper limit of the score (hundredths). Also decides how long the mode lingers after a burst.", 6000, 100, 100000, "", TweakRisk.Experimental, 100),
                Num("vrrEntropyDelayPeriodMs", "Hold time", "How long the mode stays on after the score last exceeded the trigger level.", 500, 0, 60000, "ms", TweakRisk.Experimental, 50),
                Num("vrrHysteresisParam", "Range-exit hysteresis", "How far below the VRR minimum frame period the estimate must fall before the 'out of VRR range' state is released.", 500000, 0, 5000000, "ns", TweakRisk.Experimental, 10000),
            ]),
        ]),
    ];
}
