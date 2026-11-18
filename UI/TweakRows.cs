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
        uint eff = c.Effective(d.Name, d.Default);
        bool isBit = d.Kind == TweakKind.Bit;

        bool pending, custom;
        if (isBit)
        {
            uint cur = c.EffectiveCurrent(d.Name, d.Default);
            pending = (cur & d.BitMask) != (eff & d.BitMask);
            custom = (eff & d.BitMask) != (d.Default & d.BitMask);
        }
        else
        {
            pending = c.IsPending(d.Name);
            custom = c.IsCustom(d.Name, d.Default);
        }

        Element editor = d.Kind switch
        {
            TweakKind.Toggle => ToggleSwitch(d.OnValue == 1 ? eff != d.OffValue : eff == d.OnValue,
                                             v => c.Set(d, v ? d.OnValue : d.OffValue), onContent: "켜짐", offContent: "꺼짐")
                                    .AutomationName(d.Title),
            TweakKind.Bit => ToggleSwitch((eff & d.BitMask) != 0,
                                          v => c.Set(d, v ? eff | d.BitMask : eff & ~d.BitMask), onContent: "켜짐", offContent: "꺼짐")
                                 .AutomationName(d.Title),
            TweakKind.Choice => ChoiceEditor(c, d, eff),
            _ => NumberEditor(c, d, eff),
        };

        Action resetAction = isBit
            ? () => c.Set(d, (eff & ~d.BitMask) | (d.Default & d.BitMask))
            : () => c.Edit(d.Name, null);

        Element reset = custom || pending
            ? HyperlinkButton("초기화", onClick: resetAction).AutomationName($"{d.Title}을(를) 드라이버 기본값으로 초기화")
            : Empty();

        var control = HStack(12, reset, editor);

        var badges = new Element?[]
        {
            pending ? Ui.Pill("변경됨", "accent") : custom ? Ui.Pill("사용자 지정", "neutral") : null,
            Ui.RiskPill(d.Risk),
        };

        var desc = d.Description + (d.Note is null ? "" : "  " + d.Note);
        return Ui.SettingCard(d.Title, desc + $"  ·  기본값: {DefaultText(d)}", control, badges);
    }

    private static string DefaultText(TweakDef d) => d.Kind switch
    {
        TweakKind.Toggle => d.Default == d.OffValue ? "꺼짐" : "켜짐",
        TweakKind.Bit => (d.Default & d.BitMask) != 0 ? "켜짐" : "꺼짐",
        TweakKind.Choice => d.Choices?.FirstOrDefault(x => x.Value == d.Default)?.Label ?? $"0x{d.Default:X}",
        _ => $"{d.Default / d.Scale:0.###}{(d.Unit.Length > 0 ? " " + d.Unit : "")}",
    };

    private static Element ChoiceEditor(Ctx c, TweakDef d, uint eff)
    {
        var choices = d.Choices ?? [];
        var labels = choices.Select(x => x.Label).ToList();
        int idx = -1;
        for (int i = 0; i < choices.Count; i++) if (choices[i].Value == eff) idx = i;
        if (idx < 0) { labels.Add($"사용자 지정(0x{eff:X})"); idx = labels.Count - 1; }
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
