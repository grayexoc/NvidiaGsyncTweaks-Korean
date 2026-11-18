using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NvidiaGsyncTweaks.Model;
using NvidiaGsyncTweaks.Native;
using NvidiaGsyncTweaks.Services;
using static Microsoft.UI.Reactor.Factories;

namespace NvidiaGsyncTweaks.UI;

internal enum BannerKind { Info, Success, Warning, Error }
internal sealed record Banner(BannerKind Kind, string Title, string Message);

/// <summary>
/// Application shell: title bar, navigation, GPU header, pages and the apply bar.
/// The app runs as a standard user. The one action that needs administrator rights - writing the registry values and restarting
/// the display driver (a change only takes effect after the restart, so both always happen together) - is executed by an elevated
/// worker process (one UAC prompt).
/// </summary>
internal sealed class MainView : Component
{
    private static readonly IReadOnlyDictionary<string, uint?> NoValues = new Dictionary<string, uint?>();
    private static readonly IReadOnlyList<ResolvedGpu> NoGpus = [];

    public override Element Render()
    {
        var (page, setPage) = UseState("lfc");
        var (gpus, setGpus) = UseState(NoGpus);
        var (gpuIdx, setGpuIdx) = UseState(0);
        var (current, setCurrent) = UseState(NoValues);
        var (edits, setEdits) = UseState(NoValues);
        var (busy, setBusy) = UseState(false);
        var (busyText, setBusyText) = UseState("");
        var (banner, setBanner) = UseState<Banner?>(null);
        var (askRestart, setAskRestart) = UseState(false);
        var (restoreFile, setRestoreFile) = UseState<string?>(null);   // non-null: the dialog is for restoring this backup
        var (maxHz, setMaxHz) = UseState(240.0);
        var (vrrMin, setVrrMin) = UseState(48.0);
        var (loaded, setLoaded) = UseState(false);

        ResolvedGpu? gpu = gpuIdx < gpus.Count ? gpus[gpuIdx] : null;

        // ---------------------------------------------------------------------------------------- loading
        void LoadValues(ResolvedGpu? g, bool clearEdits = true)
        {
            if (clearEdits) setEdits(NoValues);
            setCurrent(g?.KeyPath is null ? NoValues : new RegistryStore(g.KeyPath).Read(Catalog.AllNames));
        }

        void LoadGpus(int keepIndex)
        {
            var list = AdapterResolver.Resolve();
            setGpus(list);
            int idx = Math.Clamp(keepIndex, 0, Math.Max(0, list.Count - 1));
            setGpuIdx(idx);
            LoadValues(idx < list.Count ? list[idx] : null);
            setLoaded(true);
        }

        UseEffect(() =>
        {
            LoadGpus(0);
            if (DisplayModes.Primary() is { } m)
            {
                setMaxHz(m.MaxHz);
                if (m.MaxHz >= 200) setVrrMin(48);
            }
        }, 0);

        // ---------------------------------------------------------------------------------------- editing
        void Edit(string name, uint? value)
        {
            var next = new Dictionary<string, uint?>(edits);
            current.TryGetValue(name, out var stored);
            if (value == stored) next.Remove(name); else next[name] = value;
            setEdits(next);
        }

        // ---------------------------------------------------------------------------------------- the elevated action
        /// <summary>Writes <paramref name="changes"/> and restarts the display driver in the elevated worker (UAC prompt included).</summary>
        async Task ApplyAndRestartAsync(Dictionary<string, uint?> changes, string backupName, bool clearEdits)
        {
            if (gpu?.KeyPath is null || changes.Count == 0) return;

            string backup;
            try { backup = new RegistryStore(gpu.KeyPath).WriteRestoreFile(Catalog.AllNames, backupName); }
            catch (Exception ex)
            {
                setBanner(new Banner(BannerKind.Error, "복원 지점을 만들 수 없습니다. 변경 사항은 적용되지 않았습니다.", ex.Message));
                return;
            }

            setBusy(true);
            setBusyText("관리자 권한을 기다린 후 디스플레이 드라이버를 다시 시작합니다. 화면이 잠시 깜박입니다.");

            var request = new ElevatedRequest(gpu.KeyPath, changes, RestartDriver: true);
            var run = await Elevation.RunWorkerAsync(request.ToArguments());

            if (run.Cancelled)
            {
                setBusy(false);
                setBanner(new Banner(BannerKind.Info, "취소됨", "관리자 권한이 허용되지 않았습니다. 변경 사항은 적용되지 않았습니다."));
                return;
            }

            if (run.Launched)
            {
                // the GPU was reset: NVAPI handles are stale
                await Task.Run(() => { NvApi.Shutdown(); Thread.Sleep(1200); });
                LoadGpus(gpuIdx);
                bool ok = run.Exit is ElevatedExit.Ok or ElevatedExit.OkRebootRequired;
                if (!ok || !clearEdits) setEdits(edits);   // keep the pending edits (failure: so the user can retry; restore: they are unrelated)
            }
            setBusy(false);

            string n = $"{changes.Count}개 항목";
            string restorePoint = $" 복원 지점: {Path.GetFileName(backup)}";
            setBanner(run.Exit switch
            {
                _ when !run.Launched => new Banner(BannerKind.Error, "관리자 권한 작업을 시작할 수 없습니다", run.Error ?? "알 수 없는 오류"),
                ElevatedExit.Ok => new Banner(BannerKind.Success, $"{n}개 항목 적용 완료, 디스플레이 드라이버 다시 시작됨",
                    "드라이버가 레지스트리 값을 다시 읽었습니다." + restorePoint),
                ElevatedExit.OkRebootRequired => new Banner(BannerKind.Warning, "적용 완료 - Windows 재부팅 필요",
                    "값을 기록하고 드라이버를 다시 시작했습니다. 변경 사항을 완전히 적용하려면 Windows를 재부팅해야 합니다." + restorePoint),
                ElevatedExit.RestartFailed => new Banner(BannerKind.Error, "드라이버 다시 시작 실패",
                    "값은 기록했지만 디스플레이 어댑터가 정상적으로 복구되지 않았습니다. 장치 관리자에서 디스플레이 어댑터를 확인하거나 재부팅하십시오." + restorePoint),
                ElevatedExit.RegistryFailed => new Banner(BannerKind.Error, "레지스트리 쓰기 실패", "액세스가 거부되었거나 해당 레지스트리 키에 쓸 수 없습니다."),
                ElevatedExit.NotNvidiaKey => new Banner(BannerKind.Error, "요청 거부됨", "해당 키는 현재 연결된 NVIDIA 디스플레이 어댑터의 키가 아닙니다."),
                _ => new Banner(BannerKind.Error, "관리자 권한 작업 프로세스가 요청을 거부했습니다", $"종료 코드 {run.ExitCode}"),
            });
        }

        Task ApplyPendingAsync() =>
            ApplyAndRestartAsync(edits.ToDictionary(k => k.Key, k => k.Value), "before-apply", clearEdits: true);

        void RequestRestore(string file)
        {
            setRestoreFile(file);
            setAskRestart(true);
        }

        async Task RestoreAsync(string file)
        {
            if (gpu?.KeyPath is null) return;
            try
            {
                var parsed = RegistryStore.ParseRestoreFile(file);
                if (parsed is null) throw new InvalidOperationException("이 앱에서 만든 복원 지점이 아닙니다.");
                if (!string.Equals(parsed.Value.Key, gpu.KeyPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("이 복원 지점은 다른 어댑터 키에 속합니다:\n" + parsed.Value.Key);
                await ApplyAndRestartAsync(parsed.Value.Values.ToDictionary(k => k.Key, k => k.Value), "before-restore", clearEdits: false);
            }
            catch (Exception ex)
            {
                setBanner(new Banner(BannerKind.Error, "복원 실패", ex.Message));
            }
        }

        var ctx = new Ctx
        {
            Gpu = gpu,
            Current = current,
            Edits = edits,
            Busy = busy,
            MaxHz = maxHz,
            VrrMinHz = vrrMin,
            SetMaxHz = setMaxHz,
            SetVrrMinHz = setVrrMin,
            Edit = Edit,
        };

        // ---------------------------------------------------------------------------------------- page
        Element body = page switch
        {
            "lfc" => LfcPage.Build(ctx),
            "backups" => BackupsPage.Build(ctx, RequestRestore),
            _ => Catalog.Pages.FirstOrDefault(p => p.Tag == page) is { } tp ? TweakPageView.Build(ctx, tp) : LfcPage.Build(ctx),
        };

        var navItems = new List<NavigationViewItemData> { NavItem("LFC / 프레임 더블링", "Sync", "lfc") };
        foreach (var p in Catalog.Pages) navItems.Add(NavItem(p.Title, p.Icon, p.Tag));

        bool canAct = gpu?.KeyPath is not null && gpu.Adapter is not null && !busy;
        bool hasPending = edits.Count > 0;

        var content = FlexColumn(
            ScrollView(
                VStack(16,
                    GpuHeader(gpu, gpus, gpuIdx, i => { setGpuIdx(i); LoadValues(gpus[i]); }, loaded),
                    banner is null ? null :
                        InfoBar(banner.Title, banner.Message)
                            .Severity(banner.Kind switch
                            {
                                BannerKind.Success => InfoBarSeverity.Success,
                                BannerKind.Warning => InfoBarSeverity.Warning,
                                BannerKind.Error => InfoBarSeverity.Error,
                                _ => InfoBarSeverity.Informational,
                            })
                            .IsClosable(true).Closed(() => setBanner(null)),
                    body)
                .Padding(32, 8, 32, 24).MaxWidth(Ui.PageMaxWidth + 64).HAlign(HorizontalAlignment.Left))
                .Flex(grow: 1, basis: 0),
            ActionBar(ctx, hasPending, canAct, busy, busyText,
                discard: () => setEdits(NoValues),
                applyRestart: () => { setRestoreFile(null); setAskRestart(true); })
                .Flex(shrink: 0));

        var nav = NavigationView(navItems.ToArray(), content) with
        {
            SelectedTag = page,
            OnSelectedTagChanged = t => setPage(t ?? "lfc"),
            IsPaneToggleButtonVisible = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            OpenPaneLength = 260,
            FooterMenuItems = [NavItem("백업", "저장", "backups")],
        };
        nav = nav.Set(n => n.IsSettingsVisible = false);

        bool restoring = restoreFile is not null;
        var dialog = ContentDialog(
            restoring ? "이 백업을 복원하고 디스플레이 드라이버를 다시 시작하시겠습니까?" : "변경 사항을 적용하고 디스플레이 드라이버를 다시 시작하시겠습니까?",
            VStack(10,
                restoring
                    ? Body($"{Path.GetFileNameWithoutExtension(restoreFile!)}에 저장된 값을 복원합니다.").SemiBold()
                    : Body($"대기 중인 변경 사항 {edits.Count}개를 기록한 뒤 드라이버를 중지하고 다시 시작하여 레지스트리 값을 다시 읽습니다. 재부팅은 필요하지 않습니다.").SemiBold(),
                Ui.MutedBody("• 모든 화면이 몇 초간 꺼지거나 깜박이며 창 배치가 바뀔 수 있습니다."),
                Ui.MutedBody("• 실행 중인 게임, 동영상, GPU 연산 또는 캡처 프로그램이 충돌하거나 GPU 연결을 잃을 수 있습니다. 작업을 저장하고 먼저 종료하십시오."),
                Ui.MutedBody("• 이후 각 모니터와 HDR, 해상도 및 G-SYNC 연결을 다시 협상합니다."),
                Grid(
                    columns: [GridSize.Auto, GridSize.Star()],
                    rows: [GridSize.Auto],
                    ShieldIcon.Element().Grid(row: 0, column: 0).VAlign(VerticalAlignment.Top).Margin(0, 3, 8, 0),
                    Ui.MutedBody("Windows에서 이 단계에 한해 관리자 권한(UAC)을 요청합니다. 앱 자체는 관리자 권한 없이 실행됩니다.").Grid(row: 0, column: 1))),
            primaryButtonText: restoring ? "복원 후 드라이버 다시 시작" : "적용 후 드라이버 다시 시작") with
        {
            IsOpen = askRestart,
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
            OnClosed = r =>
            {
                setAskRestart(false);
                if (r == ContentDialogResult.Primary)
                {
                    if (restoreFile is { } f) _ = RestoreAsync(f);
                    else _ = ApplyPendingAsync();
                }
            },
        };

        return FlexColumn(
                TitleBar("NVIDIA G-SYNC Tweaks") with { Subtitle = AppInfo.Display },
                nav.Flex(grow: 1, basis: 0),
                dialog)
            .Backdrop(BackdropKind.Mica)
            .RequestedTheme(ElementTheme.Dark);
    }

    // ================================================================================================ pieces
    private static Element GpuHeader(ResolvedGpu? gpu, IReadOnlyList<ResolvedGpu> gpus, int idx, Action<int> select, bool loaded)
    {
        if (!loaded)
            return Ui.Panel(HStack(12, ProgressRing().IsActive().Width(24).Height(24), Body("NVIDIA GPU 검색 중…")));

        if (gpu is null)
            return InfoBar("NVIDIA GPU를 찾을 수 없음",
                NvApi.LastError ?? "NVAPI에서 GPU를 확인하지 못했습니다. NVIDIA 디스플레이 드라이버를 설치한 후 앱을 다시 실행하십시오.").Error().IsClosable(false);

        Element picker = gpus.Count > 1
            ? ComboBox(gpus.Select(g => g.Title).ToArray(), idx, select).MinWidth(320)
            : Empty();

        var verified = gpu.KeyExists && gpu.VersionMatches;
        return Ui.Panel(VStack(8,
            Grid(
                columns: [GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                HStack(12,
                    TextBlock("\uE7F4").FontFamily("Segoe Fluent Icons").FontSize(28).Foreground(Theme.Accent).VAlign(VerticalAlignment.Center),
                    VStack(0,
                        Subtitle(gpu.Gpu.Name),
                        Ui.Muted($"PCI {gpu.Gpu.BusId:X2}:{gpu.Gpu.SlotId:X2}  ·  드라이버 {gpu.NvApiDriverText}")))
                    .Grid(row: 0, column: 0),
                picker.Grid(row: 0, column: 1)),
            HStack(8,
                Ui.Muted("드라이버 레지스트리 키"),
                Ui.Mono(gpu.DisplayKeyPath)),
            HStack(8,
                verified
                    ? Ui.Pill($"확인됨: NVAPI {gpu.NvApiDriverText} = 레지스트리 {gpu.RegistryDriverVersion}", "success")
                    : Ui.Pill(gpu.KeyPath is null ? "레지스트리 키를 찾을 수 없음" : "드라이버 버전과 레지스트리 키의 버전이 일치하지 않음", "caution"),
                gpu.NvApiAgreesWithPnp ? Ui.Pill("NVAPI로 키 확인됨", "success") : null,
                gpu.NvApiKeyPath is null ? Ui.Pill($"검색 방법: {gpu.MatchMethod}", "neutral") : null)));
    }

    private static Element ActionBar(Ctx c, bool hasPending, bool canAct, bool busy, string busyText,
        Action discard, Action applyRestart)
    {
        Element status = busy
            ? HStack(12, ProgressRing().IsActive().Width(20).Height(20), Body(busyText))
            : hasPending
                ? HStack(8, Ui.Pill($"대기 중 {c.PendingCount}개", "accent"), Ui.Muted("적용하면 디스플레이 드라이버가 다시 시작됩니다").VAlign(VerticalAlignment.Center))
                : Ui.Muted("대기 중인 변경 사항 없음");

        return Border(
                Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    status.Grid(row: 0, column: 0).VAlign(VerticalAlignment.Center),
                    HStack(8,
                        Button("변경 취소", discard).IsEnabled(hasPending && !busy),
                        Ui.ElevatedButton("적용 후 드라이버 다시 시작", applyRestart, enabled: hasPending && canAct, accent: true))
                        .Grid(row: 0, column: 1)))
            .Background(Theme.LayerFill)
            .WithBorder(Theme.SurfaceStroke, 1)
            .Padding(24, 12);
    }
}
