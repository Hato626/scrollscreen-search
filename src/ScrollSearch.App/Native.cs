using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ScrollSearch.Core;

namespace ScrollSearch.App;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint h, nint after, int x, int y, int w, int hgt, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(nint h, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(nint h, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint h, int id);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint window);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    public static Box WindowBox(nint handle)
    {
        if (!GetWindowRect(handle, out var r)) throw new InvalidOperationException("无法读取目标窗口位置，请重新框选。 ");
        return new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
    public static nint Handle(Window window) => new WindowInteropHelper(window).Handle;
    public static void Place(Window window, Box box) => SetWindowPos(Handle(window), 0, (int)Math.Round(box.X), (int)Math.Round(box.Y),
        (int)Math.Round(box.Width), (int)Math.Round(box.Height), 0x14);
    public static bool Exclude(Window window) => SetWindowDisplayAffinity(Handle(window), 0x11);
    public static Box VirtualScreen => Box.Union(System.Windows.Forms.Screen.AllScreens.Select(s => new Box(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)));
    public static bool Intersects(Box a, Box b) => a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;
}

internal sealed class RegionTarget
{
    public Box Selection { get; }
    readonly nint window;
    readonly Box originalWindow;
    public bool FollowsWindow => window != 0;
    public RegionTarget(Box selection, bool follow)
    {
        Selection = selection;
        if (follow)
        {
            var point = new Native.POINT { X = (int)(selection.X + selection.Width / 2), Y = (int)(selection.Y + selection.Height / 2) };
            window = Native.GetAncestor(Native.WindowFromPoint(point), 2);
            if (window != 0) originalWindow = Native.WindowBox(window);
        }
    }
    public Box Resolve()
    {
        var current = Selection;
        if (window != 0)
        {
            if (!Native.IsWindow(window) || Native.IsIconic(window)) throw new InvalidOperationException("目标窗口已关闭或最小化。恢复窗口后继续，或重新框选。 ");
            var bounds = Native.WindowBox(window);
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException("目标窗口没有可见内容。 ");
            current = CoordinateMap.FollowWindow(Selection, originalWindow, bounds);
        }
        var desktop = Native.VirtualScreen;
        if (current.X < desktop.X || current.Y < desktop.Y || current.Right > desktop.Right || current.Bottom > desktop.Bottom)
            throw new InvalidOperationException("识别区域移出了桌面边界。请移回目标窗口或重新框选。 ");
        return new(Math.Round(current.X), Math.Round(current.Y), Math.Round(current.Width), Math.Round(current.Height));
    }
}
