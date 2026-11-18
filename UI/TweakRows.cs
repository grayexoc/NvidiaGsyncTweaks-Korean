using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NvidiaGsyncTweaks.Model;
using static Microsoft.UI.Reactor.Factories;

namespace NvidiaGsyncTweaks.UI;

/// <summary>Generic editor row for a <see cref="TweakDef"/> (toggle, choice, number or one bit of a bitmask register).</summary>
internal static class TweakRows
{
    public static Element Build(Ctx c, TweakDef d)
    {
        uint eff = c.Effective(d.Name, d.기본값);
        bool isBit = d.Kind == TweakKind.Bit;

        bool pending, custom;
        if (isBit)
        {
            uint cur = c.EffectiveCurrent(d.Name, d.기본값);
            pending = (cur & d.BitMask) != (eff & d.BitMask);
            custom = (eff & d.BitMask) != (d.기본값 & d.BitMask);
        }
        else
        {
            pending = c.IsPending(d.Name);
            custom = c.Is사용자 지정(d.Name, d.기본값);
        }

        Element editor = d.Kind switch
        {
            TweakKind.Toggle => ToggleSwitch(d.OnValue == 1 ? eff != d.OffValue : eff == d.OnValue,
                                             v => c.Set(d, v ? d.OnValue : d.OffValue), onContent: "On", offContent: "Off")
                                    .AutomationName(d.Title),
            TweakKind.Bit => ToggleSwitch((eff & d.BitMask) != 0,
                                          v => c.Set(d, v ? eff | d.BitMask : eff & ~d.BitMask), onContent: "On", offContent: "Off")
                                 .AutomationName(d.Title),
            TweakKind.Choice => ChoiceEditor(c, d, eff),
            _ => NumberEditor(c, d, eff),
        };

        Action resetAction = isBit
            ? () => c.Set(d, (eff & ~d.BitMask) | (d.기본값 & d.BitMask))
            : () => c.Edit(d.Name, null);

        Element reset = custom || pending
            ? HyperlinkButton("초기화", onClick: resetAction).AutomationName($"초기화 {d.Title} to driver default")
            : Empty();

        var control = HStack(12, reset, editor);

        var badges = new Element?[]
        {
            pending ? Ui.Pill("변경됨", "accent") : custom ? Ui.Pill("사용자 지정", "neutral") : null,
            Ui.RiskPill(d.Risk),
        };

        var desc = d.Description + (d.Note is null ? "" : "  " + d.Note);
        return Ui.SettingCard(d.Title, desc + $"  ·  default: {기본값Text(d)}", control, badges);
    }

    private static string 기본값Text(TweakDef d) => d.Kind switch
    {
        TweakKind.Toggle => d.기본값 == d.OffValue ? "off" : "on",
        TweakKind.Bit => (d.기본값 & d.BitMask) != 0 ? "on" : "off",
        TweakKind.Choice => d.Choices?.FirstOr기본값(x => x.Value == d.기본값)?.Label ?? $"0x{d.기본값:X}",
        _ => $"{d.기본값 / d.Scale:0.###}{(d.Unit.Length > 0 ? " " + d.Unit : "")}",
    };

    private static Element ChoiceEditor(Ctx c, TweakDef d, uint eff)
    {
        var choices = d.Choices ?? [];
        var labels = choices.Select(x => x.Label).ToList();
        int idx = -1;
        for (int i = 0; i < choices.Count; i++) if (choices[i].Value == eff) idx = i;
        if (idx < 0) { labels.Add($"사용자 지정 (0x{eff:X})"); idx = labels.Count - 1; }
        return ComboBox(labels.ToArray(), idx, i => { if (i >= 0 && i < choices.Count) c.Set(d, choices[i].Value); })
            .AutomationName(d.Title)
            .MinWidth(220);
    }

    private static Element NumberEditor(Ctx c, TweakDef d, uint eff)
    {
        var box = NumberBox(eff / d.Scale, v =>
            {
                if (double.IsNaN(v)) return;
                c.Set(d, (uint)Math.Clamp(Math.Round(v * d.Scale), d.Min, d.Max));
            })
            .Range(d.Min / d.Scale, d.Max / d.Scale)
            .SpinButtons()
            .AutomationName(d.Title)
            .Width(150);
        return d.Unit.Length > 0 ? HStack(8, box, Ui.Muted(d.Unit).VAlign(VerticalAlignment.Center)) : box;
    }

    public static Element Group(Ctx c, TweakGroup g) =>
        VStack(8,
            Ui.SectionHeader(g.Title, g.Intro),
            VStack(4, g.Tweaks.Select(t => Build(c, t).WithKey(t.Name + "/" + t.BitMask)).ToArray()));
}
