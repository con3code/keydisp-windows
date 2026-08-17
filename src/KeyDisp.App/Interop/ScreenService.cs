using KeyDisp.Core.Screens;
using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Interop;

/// <summary>1 台のモニタ (座標は物理 px の仮想スクリーン座標)。</summary>
public sealed record ScreenInfo(string StableId, RectD Bounds, RectD WorkArea, bool IsPrimary);

/// <summary>
/// モニタの列挙と安定 ID の解決 (Mac 版 CGDisplayCreateUUIDFromDisplayID 相当)。
/// 安定 ID は QueryDisplayConfig の monitorDevicePath (EDID 由来。プロジェクタを
/// 抜き差ししても概ね変わらない)。取れない場合は GDI デバイス名 (\\.\DISPLAYn) に
/// フォールバックする。列挙は毎回行う (モニタ構成は随時変わるため)。
/// </summary>
public sealed class ScreenService
{
    public IReadOnlyList<ScreenInfo> All()
    {
        var stableIds = QueryStableIds(); // GDI デバイス名 → monitorDevicePath
        var result = new List<ScreenInfo>();
        MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEXW { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEXW>() };
            if (GetMonitorInfoW(hMonitor, ref info))
            {
                var id = stableIds.TryGetValue(info.szDevice, out var path) && path.Length > 0
                    ? path
                    : info.szDevice;
                result.Add(new ScreenInfo(
                    id,
                    ToRect(info.rcMonitor),
                    ToRect(info.rcWork),
                    (info.dwFlags & 1) != 0));
            }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return result;
    }

    public ScreenInfo Primary()
    {
        var all = All();
        return all.FirstOrDefault(s => s.IsPrimary) ?? all[0];
    }

    public ScreenInfo? FromPoint(double x, double y) =>
        All().FirstOrDefault(s => s.Bounds.Contains(x, y));

    /// <summary>フレーム中心を含む画面 (無ければ最も近い = プライマリ)。</summary>
    public ScreenInfo FromRectCenter(RectD frame) =>
        FromPoint(frame.MidX, frame.MidY) ?? Primary();

    public (double X, double Y) CursorPosition()
    {
        GetCursorPos(out var pt);
        return (pt.X, pt.Y);
    }

    private static RectD ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    /// <summary>GDI デバイス名 (\\.\DISPLAYn) → monitorDevicePath の対応表を作る。</summary>
    private static Dictionary<string, string> QueryStableIds()
    {
        var map = new Dictionary<string, string>();
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var numPaths, out var numModes) != 0)
            {
                return map;
            }
            var paths = new DISPLAYCONFIG_PATH_INFO[numPaths];
            var modes = new DISPLAYCONFIG_MODE_INFO[numModes];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths,
                    ref numModes, modes, IntPtr.Zero) != 0)
            {
                return map;
            }
            for (var i = 0; i < numPaths; i++)
            {
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = paths[i].sourceInfo.adapterId,
                        id = paths[i].sourceInfo.id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = paths[i].targetInfo.adapterId,
                        id = paths[i].targetInfo.id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref target) != 0) continue;

                map[source.viewGdiDeviceName] = target.monitorDevicePath;
            }
        }
        catch (Exception)
        {
            // 失敗時は空 (呼び出し側が GDI 名へフォールバック)
        }
        return map;
    }
}
