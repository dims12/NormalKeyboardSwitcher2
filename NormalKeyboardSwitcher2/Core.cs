using System;
using System.Collections.Generic;
using System.Linq;

namespace NormalKeyboardSwitcher2;

// The cycle snapshot never changes until the gesture ends.
public sealed class RecentLayouts
{
    private readonly List<nint> order = new();
    private nint[]? cycle;
    private int index;
    public IReadOnlyList<nint> Order => order;
    public bool Cycling => cycle != null;
    public void Refresh(IEnumerable<nint> installed)
    {
        var available = installed.Distinct().ToArray();
        order.RemoveAll(x => !available.Contains(x));
        foreach (var item in available)
            if (!order.Contains(item)) order.Add(item);
    }
    public void Observe(nint layout)
    {
        if (layout == 0 || Cycling) return;
        order.Remove(layout);
        order.Insert(0, layout);
    }
    public nint Next(nint current)
    {
        if (cycle == null)
        {
            Observe(current);
            cycle = order.ToArray();
            index = 0;
        }
        if (cycle.Length == 0) return 0;
        index = (index + 1) % cycle.Length;
        return cycle[index];
    }
    public void End(nint actual)
    {
        cycle = null;
        Observe(actual);
    }
}

public enum Gesture { CtrlShift = 0, AltShift = 1, System = 2 }
public enum GestureAction { None, Next, Commit, Cancel }

// Modifier-only gestures must not activate while typing Ctrl+Shift+letter shortcuts.
// Selection occurs on Shift release, so Ctrl+Shift+C remains a normal application shortcut.
public sealed class GestureState
{
    private readonly HashSet<int> down = new();
    private bool armed, cycling, contaminated;
    public Gesture Gesture { get; set; }
    private bool First => Gesture == Gesture.CtrlShift
        ? down.Contains(0xA2) || down.Contains(0xA3) : down.Contains(0xA4);
    private bool Shift => down.Contains(0xA0) || down.Contains(0xA1);
    private bool IsFirst(int key) => Gesture == Gesture.CtrlShift
        ? key is 0xA2 or 0xA3 : key == 0xA4;
    public GestureAction Feed(int key, bool pressed)
    {
        if (Gesture == Gesture.System) { Reset(); return GestureAction.None; }
        bool wasFirst = First, wasShift = Shift;
        if (pressed) { if (!down.Add(key)) return GestureAction.None; }
        else down.Remove(key);
        bool relevant = IsFirst(key) || key is 0xA0 or 0xA1;
        if (pressed && !relevant)
        {
            contaminated = true;
            armed = false;
            if (cycling) { cycling = false; return GestureAction.Cancel; }
        }
        if (pressed && relevant && First && Shift && !contaminated &&
            down.All(k => IsFirst(k) || k is 0xA0 or 0xA1))
            armed = true;
        GestureAction action = GestureAction.None;
        if (!pressed && relevant && armed && ((wasFirst && !First) || (wasShift && !Shift)))
        {
            armed = false;
            cycling = true;
            action = GestureAction.Next;
        }
        if (!First && !Shift)
        {
            contaminated = down.Count != 0;
            armed = false;
            if (cycling && action == GestureAction.None)
            {
                cycling = false;
                action = GestureAction.Commit;
            }
        }
        return action;
    }
    public bool Released => !First && !Shift;
    public void Reset() { down.Clear(); armed = cycling = contaminated = false; }
}

public readonly record struct PixelRect(double X, double Y, double Width, double Height);
public static class BadgePlacement
{
    public static (int X, int Y) Place(PixelRect caret, PixelRect work, int width, int height, int gap)
    {
        double x = caret.X + caret.Width + gap;
        double y = caret.Y + caret.Height - height / 3.0;
        if (y + height > work.Y + work.Height) y = work.Y + work.Height - height;
        return ((int)Math.Clamp(x, work.X, Math.Max(work.X, work.X + work.Width - width)),
                (int)Math.Clamp(y, work.Y, Math.Max(work.Y, work.Y + work.Height - height)));
    }
}


public static class ShortcutPolicy
{
    private static readonly string[] Names = { "Hotkey", "Language Hotkey", "Layout Hotkey" };
    public static Dictionary<string,string> ToDisable(IReadOnlyDictionary<string,string> current,Gesture gesture)
    {
        if (gesture == Gesture.System) return new();
        string code=gesture == Gesture.CtrlShift ? "2" : "1";
        return Names.Where(name=>current.TryGetValue(name,out var value) && value == code)
            .ToDictionary(name=>name,name=>current[name]);
    }
    public static Dictionary<string,string> ToRestore(IReadOnlyDictionary<string,string> current,IReadOnlyDictionary<string,string> backup)
    {
        if (backup.Any(pair=>!Names.Contains(pair.Key) || pair.Value is not ("1" or "2")))
            throw new ArgumentException("Invalid Windows shortcut recovery journal.");
        return backup.Where(pair=>current.TryGetValue(pair.Key,out var value) && value == "3")
            .ToDictionary(pair=>pair.Key,pair=>pair.Value);
    }
}


