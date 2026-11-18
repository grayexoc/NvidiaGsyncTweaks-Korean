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
    private static readonly TweakDef Def = new(ValueName, "프레임 배수 전환 방식", "", TweakKind.Number, FrameSplit.DefaultRaw);

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
            BodyStrong("드라이버가 언제부터 프레임 더블링(LFC)을 시작해야 할까요?"),
            RadioButtons(
                [
                    "NVIDIA 기본값 - 2배로 나눈 프레임 시간이 VRR 범위의 중앙에 더 가까워질 때 더블링을 시작합니다.",
                    "최저 배수 - 프레임 속도가 VRR 최저 주사율 아래로 떨어질 때까지 1배를 유지합니다(권장).",
                    "사용자 지정 - NVIDIA의 선택 로직을 유지하면서 현재 배수를 유지하려는 정도를 조정합니다.",
                ],
                mode, SetMode),
            mode == 2 ? CustomEditor(c, pending, SetRaw) : null,
            HStack(8,
                Ui.Muted($"레지스트리 값  {ValueName}  ="),
                Ui.Mono($"0x{pending.Raw:X8}"),
                changed ? Ui.Pill("변경됨", "accent") : null,
                pending.Raw != FrameSplit.DefaultRaw && !changed ? Ui.Pill("레지스트리에 적용됨", "success") : null)));

        return VStack(16,
            Ui.PageHeader("LFC / 프레임 더블링",
                "고주사율 G-SYNC / VRR 모니터에서 NVIDIA 드라이버는 모니터의 VRR 최저 주사율에 도달하기 훨씬 전부터 프레임을 더블링합니다. 240/360Hz 패널에서는 약 58~65FPS부터 시작할 수 있습니다. " +
                "이로 인해 패널 주사율이 약 60Hz와 120Hz 사이를 오가며 OLED에서는 VRR 밝기 깜박임, 다른 패널에서는 끊김으로 나타날 수 있습니다."),
            modeCard,
            Preview(c, current, pending),
            ByRefreshRate(c, pending),
            VStack(16, Catalog.LfcGroups.Select(g => TweakRows.Group(c, g).WithKey(g.Title)).ToArray()),
            Ui.Panel(VStack(6,
                BodyStrong("알아둘 점"),
                Ui.Muted("• 전환 지점이 VRR 최저 주사율로 이동할 뿐 사라지는 것은 아닙니다. 해당 프레임 속도 부근에서 움직이는 게임은 여전히 깜박일 수 있습니다."),
                Ui.Muted("• 최저 주사율에 가까우면 프레임이 늦게 도착해 반복 표시되면서 작은 끊김이 생길 수 있습니다. 약 55FPS 이상에서는 여유가 충분합니다."),
                Ui.Muted("• 드라이버에는 LFC를 완전히 끄는 스위치가 없습니다. 이 선택 항목이 더블링 시작 지점을 결정하는 유일한 설정입니다. 이외에 임계값에 영향을 주는 것은 다음 범위입니다. " +
                         "임계값은 Windows 디스플레이 모드의 주사율(120Hz로 낮추면 문제가 '해결'되는 이유)과 모니터가 EDID로 보고하는 VRR 최저 주사율을 기준으로 계산됩니다."),
                Ui.Muted("• 이 GPU에 연결된 모든 디스플레이에 적용됩니다. 드라이버 시작 시 값을 읽으며, '적용 후 드라이버 다시 시작'이 이 과정을 수행합니다."),
                Ui.Muted("• 드라이버를 완전히 새로 설치하면 값이 초기화될 수 있지만 일반 업데이트에서는 유지됩니다."))));
    }

    // ------------------------------------------------------------------------------------------------
    /// <summary>Where doubling starts for monitors of different max refresh rates (same VRR minimum) - makes the "주사율이 높을수록 더 일찍 더블링" rule visible.</summary>
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
                Ui.Muted("모니터 최대 주사율").Grid(row: 0, column: 0),
                Ui.Muted("NVIDIA 기본값(시작 / 해제)").Grid(row: 0, column: 1),
                Ui.Muted("현재 설정").Grid(row: 0, column: 2)),
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
            BodyStrong("다른 모니터에서는 언제 더블링이 시작될까요?"),
            Ui.Muted($"첫 번째 FPS 값 아래에서 프레임 더블링이 시작되고 두 번째 값 위에서 해제됩니다. VRR 최저 주사율: {min:0}Hz. 주사율이 높을수록 NVIDIA 기본값은 더 일찍 더블링을 시작합니다."),
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
                    BodyStrong("현재 배수 유지 보너스"),
                    Ui.Muted("드라이버가 현재 사용 중인 배수를 얼마나 선호하는지 정합니다. 15%는 NVIDIA 기본값, 0%는 보너스 없음, " +
                             "값이 높을수록 더 넓은 FPS 범위에서 현재 배수를 유지합니다.")).Grid(row: 0, column: 0),
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
            Math.Abs(on - off) < 0.2 ? $"{on:F1}FPS 미만" : $"{on:F1}FPS 미만(다시 {off:F1}FPS 초과 시 해제)";

        var rows = new List<Element?>
        {
            Grid(
                columns: [GridSize.Px(70), GridSize.Star(), GridSize.Star()],
                rows: [GridSize.Auto],
                Ui.Muted("FPS").Grid(row: 0, column: 0),
                Ui.Muted("NVIDIA 기본값").Grid(row: 0, column: 1),
                Ui.Muted(pending.Raw == driverDefault.Raw ? "현재 설정(기본값과 동일)" : "현재 설정").Grid(row: 0, column: 2)),
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
            BodyStrong("모니터는 어떻게 동작할까요?"),
            Ui.Muted("주 디스플레이 정보를 바탕으로 자동 입력됩니다. 모니터에 맞게 조정하십시오(VRR 범위는 모니터 OSD / CRU / NVIDIA 앱에서 확인할 수 있습니다)."),
            HStack(16,
                NumberBox(c.MaxHz, v => { if (!double.IsNaN(v)) c.SetMaxHz(v); }, header: "최대 주사율(Hz)").Range(60, 1000).SpinButtons().Width(190),
                NumberBox(c.VrrMinHz, v => { if (!double.IsNaN(v)) c.SetVrrMinHz(v); }, header: "VRR 최저 주사율(Hz)").Range(10, 240).SpinButtons().Width(190)),
            HStack(24,
                VStack(2, Ui.Muted("NVIDIA 기본값"), BodyStrong($"더블링 {Range(onD, offD)}")),
                VStack(2, Ui.Muted("현재 설정"), BodyStrong($"더블링 {Range(onP, offP)}").Foreground(pending.Raw == driverDefault.Raw ? Theme.PrimaryText : Theme.AccentText))),
            VStack(2, rows.ToArray()),
            Ui.Muted("높은 FPS에서 안정적으로 내려오는 상황입니다. 더블링 시작 및 해제 임계값 사이에서는 드라이버가 기존에 사용하던 배수를 유지합니다.")));
    }
}
