using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NvidiaGsyncTweaks.Model;
using static Microsoft.UI.Reactor.Factories;

namespace NvidiaGsyncTweaks.UI;

/// <summary>Small Fluent 2 building blocks shared by all pages.</summary>
internal static class Ui
{
    public const double PageMaxWidth = 980;

    public static Element Muted(string text) =>
        Caption(text).Foreground(Theme.SecondaryText).TextWrapping(TextWrapping.Wrap);

    public static Element MutedBody(string text) =>
        Body(text).Foreground(Theme.SecondaryText).TextWrapping(TextWrapping.Wrap);

    public static Element Mono(string text) =>
        Caption(text).Foreground(Theme.SecondaryText).FontFamily("Cascadia Mono, Consolas").TextWrapping(TextWrapping.Wrap);

    public static Element SectionHeader(string title, string? intro = null) =>
        VStack(2,
            Subtitle(title),
            intro is null ? null : MutedBody(intro))
        .Margin(0, 8, 0, 0);

    public static Element PageHeader(string title, string subtitle) =>
        VStack(4,
            Title(title),
            MutedBody(subtitle));

    // ---- pills -------------------------------------------------------------------------------------------
    public static Element Pill(string text, string kind = "neutral")
    {
        var (bg, fg) = kind switch
        {
            "success" => (Theme.SystemSuccessBackground, Theme.SystemSuccess),
            "caution" => (Theme.SystemCautionBackground, Theme.SystemCaution),
            "critical" => (Theme.SystemCriticalBackground, Theme.SystemCritical),
            "accent" => (Theme.SystemAttentionBackground, Theme.AccentText),
            _ => (Theme.SystemNeutralBackground, Theme.SecondaryText),
        };
        return Border(Caption(text).Foreground(fg).SemiBold())
            .Background(bg).CornerRadius(10).Padding(8, 2).VAlign(VerticalAlignment.Center);
    }

    public static Element? RiskPill(TweakRisk risk) => risk switch
    {
        TweakRisk.Caution => Pill("주의해서 사용", "caution"),
        TweakRisk.실험적 => Pill("실험적", "critical"),
        _ => null,
    };

    // ---- cards -------------------------------------------------------------------------------------------
    /// <summary>Windows 11 settings-card: title + description on the left, the control on the right.</summary>
    public static Element SettingCard(string title, string? description, Element control, params Element?[] badges)
    {
        var titleRow = HStack(8, new Element?[] { BodyStrong(title) }.Concat(badges).ToArray());
        return Border(
                Grid(
                    columns: [GridSize.Star(), GridSize.Auto],
                    rows: [GridSize.Auto],
                    VStack(2,
                        titleRow,
                        description is null ? null : Muted(description))
                        .Grid(row: 0, column: 0).VAlign(VerticalAlignment.Center).Margin(0, 0, 16, 0),
                    control.Grid(row: 0, column: 1).VAlign(VerticalAlignment.Center)))
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke, 1)
            .CornerRadius(8)
            .Padding(16, 12);
    }

    /// <summary>Free-form card (header + arbitrary body).</summary>
    public static Element Panel(Element body, double padding = 16) =>
        Border(body)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke, 1)
            .CornerRadius(8)
            .Padding(padding);

    /// <summary>A button that triggers an elevated (UAC) action: label with the Windows shield in front.</summary>
    public static Element ElevatedButton(string label, Action onClick, bool enabled = true, bool accent = false)
    {
        var b = Button(label, onClick) with { ContentElement = HStack(8, ShieldIcon.Element(), TextBlock(label)) };
        var e = b.AutomationName(label + " (asks for administrator permission)").IsEnabled(enabled);
        return accent ? e.적용Style("AccentButtonStyle") : e;
    }
}