public static class CaretInkPolicy
{
    // At the first column, left-hand samples may belong to a row header or
    // gutter. Keep samples from one side instead of mixing two surfaces.
    public static uint[] Background(uint[] left,uint[] right) =>
        right.Length>=3 ? right : left.Length>=3 ? left : Array.Empty<uint>();

    private static double Luma(uint rgb) => .2126*(rgb&255)+.7152*((rgb>>8)&255)+.0722*((rgb>>16)&255);
    private static double Distance(uint a,uint b) => Math.Abs((int)(a&255)-(int)(b&255))+
        Math.Abs((int)((a>>8)&255)-(int)((b>>8)&255))+Math.Abs((int)((a>>16)&255)-(int)((b>>16)&255));
    public static bool HasStroke(uint[][] columns,uint[] surroundings)
    {
        if(surroundings.Length==0) return false;
        uint background=surroundings.OrderBy(Luma).ElementAt(surroundings.Length/2);
        return Stroke(columns,background).HasValue;
    }
    private static uint? Stroke(uint[][] columns,uint background)
    {
        uint? best=null; double bestDistance=0;
        foreach(var column in columns)
        {
            if(column.Length<3) continue;
            uint color=column.OrderBy(Luma).ElementAt(column.Length/2);
            double distance=Distance(color,background);
            // Samples span the full caret height. Text stems, subpixel fringes,
            // and spell-check underlines cover only part of that height.
            int required=Math.Max(3,(int)Math.Ceiling(column.Length*0.8));
            if(distance>120 && distance>bestDistance && column.Count(p=>Distance(p,color)<60)>=required)
            { best=color; bestDistance=distance; }
        }
        return best;
    }
    public static uint Choose(uint[][] columns,uint[] surroundings,uint? previous)
    {
        if(surroundings.Length==0) return previous??0u;
        uint background=surroundings.OrderBy(Luma).ElementAt(surroundings.Length/2);
        uint? best=Stroke(columns,background);
        double bestDistance=best.HasValue ? Distance(best.Value,background) : 0;
        // Anti-aliased/blinking carets can produce lighter samples at the same
        // position. Preserve the stronger ink while it contrasts with the surface.
        // A theme/background change invalidates this naturally through contrast.
        if(previous.HasValue && Distance(previous.Value,background)>180 &&
            (!best.HasValue || Distance(previous.Value,background)>=bestDistance))
            return previous.Value;
        if(best.HasValue) return best.Value;
        // Preserve a colored caret during its blink-off phase only if it still contrasts.
        if(previous.HasValue && Distance(previous.Value,background)>180) return previous.Value;
        return Luma(background)>=140 ? 0u : 0xFFFFFFu;
    }
}

public static class ConsoleGeometry
{
    public static PixelRect? Caret(int originX,int originY,int x,int y,int left,int top,int right,int bottom,int cellWidth,int cellHeight,int percent)
    {
        if(x<left || x>right || y<top || y>bottom || cellWidth<=0 || cellHeight<=0) return null;
        int height=Math.Max(1,cellHeight*Math.Clamp(percent,1,100)/100);
        return new(originX+(x-left)*cellWidth,originY+(y-top)*cellHeight+cellHeight-height,cellWidth,height);
    }
}

public sealed class CaretBlink
{
    private long epoch;
    public void Restart(long now) => epoch=now;
    public bool Visible(long now,uint systemInterval)
    {
        if(systemInterval==0 || systemInterval==uint.MaxValue) return true;
        long interval=Math.Max(1,systemInterval/2);
        long elapsed=now-epoch;
        if(elapsed<1000) return true;
        return (elapsed-1000)/interval%2==0;
    }
}


public static class CaretGeometry
{
    public static PixelRect ExpandPlaceholder(PixelRect reported,PixelRect owner)
    {
        // Some IA2 providers return a 1x1 virtual character for an empty editor,
        // while the focused accessible object is the full-height caret itself.
        // Never use an entire cell, paragraph, or unrelated control as a caret.
        if(reported.Height<=1 && reported.Width<=1 && owner.Width>0 && owner.Width<=4 &&
            owner.Height>1 && owner.Height<500 &&
            reported.X>=owner.X && reported.X<=owner.X+owner.Width &&
            reported.Y>=owner.Y && reported.Y<owner.Y+owner.Height)
            return new(reported.X,owner.Y,1,owner.Height);
        return reported;
    }
    public static bool Contains(PixelRect rect,int x,int y) =>
        x>=rect.X && x<rect.X+rect.Width && y>=rect.Y && y<rect.Y+rect.Height;
}
