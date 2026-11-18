using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NvidiaGsyncTweaks.Model;
using static Microsoft.UI.Reactor.Factories;

namespace NvidiaGsyncTweaks.UI;

/// <summary>
/// Flagship page: when does the driver start frame doubling (LFC)?  Wraps the single DWORD "vrrFrameSplitBehavior" into
/// understandable options and shows what the monitor will do (using the re-implemented selection logic of the driver).
/// </summary>
internal static class LfcPage
{
    public const string ValueName = FrameSplit.ValueName;
    private static readonly TweakDef Def = new(ValueName, "Frame split behaviour", "", TweakKind.Number, FrameSplit.DefaultRaw);

    public static Element Build(Ctx c)
    {
        var current = new FrameSplit(c.EffectiveCurrent(ValueName, FrameSplit.DefaultRaw));
        var pending = new FrameSplit(c.Effective(ValueName, FrameSplit.DefaultRaw));
        bool changed = c.IsPending(ValueName);

        int mode = pending.Raw == FrameSplit.DefaultRaw ? 0
                 : pending.LowestFit ? 1
                 : 2;

        void SetRaw(FrameSplit s) => c.Set(Def, s.Raw);

        void SetMode(int m)
        {
            switch (m)
            {
                case 0: SetRaw(FrameSplit.Default); break;
                case 1: SetRaw(FrameSplit.LowestMultiplier); break;
                default: SetRaw(FrameSplit.Make(false, 50, false)); break; // NVIDIA default logic, stickier (value from the community thread)
            }
        }

        var modeCard = Ui.Panel(VStack(12,
            BodyStrong("When should the driver start frame doubling (LFC)?"),
            RadioButtons(
                [
                    "NVIDIA default  -  doubling starts when the doubled frame time is closer to the middle of the VRR window",
                    "Lowest multiplier  -  stay at 1x until the frame rate falls below the VRR minimum   (recommended)",
                    "Custom  -  keep NVIDIA's logic, but tune how sticky the current multiplier is",
                ],
                mode, SetMode),
            mode == 2 ? CustomEditor(c, pending, SetRaw) : null,
            HStack(8,
                Ui.Muted($"Registry value  {ValueName}  ="),
                Ui.Mono($"0x{pending.Raw:X8}"),
                changed ? Ui.Pill("Changed", "accent") : null,
                pending.Raw != FrameSplit.DefaultRaw && !changed ? Ui.Pill("Active in registry", "success") : null)));

        return VStack(16,
            Ui.PageHeader("LFC / frame doubling",
                "On high-refresh G-SYNC / VRR monitors the NVIDIA driver starts doubling frames long before your monitor's VRR minimum - around 58-65 FPS on 240/360 Hz panels. " +
                "That flips the panel between ~60 Hz and ~120 Hz and shows up as VRR brightness flicker (OLED) or stutter."),
            modeCard,
            Preview(c, current, pending),
            ByRefreshRate(c, pending),
            VStack(16, Catalog.LfcGroups.Select(g => TweakRows.Group(c, g).WithKey(g.Title)).ToArray()),
            Ui.Panel(VStack(6,
                BodyStrong("Good to know"),
                Ui.Muted("•  The switch point moves to the VRR minimum; it does not disappear. A game hovering right around that frame rate can still flicker."),
                Ui.Muted("•  Close to the minimum a late frame can force a repeat (small hitch). Above ~55 FPS there is plenty of headroom."),
                Ui.Muted("•  The driver has no switch to turn LFC off, and this choice is the only setting that decides where doubling starts. What moves the thresholds besides it is the window " +
                         "they are computed from: the refresh rate of your Windows display mode (why 120 Hz 'fixes' it) and the VRR minimum your monitor reports (EDID)."),
                Ui.Muted("•  Applies to every display on this GPU. The values are read when the driver starts - Apply & restart does that for you."),
                Ui.Muted("•  A clean driver install can reset the value; a normal update keeps it."))));
    }

    // ------------------------------------------------------------------------------------------------
    /// <summary>Where doubling starts for monitors of different max refresh rates (same VRR minimum) - makes the "higher refresh = earlier doubling" rule visible.</summary>
    private static Element ByRefreshRate(Ctx c, FrameSplit pending)
    {
        double min = Math.Clamp(c.VrrMinHz, 10, 500);
        string Fmt((double On, double Off) t) =>
            double.IsNaN(t.On) ? "-" : Math.Abs(t.On - t.Off) < 0.2 ? $"{t.On:F1} FPS" : $"{t.On:F1} / {t.Off:F1} FPS";

        var rows = new List<Element?>
        {
            Grid(
                columns: [GridSize.Px(150), GridSize.Star(), GridSize.Star()],
                rows: [GridSize.Auto],
                Ui.Muted("Monitor max refresh").Grid(row: 0, column: 0),
                Ui.Muted("NVIDIA default (on / off)").Grid(row: 0, column: 1),
                Ui.Muted("With your setting").Grid(row: 0, column: 2)),
        };
        foreach (var hz in new double[] { 120, 144, 165, 240, 360, 480 })
        {
            if (hz <= min + 1) continue;
            var d = LfcModel.DoublingThresholds(hz, min, FrameSplit.Default);
            var p = LfcModel.DoublingThresholds(hz, min, pending);
            bool same = Math.Abs(d.On - p.On) < 0.1 && Math.Abs(d.Off - p.Off) < 0.1;
            rows.Add(Grid(
                columns: [GridSize.Px(150), GridSize.Star(), GridSize.Star()],
                rows: [GridSize.Auto],
                Body($"{hz:0} Hz").Grid(row: 0, column: 0),
                Body(Fmt(d)).Grid(row: 0, column: 1),
                (same ? Body(Fmt(p)) : BodyStrong(Fmt(p)).Foreground(Theme.AccentText)).Grid(row: 0, column: 2)));
        }

        return Ui.Panel(VStack(10,
            BodyStrong("Where does doubling start on other monitors?"),
            Ui.Muted($"Frame doubling switches on below the first FPS value (and off again above the second one). VRR minimum: {min:0} Hz. The higher the refresh rate, the earlier NVIDIA's default doubles."),
            VStack(2, rows.ToArray())));
    }

