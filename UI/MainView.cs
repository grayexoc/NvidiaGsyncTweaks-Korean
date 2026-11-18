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
                setBanner(new Banner(BannerKind.Error, "Could not write the restore point - nothing was changed", ex.Message));
                return;
            }

            setBusy(true);
            setBusyText("Waiting for administrator permission, then restarting the display driver - the screen will flicker");

            var request = new ElevatedRequest(gpu.KeyPath, changes, RestartDriver: true);
            var run = await Elevation.RunWorkerAsync(request.ToArguments());

            if (run.Cancelled)
            {
                setBusy(false);
                setBanner(new Banner(BannerKind.Info, "Cancelled", "Administrator permission was not granted - nothing was changed."));
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

            string n = $"{changes.Count} change(s)";
            string restorePoint = $" Restore point: {Path.GetFileName(backup)}";
            setBanner(run.Exit switch
            {
                _ when !run.Launched => new Banner(BannerKind.Error, "Could not start the elevated helper", run.Error ?? "unknown error"),
                ElevatedExit.Ok => new Banner(BannerKind.Success, $"{n} applied, display driver restarted",
                    "The driver has re-read its registry values." + restorePoint),
                ElevatedExit.OkRebootRequired => new Banner(BannerKind.Warning, "Applied - Windows asks for a reboot",
                    "The values are written and the driver restarted; a reboot is still needed for full effect." + restorePoint),
                ElevatedExit.RestartFailed => new Banner(BannerKind.Error, "Driver restart failed",
                    "The values were written, but the display adapter did not come back cleanly. Check Device Manager (display adapter) or reboot." + restorePoint),
                ElevatedExit.RegistryFailed => new Banner(BannerKind.Error, "Writing the registry failed", "Access was denied or the key is not writable."),
                ElevatedExit.NotNvidiaKey => new Banner(BannerKind.Error, "Refused", "The key does not belong to a present NVIDIA display adapter."),
                _ => new Banner(BannerKind.Error, "The elevated helper rejected the request", $"exit code {run.ExitCode}"),
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
                if (parsed is null) throw new InvalidOperationException("This is not a restore point written by this app.");
                if (!string.Equals(parsed.Value.Key, gpu.KeyPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("This restore point belongs to a different adapter key:\n" + parsed.Value.Key);
                await ApplyAndRestartAsync(parsed.Value.Values.ToDictionary(k => k.Key, k => k.Value), "before-restore", clearEdits: false);
            }
            catch (Exception ex)
            {
                setBanner(new Banner(BannerKind.Error, "Restore failed", ex.Message));
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

        var navItems = new List<NavigationViewItemData> { NavItem("LFC / frame doubling", "Sync", "lfc") };
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
            FooterMenuItems = [NavItem("Backups", "Save", "backups")],
        };
        nav = nav.Set(n => n.IsSettingsVisible = false);

        bool restoring = restoreFile is not null;
        var dialog = ContentDialog(
            restoring ? "Restore this backup and restart the display driver?" : "Apply changes and restart the display driver?",
            VStack(10,
                restoring
                    ? Body($"Restores the values saved in {Path.GetFileNameWithoutExtension(restoreFile!)}.").SemiBold()
                    : Body($"{edits.Count} pending change(s) are written, then the driver is stopped and started again so it re-reads its registry values - no reboot needed.").SemiBold(),
                Ui.MutedBody("•  All screens go black / flicker for a few seconds and windows may be rearranged."),
                Ui.MutedBody("•  Running games, video, GPU compute or capture apps can crash or lose their GPU - save your work and close them first."),
                Ui.MutedBody("•  HDR, resolution and G-SYNC are re-negotiated with every monitor afterwards."),
                Grid(
                    columns: [GridSize.Auto, GridSize.Star()],
                    rows: [GridSize.Auto],
                    ShieldIcon.Element().Grid(row: 0, column: 0).VAlign(VerticalAlignment.Top).Margin(0, 3, 8, 0),
                    Ui.MutedBody("Windows asks for administrator permission (UAC) - only for this step; the app itself runs without admin rights.").Grid(row: 0, column: 1))),
            primaryButtonText: restoring ? "Restore & restart driver" : "Apply & restart driver") with
        {
            IsOpen = askRestart,
            CloseButtonText = "Cancel",
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
            return Ui.Panel(HStack(12, ProgressRing().IsActive().Width(24).Height(24), Body("Looking for NVIDIA GPUs …")));

        if (gpu is null)
            return InfoBar("No NVIDIA GPU found",
                NvApi.LastError ?? "NVAPI did not report a GPU. Install the NVIDIA display driver and start the app again.").Error().IsClosable(false);

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
                        Ui.Muted($"PCI {gpu.Gpu.BusId:X2}:{gpu.Gpu.SlotId:X2}  ·  driver {gpu.NvApiDriverText}")))
                    .Grid(row: 0, column: 0),
                picker.Grid(row: 0, column: 1)),
            HStack(8,
                Ui.Muted("Driver key"),
                Ui.Mono(gpu.DisplayKeyPath)),
            HStack(8,
                verified
                    ? Ui.Pill($"Verified: NVAPI {gpu.NvApiDriverText} = registry {gpu.RegistryDriverVersion}", "success")
                    : Ui.Pill(gpu.KeyPath is null ? "Registry key not found" : "Driver version does not match the registry key", "caution"),
                gpu.NvApiAgreesWithPnp ? Ui.Pill("Key confirmed by NVAPI", "success") : null,
                gpu.NvApiKeyPath is null ? Ui.Pill($"Found via {gpu.MatchMethod}", "neutral") : null)));
    }

    private static Element ActionBar(Ctx c, bool hasPending, bool canAct, bool busy, string busyText,
        Action discard, Action applyRestart)
    {
        Element status = busy
            ? HStack(12, ProgressRing().IsActive().Width(20).Height(20), Body(busyText))
            : hasPending
                ? HStack(8, Ui.Pill($"{c.PendingCount} pending", "accent"), Ui.Muted("Applying restarts the display driver").VAlign(VerticalAlignment.Center))
                : Ui.Muted("No pending changes");

        return Border(
                Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    status.Grid(row: 0, column: 0).VAlign(VerticalAlignment.Center),
                    HStack(8,
                        Button("Discard", discard).IsEnabled(hasPending && !busy),
                        Ui.ElevatedButton("Apply & restart driver", applyRestart, enabled: hasPending && canAct, accent: true))
                        .Grid(row: 0, column: 1)))
            .Background(Theme.LayerFill)
            .WithBorder(Theme.SurfaceStroke, 1)
            .Padding(24, 12);
    }
}
