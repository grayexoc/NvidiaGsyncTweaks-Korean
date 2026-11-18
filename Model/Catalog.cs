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
        new TweakGroup("LFC 반응 속도",
            "배수는 실시간 프레임 시간 추정값을 바탕으로 선택됩니다. 다음 두 값은 추정값이 게임의 프레임 변화에 반응하는 방식을 결정합니다.",
        [
            Num("vrrDecayCoefficient", "추정값 반응성", "프레임 시간 추정에서 가장 최근 프레임에 부여하는 가중치입니다. 낮으면 추정값이 안정되어 임계값 부근의 배수 전환이 줄고, 높으면 빠르게 반응하지만 순간적인 프레임 급등락에도 흔들립니다.",
                30, 1, 100, "%", step: 5),
            Num("anomalousFrametimeDetectionCooldown", "프레임 끊김 필터 대기 시간", "추정값보다 5배 이상 긴 프레임을 일회성 끊김으로 간주해 한 번 무시한 뒤, 지정한 프레임 수만큼 필터를 일시 중지합니다.",
                10, 1, 1000, "프레임", TweakRisk.Caution,
                note: "0으로 설정하지 마십시오. 프레임 속도가 지속적으로 떨어져도 추정값이 이를 따라가지 못합니다."),
        ]),
        new TweakGroup("프레임 생성 페이싱(실험적)",
            "프레임 수 정보를 포함한 플립을 처리하는 스케줄러에도 동일한 배수 로직이 포함되어 있습니다. DLSS 프레임 생성 프레임의 페이싱일 가능성이 있습니다. " +
            "따라서 LFC 범위보다 훨씬 높은 FPS에서도 이 수정이 프레임 생성 게임의 체감에 영향을 줄 수 있습니다.",
        [
            Pick("enableVrrFlipMetering", "플립 간격 측정", "측정 대상 플립 사이의 시간을 LFC 배수를 사용해 균등한 하위 플립으로 나눕니다. 끄면 일반 VRR 경로를 사용합니다.",
                 3, TweakRisk.Experimental,
                 new Choice(3, "켜짐(기본값)"),
                 new Choice(1, "프레임 수 정보를 자체적으로 전달하는 플립만"),
                 new Choice(0, "꺼짐")),
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
        new TweakPage("smooth", "스무딩 및 깜박임", "VRR 패널의 밝기 깜박임은 불균일한 프레임 시간에서 발생할 수 있습니다. 이 값들은 드라이버가 프레임 시간 차이를 완화하는 방식을 제어합니다.", "\uE706",
        [
            new TweakGroup("프레임 스무딩(깜박임 완화)",
                "드라이버는 수백 마이크로초 동안 플립을 지연시켜 번갈아 나타나는 긴 프레임과 짧은 프레임의 시간을 비슷하게 맞출 수 있습니다. " +
                "패널 주사율을 안정시키고 OLED 밝기 깜박임을 줄이는 대신 약간의 지연 시간이 추가됩니다.",
            [
                Pick("enableVrrFrameSmoothing", "VRR 프레임 스무딩",
                     "마스터 스위치입니다. 끄면 원래 프레임 시간을 사용해 지연 시간은 최소화되지만 페이싱이 불균일할 때 깜박임이 늘어날 수 있습니다.",
                     3, TweakRisk.Safe, new Choice(3, "켜짐"), new Choice(0, "꺼짐")),
                Num("vrrDeflickerMaxUs", "최대 스무딩 지연", "단일 프레임에 추가할 수 있는 지연 시간의 상한입니다. 0이면 스무딩을 비활성화합니다.",
                    3000, 0, 20000, "µs", step: 100,
                    note: "값이 높으면 더 큰 차이를 보정하지만 추가 지연 시간도 늘어납니다."),
                Num("vrrMonAdj", "스무딩 강도", "측정된 긴 프레임과 짧은 프레임의 불균형을 프레임마다 얼마나 보정할지 정합니다.",
                    50, 0, 100, "%", step: 5),
                Num("vrrDeflickerMaxPulsar", "G-SYNC Pulsar 패널 제한", "NVIDIA 패널 데이터베이스에서 Pulsar로 표시된 패널에서는 지연 시간이 가장 짧은 프레임 시간의 이 비율을 초과하지 않도록 추가 제한됩니다.",
                    60, 0, 100, "%", step: 5),
            ]),
        ]),

        new TweakPage("vrr", "VRR 동작", "VRR이 활성화되었을 때 모니터를 감지하는 방식과 드라이버가 설정하는 주사율 제한을 조정합니다.", "Setting",
        [
            new TweakGroup("VRR 활성화 상태",
                null,
            [
                Toggle("VRRAlwaysOn", "VRR 항상 활성화", "애플리케이션이 요청하지 않아도(바탕 화면 및 창 모드 앱 포함) G-SYNC / VRR을 계속 활성화합니다.",
                    0, TweakRisk.Caution, note: "바탕 화면에서 OLED 깜박임을 일으키는 대표적인 원인입니다. 필요한 경우가 아니라면 끄십시오."),
                Pick("forceVrrTearingMode", "티어링 동작", "VRR 범위 안에서 프레임을 즉시 표시해 티어링을 허용할지 재정의합니다.",
                     0, TweakRisk.Caution,
                     new Choice(0, "애플리케이션 설정 따름(기본값)"),
                     new Choice(1, "항상 티어링 허용"),
                     new Choice(2, "티어링 금지(VRR 표시 시점까지 항상 대기)")),
                Toggle("disableOSVRR", "Windows VRR 지원 비활성화", "'OS VRR' 기능을 보고하지 않습니다(창 모드 / DX11 플립 모델 게임을 위한 Windows 가변 주사율 기능). NVIDIA 자체 G-SYNC 경로에는 영향을 주지 않습니다.",
                    0, TweakRisk.Caution),
            ]),
            new TweakGroup("모니터 감지(Adaptive-Sync)",
                "모니터가 VRR을 지원한다고 판단할 때 드라이버가 허용할 신호 방식을 정합니다.",
            [
                Flag("enableAdaptiveSync", 0x17, 0x03, "DisplayPort Adaptive-Sync", "DisplayPort Adaptive-Sync 기능을 통해 VRR을 감지합니다(비트 0과 1은 동일하게 동작합니다).",
                     TweakRisk.Caution, "끄면 DisplayPort 연결 VRR 모니터를 감지하지 못할 수 있습니다."),
                Flag("enableAdaptiveSync", 0x17, 0x10, "HDMI VRR", "HDMI 제조사별 데이터 블록을 통해 VRR을 감지합니다(HDMI VRR / HDMI FreeSync).",
                     TweakRisk.Caution),
                Flag("enableAdaptiveSync", 0x17, 0x04, "다중 디스플레이에서 VRR 사용", "같은 그룹에서 여러 디스플레이가 활성화된 경우에도 VRR 검증을 허용합니다.",
                     TweakRisk.Caution),
                Toggle("enableVrrDisplayValidation", "엄격한 VRR 디스플레이 검증", "디스플레이 그룹이 이미 VRR 집합에 포함되어 있지 않으면 VRR을 거부합니다.",
                    0, TweakRisk.Experimental),
            ]),
            new TweakGroup("주사율 제한 및 타이밍",
                "드라이버가 모니터 타이밍을 설정할 때 사용하는 값입니다. 잘못 설정하면 모니터가 신호를 잃을 수 있습니다. 화면이 계속 검게 표시되면 드라이버를 다시 시작하고 값을 되돌리십시오.",
            [
                Num("vrrMaxRRx1k", "VRR 최대 주사율 제한", "VRR에서 사용할 최대 주사율을 제한합니다. 드라이버가 수직 블랭킹을 늘려 패널이 이보다 빠르게 동작하지 않도록 합니다. 0은 제한 없음입니다.",
                    0, 0, 1_000_000, "Hz", TweakRisk.Caution, 1000, scale: 1000,
                    note: "값이 너무 낮으면 VRR 범위가 줄어들고 LFC가 정상 작동하지 않을 수 있습니다."),
                Num("vrrVblankExtension", "수직 블랭킹 확장", "가장 느린 VRR 프레임에 추가할 블랭킹 라인 수입니다. 모니터가 최저 주사율을 보고하지 않을 때만 사용됩니다.",
                    0x3FFF, 0, 65535, "라인", TweakRisk.Caution, 256),
                Pick("GsyncTimingOptimizationPolicy", "G-SYNC 타이밍 최적화", "모드 설정 시 드라이버가 모니터 테이블에서 사전 최적화된 타이밍을 선택할 수 있습니다.",
                     0, TweakRisk.Experimental, new Choice(0, "허용(기본값)"), new Choice(1, "비활성화")),
                Toggle("bIgnoreBlankingReqForDRR", "짧은 블랭킹 모드에서 VRR 사용", "수직 블랭킹이 약 25라인 + 280µs보다 짧은 모드에서도 VRR / 가상 주사율을 허용합니다.",
                    0, TweakRisk.Experimental),
                Num("vblCooldownTimeMs", "VBL 정책 재설정 대기 시간", "드라이버의 블랭킹 정책을 다시 설정하는 두 작업 사이의 최소 시간입니다.", 10000, 0, 600000, "ms", TweakRisk.Caution, 500),
            ]),
            new TweakGroup("리소스 관리자(Resource Manager, 실험적)",
                "VRR에 영향을 주는 저수준 디스플레이 RM 스위치입니다. 일반적으로 사용할 일이 드물며 효과도 일부만 파악되어 있습니다.",
            [
                Num("RmVrrMaxFrameDeltaOverride", "웨이크업 예측 시간 상한", "헤드별 VRR 웨이크업 예측 시간의 상한입니다. 0은 100ms(기본값)를 의미합니다.",
                    0, 0, 1000, "ms", TweakRisk.Experimental),
                Toggle("RmInternalVrrMclkOn2h1or", "메모리 클럭 유지(헤드 2개, 출력 1개)", "두 헤드가 하나의 출력을 공유할 때(매우 높은 픽셀 클럭 / 병합 모드) VRR 중 메모리 클럭을 고정합니다. 값이 0이면 비활성화됩니다.",
                    1, TweakRisk.Experimental, note: "값이 없으면 현재 GPU에서는 켜진 상태입니다. 끄면 해당 모드에서 깜박임이나 화면 꺼짐이 발생할 수 있습니다."),
            ]),
        ]),

        new TweakPage("cursor", "커서 및 유휴 상태", "게임이 프레임을 전달하지 않을 때(메뉴, 로딩 화면, 매우 낮은 FPS) VRR 디스플레이에서 발생하는 동작을 설정합니다.", "Pin",
        [
            new TweakGroup("VRR 유휴 상태에서 커서 갱신",
                "일반적으로 VRR은 다음 게임 프레임이 도착할 때까지 마우스 커서를 포함한 화면 전체를 멈춥니다. 드라이버가 커서만 갱신하는 프레임을 삽입하도록 설정할 수 있습니다.",
            [
                Toggle("enableVrrCursorRelease", "커서 갱신 허용", "게임이 멈춘 동안에도 마우스 커서가 움직이도록 합니다.", 1, TweakRisk.Caution,
                    note: "끄면 메뉴 / 로딩 화면에서 커서가 멈출 수 있습니다."),
                Num("minVrrCursorFPS", "최소 커서 갱신 빈도", "게임 프레임이 도착하지 않을 때 커서 전용 갱신을 허용하는 빈도입니다(0은 갱신 안 함).",
                    30, 0, 240, "Hz",
                    note: "추가 갱신이 발생할 때마다 OLED에서 작은 밝기 변화가 보일 수 있습니다."),
                Num("vrrCursorMarginUs", "커서 갱신 보호 시간", "다음 게임 프레임이 이 여유 시간보다 늦게 도착할 것으로 예상될 때만 커서 갱신을 삽입하여 게임 프레임과 겹치지 않도록 합니다.",
                    1500, 0, 20000, "µs", step: 100),
            ]),
        ]),

        new TweakPage("detect", "스마트 감지", "실험적 기능입니다. 드라이버가 불안정한 프레임 시간이나 슬라이드쇼 형태의 콘텐츠를 감지해 대응하도록 합니다. 기본값은 꺼짐이며 실험하려는 경우가 아니라면 그대로 두십시오.", "Repair",
        [
            new TweakGroup("감지",
                "감지가 작동하면 모든 프레임을 따라가는 대신 패널을 일정한 최대 프레임 주기로 전환합니다. 불규칙한 프레임 시간으로 인한 깜박임을 줄이기 위한 방식입니다.",
            [
                Pick("vrrSmartDetection", "스마트 감지 모드",
                     "활성화할 감지기를 선택합니다. 실제 효과가 나타나려면 '헤드 마스크' 부분이 필요합니다.",
                     0, TweakRisk.Experimental,
                     new Choice(0, "꺼짐(기본값)"),
                     new Choice(5, "슬라이드쇼 감지"),
                     new Choice(6, "엔트로피(지터) 감지"),
                     new Choice(7, "슬라이드쇼 + 엔트로피")),
            ]),
            new TweakGroup("슬라이드쇼 감지",
                "단일 프레임의 처리 시간이 매우 길어질 때 작동합니다(로딩 중 끊김, 일시 정지 화면 등).",
            [
                Num("vrrSlideShowModeThresholdMs", "슬라이드쇼 감지 임계값", "프레임 시간이 이 값보다 길면 슬라이드쇼로 간주합니다.", 1000, 20, 60000, "ms", TweakRisk.Experimental, 50,
                    note: "고정값을 사용하는 내장 패널 목록에서는 이 값이 무시됩니다."),
                Num("vrrSlideShowModePeriodMs", "슬라이드쇼 감지 유지 시간", "마지막으로 느린 프레임이 발생한 뒤 감지 모드를 유지할 시간입니다.", 2000, 0, 60000, "ms", TweakRisk.Experimental, 100),
            ]),
            new TweakGroup("엔트로피(지터) 감지",
                "실시간 '지터 점수'는 직전 프레임보다 긴 프레임이 발생할 때마다 증가하고 시간이 지나면서 감소합니다. 임계값을 넘으면 모드가 활성화됩니다.",
            [
                Num("vrrEntropyThreshold", "작동 임계값", "지터 모드가 시작되는 점수입니다(100분율 단위: 100은 프레임 시간이 2배가 된 상태).", 100, 1, 10000, "", TweakRisk.Experimental, 5),
                Num("vrrEntropyDecrease", "노이즈 하한", "매 프레임에서 차감되는 값(100분율 단위)입니다. 이보다 작은 증가는 누적되지 않습니다.", 30, 0, 1000, "", TweakRisk.Experimental, 5),
                Num("vrrEntropyDecay", "프레임당 감쇠율", "매 프레임 점수에 적용되는 1,000분율 배수입니다. 1000은 감쇠 없음, 900은 프레임마다 10% 감소입니다.", 1000, 1, 1000, "‰", TweakRisk.Experimental, 10,
                    note: "0으로 설정하면 감지기가 비활성화됩니다."),
                Num("vrrEntropyMax", "최대 점수", "점수의 상한(100분율 단위)입니다. 급격한 변동이 발생한 뒤 모드를 얼마나 오래 유지할지도 결정합니다.", 6000, 100, 100000, "", TweakRisk.Experimental, 100),
                Num("vrrEntropyDelayPeriodMs", "유지 시간", "점수가 마지막으로 작동 임계값을 초과한 뒤 모드를 유지할 시간입니다.", 500, 0, 60000, "ms", TweakRisk.Experimental, 50),
                Num("vrrHysteresisParam", "VRR 범위 이탈 히스테리시스", "'VRR 범위 밖' 상태가 해제되기 전에 추정값이 VRR 최저 프레임 주기보다 얼마나 더 낮아져야 하는지 정합니다.", 500000, 0, 5000000, "ns", TweakRisk.Experimental, 10000),
            ]),
        ]),
    ];
}