    private static Element CustomEditor(Ctx c, FrameSplit pending, Action<FrameSplit> setRaw)
    {
        int bonus = 100 - pending.Stickiness;                   // 0 = no bonus, 15 = NVIDIA default
        bonus = pending.NoSticky ? 0 : Math.Clamp(bonus, 0, 100);
        return VStack(8,
            Grid(
                columns: [GridSize.Star(), GridSize.Px(80)],
                rows: [GridSize.Auto],
                VStack(2,
                    BodyStrong("Stickiness bonus"),
                    Ui.Muted("How strongly the driver prefers the multiplier it is already using. 15 % = NVIDIA default, 0 % = none, " +
                             "higher = it holds the current multiplier over a wider FPS range.")).Grid(row: 0, column: 0),
                Body($"{bonus} %").HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center).Grid(row: 0, column: 1)),
            Slider(bonus, 0, 100, v => setRaw(FrameSplit.Make(false, 100 - (int)Math.Round(v), false)))
                .StepFrequency(1))
            .Margin(32, 4, 0, 4);
    }

    // ------------------------------------------------------------------------------------------------
    private static Element Preview(Ctx c, FrameSplit current, FrameSplit pending)
    {
        double max = Math.Max(c.MaxHz, 60), min = Math.Clamp(c.VrrMinHz, 10, max - 1);
        var driverDefault = FrameSplit.Default;

        var (onD, offD) = LfcModel.DoublingThresholds(max, min, driverDefault);
        var (onP, offP) = LfcModel.DoublingThresholds(max, min, pending);

        string Range(double on, double off) =>
            Math.Abs(on - off) < 0.2 ? $"below {on:F1} FPS" : $"below {on:F1} FPS (turns off again above {off:F1})";

        var rows = new List<Element?>
        {
            Grid(
                columns: [GridSize.Px(70), GridSize.Star(), GridSize.Star()],
                rows: [GridSize.Auto],
                Ui.Muted("FPS").Grid(row: 0, column: 0),
                Ui.Muted("NVIDIA default").Grid(row: 0, column: 1),
                Ui.Muted(pending.Raw == driverDefault.Raw ? "Your setting (= default)" : "With your setting").Grid(row: 0, column: 2)),
        };

        foreach (var fps in new double[] { 30, 40, 48, 50, 55, 60, 72, 90, 120 })
        {
            if (fps > max) continue;
            var d = LfcModel.Evaluate(fps, 1, max, min, driverDefault);
            var p = LfcModel.Evaluate(fps, 1, max, min, pending);
            bool diff = d.Multiplier != p.Multiplier;
            rows.Add(Grid(
                columns: [GridSize.Px(70), GridSize.Star(), GridSize.Star()],
                rows: [GridSize.Auto],
                Body($"{fps:0}").Grid(row: 0, column: 0),
                Body($"{d.Multiplier}x  ->  {d.RefreshHz:0} Hz").Grid(row: 0, column: 1),
                (diff ? BodyStrong($"{p.Multiplier}x  ->  {p.RefreshHz:0} Hz").Foreground(Theme.AccentText)
                      : Body($"{p.Multiplier}x  ->  {p.RefreshHz:0} Hz")).Grid(row: 0, column: 2)));
        }

        return Ui.Panel(VStack(12,
            BodyStrong("What will my monitor do?"),
            Ui.Muted("Pre-filled from your primary display. Adjust it to match your monitor (VRR range is shown in the monitor OSD / CRU / NVIDIA app)."),
            HStack(16,
                NumberBox(c.MaxHz, v => { if (!double.IsNaN(v)) c.SetMaxHz(v); }, header: "Max refresh rate (Hz)").Range(60, 1000).SpinButtons().Width(190),
                NumberBox(c.VrrMinHz, v => { if (!double.IsNaN(v)) c.SetVrrMinHz(v); }, header: "VRR minimum (Hz)").Range(10, 240).SpinButtons().Width(190)),
            HStack(24,
                VStack(2, Ui.Muted("NVIDIA default"), BodyStrong($"Doubling {Range(onD, offD)}")),
                VStack(2, Ui.Muted("With your setting"), BodyStrong($"Doubling {Range(onP, offP)}").Foreground(pending.Raw == driverDefault.Raw ? Theme.PrimaryText : Theme.AccentText))),
            VStack(2, rows.ToArray()),
            Ui.Muted("Steady frame rate, coming from a higher FPS. Between the 'on' and 'off' threshold the driver keeps whichever multiplier it is already using.")));
    }
}
