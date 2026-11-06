using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowsQuickScaler
{
    /// <summary>
    /// Live scaling information for one active display, as reported by Windows.
    /// </summary>
    public sealed class DisplayScaleInfo
    {
        public string DeviceName { get; set; } = "";
        public string? FriendlyName { get; set; }
        public int? CurrentScalePercentage { get; set; }
        public int? MinScalePercentage { get; set; }
        public int? MaxScalePercentage { get; set; }
        public int? NativeWidth { get; set; }
        public int? NativeHeight { get; set; }
    }

    /// <summary>
    /// Reads and updates the DPI scale for a connected Windows display.
    /// </summary>
    public static class ScaleManager
    {
        private const uint QueryDisplayConfigOnlyActivePaths = 0x00000002;
        private const int GetSourceDeviceName = 1;
        private const int GetTargetDeviceName = 3;
        private const int GetDpiScale = -3;
        private const int SetDpiScale = -4;
        private const int ErrorSuccess = 0;
        private const int ErrorInsufficientBuffer = 122;
        private const uint DisplayConfigModeInfoTypeSource = 1;

        /// <summary>Every scale percentage Windows can express via the DPI API.</summary>
        public static readonly int[] SupportedScalePercentages =
            [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

        public static bool IsSupportedScale(int scalePercentage) =>
            Array.IndexOf(SupportedScalePercentages, scalePercentage) >= 0;

        /// <summary>Index into <see cref="SupportedScalePercentages"/>, or -1 when unsupported.</summary>
        public static int ScaleIndex(int scalePercentage) =>
            Array.IndexOf(SupportedScalePercentages, scalePercentage);

        /// <summary>Percentage for an index into <see cref="SupportedScalePercentages"/>, or null out of range.</summary>
        public static int? ScalePercentageAtIndex(int index) =>
            index >= 0 && index < SupportedScalePercentages.Length
                ? SupportedScalePercentages[index]
                : null;

        /// <summary>Next supported scale above <paramref name="scalePercentage"/>, or null at the top.</summary>
        public static int? NextScale(int scalePercentage)
        {
            int index = ScaleIndex(scalePercentage);
            return index >= 0 ? ScalePercentageAtIndex(index + 1) : null;
        }

        /// <summary>Next supported scale below <paramref name="scalePercentage"/>, or null at the bottom.</summary>
        public static int? PreviousScale(int scalePercentage)
        {
            int index = ScaleIndex(scalePercentage);
            return index > 0 ? ScalePercentageAtIndex(index - 1) : null;
        }

        /// <summary>
        /// Sets the scaling percentage for the active display identified by its GDI device name.
        /// </summary>
        public static void SetMonitorScaling(string deviceName, int scalePercentage)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);

            int scaleIndex = ScaleIndex(scalePercentage);
            if (scaleIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scalePercentage),
                    scalePercentage,
                    $"Scale must be one of {string.Join(", ", SupportedScalePercentages)} percent.");
            }

            DisplayConfigPathInfo? targetPath = FindActiveDisplayPath(deviceName);
            if (targetPath is not DisplayConfigPathInfo path)
            {
                throw new InvalidOperationException($"Could not find an active display named '{deviceName}'.");
            }

            DisplayConfigGetDpiScale scaleInfo = QueryDpiScale(path, deviceName);

            int maxScaleIndex = Math.Abs(scaleInfo.MinScaleRelative) + scaleInfo.MaxScaleRelative;
            if (scaleInfo.MinScaleRelative > 0 ||
                scaleInfo.MaxScaleRelative < 0 ||
                maxScaleIndex < 0)
            {
                throw new InvalidOperationException($"Windows returned an invalid scaling range for '{deviceName}'.");
            }

            if (scaleIndex > maxScaleIndex)
            {
                throw new InvalidOperationException(
                    $"{scalePercentage}% scaling is not available for '{deviceName}'. " +
                    $"This display supports {SupportedScalePercentages[0]}% through " +
                    $"{SupportedScalePercentages[Math.Min(maxScaleIndex, SupportedScalePercentages.Length - 1)]}%.");
            }

            DisplayConfigSetDpiScale setScale = new()
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = SetDpiScale,
                    Size = (uint)Marshal.SizeOf<DisplayConfigSetDpiScale>(),
                    AdapterId = path.SourceInfo.AdapterId,
                    Id = path.SourceInfo.Id
                },
                ScaleRelative = scaleIndex + scaleInfo.MinScaleRelative
            };

            int result = DisplayConfigSetDeviceInfo(ref setScale);
            if (result != ErrorSuccess)
                throw new Win32Exception(result, $"Could not set scaling for '{deviceName}'.");
        }

        /// <summary>Reads the current scale for a display. Returns false (never throws) on any failure.</summary>
        public static bool TryGetCurrentScale(string deviceName, out int scalePercentage)
        {
            scalePercentage = 0;
            try
            {
                if (string.IsNullOrWhiteSpace(deviceName))
                    return false;

                DisplayConfigPathInfo? targetPath = FindActiveDisplayPath(deviceName);
                if (targetPath is not DisplayConfigPathInfo path)
                    return false;

                DisplayConfigGetDpiScale scaleInfo = QueryDpiScale(path, deviceName);
                int index = scaleInfo.CurrentScaleRelative - scaleInfo.MinScaleRelative;
                int? percentage = ScalePercentageAtIndex(index);
                if (percentage is not int value)
                    return false;

                scalePercentage = value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Reads current scale, range, friendly name and native size for one display. Never throws.</summary>
        public static bool TryGetDisplayDetails(string deviceName, out DisplayScaleInfo info)
        {
            info = new DisplayScaleInfo { DeviceName = deviceName ?? "" };
            try
            {
                if (string.IsNullOrWhiteSpace(deviceName))
                    return false;

                IReadOnlyDictionary<string, DisplayScaleInfo> all = GetActiveDisplays()
                    .ToDictionary(d => d.DeviceName, StringComparer.OrdinalIgnoreCase);
                if (!all.TryGetValue(deviceName, out DisplayScaleInfo? found))
                    return false;

                info = found;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Enumerates every active display with its current scale, range and names. Never throws.</summary>
        public static List<DisplayScaleInfo> GetActiveDisplays()
        {
            var results = new List<DisplayScaleInfo>();
            try
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int result = GetDisplayConfigBufferSizes(
                        QueryDisplayConfigOnlyActivePaths,
                        out uint pathCount,
                        out uint modeCount);
                    if (result != ErrorSuccess)
                        return results;

                    var paths = new DisplayConfigPathInfo[pathCount];
                    var modes = new DisplayConfigModeInfo[modeCount];

                    result = QueryDisplayConfig(
                        QueryDisplayConfigOnlyActivePaths,
                        ref pathCount,
                        paths,
                        ref modeCount,
                        modes,
                        IntPtr.Zero);

                    if (result == ErrorInsufficientBuffer)
                        continue;
                    if (result != ErrorSuccess)
                        return results;

                    foreach (DisplayConfigPathInfo path in paths.Take((int)pathCount))
                    {
                        try
                        {
                            string? deviceName = GetSourceDeviceNameString(path);
                            if (string.IsNullOrWhiteSpace(deviceName))
                                continue;

                            // Avoid duplicate entries if a display appears twice.
                            if (results.Any(d => string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            var info = new DisplayScaleInfo { DeviceName = deviceName };
                            info.FriendlyName = GetFriendlyName(path);

                            try
                            {
                                DisplayConfigGetDpiScale scaleInfo = QueryDpiScale(path, deviceName);
                                int minIndex = 0;
                                int currentIndex = scaleInfo.CurrentScaleRelative - scaleInfo.MinScaleRelative;
                                int maxIndex = Math.Abs(scaleInfo.MinScaleRelative) + scaleInfo.MaxScaleRelative;
                                info.CurrentScalePercentage = ScalePercentageAtIndex(currentIndex);
                                info.MinScalePercentage = ScalePercentageAtIndex(minIndex);
                                info.MaxScalePercentage = ScalePercentageAtIndex(Math.Min(maxIndex, SupportedScalePercentages.Length - 1));
                            }
                            catch
                            {
                                // Scale details are best-effort per display.
                            }

                            try
                            {
                                uint modeIndex = path.SourceInfo.ModeInfoIndex;
                                if (modeIndex < modes.Length && modes[(int)modeIndex].InfoType == DisplayConfigModeInfoTypeSource)
                                {
                                    ulong data0 = modes[(int)modeIndex].ModeData0;
                                    uint width = (uint)(data0 & 0xFFFFFFFFUL);
                                    uint height = (uint)(data0 >> 32);
                                    if (width > 0 && height > 0 && width <= 16384 && height <= 16384)
                                    {
                                        info.NativeWidth = (int)width;
                                        info.NativeHeight = (int)height;
                                    }
                                }
                            }
                            catch
                            {
                                // Native size is best-effort.
                            }

                            results.Add(info);
                        }
                        catch
                        {
                            // Skip a single broken path rather than failing all displays.
                        }
                    }

                    return results;
                }
            }
            catch
            {
                // Enumeration is best-effort; callers fall back to Screen info.
            }

            return results;
        }

        private static DisplayConfigGetDpiScale QueryDpiScale(DisplayConfigPathInfo path, string deviceName)
        {
            DisplayConfigGetDpiScale scaleInfo = new()
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = GetDpiScale,
                    Size = (uint)Marshal.SizeOf<DisplayConfigGetDpiScale>(),
                    AdapterId = path.SourceInfo.AdapterId,
                    Id = path.SourceInfo.Id
                }
            };

            int result = DisplayConfigGetDeviceInfo(ref scaleInfo);
            if (result != ErrorSuccess)
                throw new Win32Exception(result, $"Could not read the scaling range for '{deviceName}'.");
            return scaleInfo;
        }

        private static string? GetSourceDeviceNameString(DisplayConfigPathInfo path)
        {
            var sourceName = new DisplayConfigSourceDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = GetSourceDeviceName,
                    Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                    AdapterId = path.SourceInfo.AdapterId,
                    Id = path.SourceInfo.Id
                }
            };

            int result = DisplayConfigGetDeviceInfo(ref sourceName);
            if (result != ErrorSuccess)
                return null;
            return string.IsNullOrWhiteSpace(sourceName.ViewGdiDeviceName) ? null : sourceName.ViewGdiDeviceName;
        }

        private static string? GetFriendlyName(DisplayConfigPathInfo path)
        {
            try
            {
                var targetName = new DisplayConfigTargetDeviceName
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = GetTargetDeviceName,
                        Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                        AdapterId = path.TargetInfo.AdapterId,
                        Id = path.TargetInfo.Id
                    }
                };

                int result = DisplayConfigGetDeviceInfo(ref targetName);
                if (result != ErrorSuccess)
                    return null;

                string? candidate = !string.IsNullOrWhiteSpace(targetName.FriendlyDeviceName)
                    ? targetName.FriendlyDeviceName
                    : targetName.EdidFriendlyName;

                return IsPlausibleDisplayName(candidate) ? candidate!.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPlausibleDisplayName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
                return false;
            // Reject control characters / replacement chars from a mis-sized interop struct.
            return value.All(c => !char.IsControl(c) && c != '\uFFFD');
        }

        private static DisplayConfigPathInfo? FindActiveDisplayPath(string deviceName)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int result = GetDisplayConfigBufferSizes(
                    QueryDisplayConfigOnlyActivePaths,
                    out uint pathCount,
                    out uint modeCount);
                if (result != ErrorSuccess)
                    throw new Win32Exception(result, "Could not enumerate active displays.");

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];

                result = QueryDisplayConfig(
                    QueryDisplayConfigOnlyActivePaths,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero);

                if (result == ErrorInsufficientBuffer)
                    continue;
                if (result != ErrorSuccess)
                    throw new Win32Exception(result, "Could not enumerate active displays.");

                foreach (DisplayConfigPathInfo path in paths.Take((int)pathCount))
                {
                    string? viewName = GetSourceDeviceNameString(path);
                    if (viewName is null)
                        continue;

                    if (string.Equals(viewName, deviceName, StringComparison.OrdinalIgnoreCase))
                        return path;
                }

                return null;
            }

            throw new InvalidOperationException("The display configuration changed repeatedly during enumeration.");
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(
            uint flags,
            out uint numPathArrayElements,
            out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [Out] DisplayConfigPathInfo[] pathArray,
            ref uint numModeInfoArrayElements,
            [Out] DisplayConfigModeInfo[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetDpiScale requestPacket);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")]
        private static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigSetDpiScale requestPacket);

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigLuid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigDeviceInfoHeader
        {
            public int Type;
            public uint Size;
            public DisplayConfigLuid AdapterId;
            public uint Id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayConfigSourceDeviceName
        {
            public DisplayConfigDeviceInfoHeader Header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string ViewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayConfigTargetDeviceName
        {
            public DisplayConfigDeviceInfoHeader Header;
            public uint EdidManufactureId;
            public uint EdidProductCodeId;
            public uint ConnectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string FriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string EdidFriendlyName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigPathSourceInfo
        {
            public DisplayConfigLuid AdapterId;
            public uint Id;
            public uint ModeInfoIndex;
            public uint StatusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigRational
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigPathTargetInfo
        {
            public DisplayConfigLuid AdapterId;
            public uint Id;
            public uint ModeInfoIndex;
            public uint OutputTechnology;
            public uint Rotation;
            public uint Scaling;
            public DisplayConfigRational RefreshRate;
            public uint ScanLineOrdering;
            public int TargetAvailable;
            public uint StatusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigPathInfo
        {
            public DisplayConfigPathSourceInfo SourceInfo;
            public DisplayConfigPathTargetInfo TargetInfo;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Explicit, Size = 64)]
        private struct DisplayConfigModeInfo
        {
            [FieldOffset(0)] public uint InfoType;
            [FieldOffset(4)] public uint Id;
            [FieldOffset(8)] public DisplayConfigLuid AdapterId;
            [FieldOffset(16)] public ulong ModeData0;
            [FieldOffset(24)] public ulong ModeData1;
            [FieldOffset(32)] public ulong ModeData2;
            [FieldOffset(40)] public ulong ModeData3;
            [FieldOffset(48)] public ulong ModeData4;
            [FieldOffset(56)] public ulong ModeData5;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigGetDpiScale
        {
            public DisplayConfigDeviceInfoHeader Header;
            public int MinScaleRelative;
            public int CurrentScaleRelative;
            public int MaxScaleRelative;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigSetDpiScale
        {
            public DisplayConfigDeviceInfoHeader Header;
            public int ScaleRelative;
        }
    }
}
