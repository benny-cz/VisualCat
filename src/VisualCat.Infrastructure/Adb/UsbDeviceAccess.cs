namespace VisualCat.Infrastructure.Adb;

/// <summary>
/// Whether this computer has an Android device on its own USB bus that this account may not open.
/// </summary>
/// <remarks>
/// ADB reads a device's descriptors before it decides whether to list it, so a node the account
/// cannot read at all is left out of <c>adb devices</c> entirely: the user is told nothing is
/// connected while the phone sits on the bus with debugging enabled. That is what a <c>udev</c>
/// rule granting a group the account is not in produces — <c>MODE="0660"</c> leaves no
/// world-readable bit — and it is worse than <see cref="AdbDeviceState.NoPermissions"/>, which at
/// least names itself. Measured live: mode <c>0664</c> gives "no permissions", mode <c>0660</c>
/// gives silence, and the same device is <c>device</c> for an account in the group (A-16, F-34).
/// sysfs answers the question without opening anything — an ADB interface is class <c>ff</c>,
/// subclass <c>42</c>, protocol <c>01</c>, and interface descriptors stay world-readable when the
/// device node does not.
/// </remarks>
public static class UsbDeviceAccess
{
    /// <summary>The steps that fix it, for a message with room for commands.</summary>
    /// <remarks>
    /// The ADB server is part of the fix, not an afterthought: a server already running keeps
    /// the credentials it started with, so adding your account to the group changes nothing
    /// until it is restarted. Measured — the same account, in the group, still saw no device
    /// until <c>adb kill-server</c>.
    /// </remarks>
    public const string ShellRemedy =
        "add a udev rule for the device's vendor id and reload it — " +
        "'sudo tee /etc/udev/rules.d/51-android.rules', then " +
        "'sudo udevadm control --reload-rules && sudo udevadm trigger' — and make sure your " +
        "account is in the group that rule grants (often plugdev). Then replug the device and " +
        "restart the daemon with 'adb kill-server', because a running server keeps the " +
        "credentials it started with.";

    /// <summary>The same fix for a status line, without the commands.</summary>
    public const string ShortRemedy =
        "Add a udev rule granting a group your account is in — often plugdev — then replug the " +
        "device, run 'adb kill-server', and refresh.";

    private const string UsbDevices = "/sys/bus/usb/devices";
    private const string AdbInterfaceClass = "ff";
    private const string AdbInterfaceSubClass = "42";
    private const string AdbInterfaceProtocol = "01";

    /// <summary>
    /// Attached Android devices with debugging enabled that this account cannot open, each
    /// described by its node, product and mode. Always empty off Linux.
    /// </summary>
    public static IReadOnlyList<string> UnopenableAdbDevices()
    {
        if (!OperatingSystem.IsLinux() || !Directory.Exists(UsbDevices))
        {
            return [];
        }

        var found = new List<string>();
        try
        {
            foreach (var entry in Directory.EnumerateDirectories(UsbDevices))
            {
                // Interface directories are "<device>:<configuration>.<interface>"; the rest are
                // the devices themselves, which carry no interface descriptors.
                var name = Path.GetFileName(entry);
                var separator = name.IndexOf(':', StringComparison.Ordinal);
                if (separator <= 0 || !IsAdbInterface(entry))
                {
                    continue;
                }

                var device = Path.Combine(UsbDevices, name[..separator]);
                var node = NodeOf(device);
                if (node is not null && !CanOpen(node))
                {
                    found.Add(Describe(node, device));
                }
            }
        }
        catch (IOException)
        {
            // sysfs is not guaranteed — a container may not mount it, and a device can vanish
            // mid-walk. Neither is evidence of a permission problem, so claim nothing.
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        return found;
    }

    /// <summary>
    /// One sentence naming what is attached but unopenable, or <c>null</c> when nothing is.
    /// </summary>
    public static string? MissingDeviceExplanation()
    {
        var hidden = UnopenableAdbDevices();
        if (hidden.Count == 0)
        {
            return null;
        }

        var subject = hidden.Count == 1
            ? "An Android device with USB debugging enabled is attached to this computer and " +
              "this account may not open it"
            : $"{hidden.Count} Android devices with USB debugging enabled are attached to this " +
              "computer and this account may not open them";
        return $"{subject} — {string.Join("; ", hidden)} — and ADB leaves out a device whose " +
               "USB node it cannot read.";
    }

    private static bool IsAdbInterface(string interfaceDirectory) =>
        Reads(interfaceDirectory, "bInterfaceClass", AdbInterfaceClass) &&
        Reads(interfaceDirectory, "bInterfaceSubClass", AdbInterfaceSubClass) &&
        Reads(interfaceDirectory, "bInterfaceProtocol", AdbInterfaceProtocol);

    private static bool Reads(string directory, string attribute, string expected) =>
        string.Equals(Value(directory, attribute), expected, StringComparison.OrdinalIgnoreCase);

    private static string? Value(string directory, string attribute)
    {
        try
        {
            var path = Path.Combine(directory, attribute);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? NodeOf(string device)
    {
        if (!int.TryParse(Value(device, "busnum"), out var bus) ||
            !int.TryParse(Value(device, "devnum"), out var number))
        {
            return null;
        }

        var node = $"/dev/bus/usb/{bus:D3}/{number:D3}";
        return File.Exists(node) ? node : null;
    }

    private static bool CanOpen(string node)
    {
        try
        {
            using var stream = new FileStream(
                node, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            // Busy, or gone. Only a refusal is evidence, so treat everything else as openable
            // rather than blame permissions for something that was never measured.
            return true;
        }
    }

    private static string Describe(string node, string device)
    {
        var parts = new List<string>();
        var product = Value(device, "product");
        if (!string.IsNullOrEmpty(product))
        {
            parts.Add(product);
        }

        var serial = Value(device, "serial");
        if (!string.IsNullOrEmpty(serial))
        {
            parts.Add($"serial {serial}");
        }

        parts.Add($"mode {Octal(node)}");
        return $"{node} ({string.Join(", ", parts)})";
    }

    private static string Octal(string node)
    {
        if (!OperatingSystem.IsLinux())
        {
            return "unknown";
        }

        try
        {
            var mode = File.GetUnixFileMode(node);
            var bits =
                (mode.HasFlag(UnixFileMode.UserRead) ? 0b100_000_000 : 0) |
                (mode.HasFlag(UnixFileMode.UserWrite) ? 0b010_000_000 : 0) |
                (mode.HasFlag(UnixFileMode.UserExecute) ? 0b001_000_000 : 0) |
                (mode.HasFlag(UnixFileMode.GroupRead) ? 0b000_100_000 : 0) |
                (mode.HasFlag(UnixFileMode.GroupWrite) ? 0b000_010_000 : 0) |
                (mode.HasFlag(UnixFileMode.GroupExecute) ? 0b000_001_000 : 0) |
                (mode.HasFlag(UnixFileMode.OtherRead) ? 0b000_000_100 : 0) |
                (mode.HasFlag(UnixFileMode.OtherWrite) ? 0b000_000_010 : 0) |
                (mode.HasFlag(UnixFileMode.OtherExecute) ? 0b000_000_001 : 0);
            return Convert.ToString(bits, 8).PadLeft(4, '0');
        }
        catch (IOException)
        {
            return "unknown";
        }
        catch (UnauthorizedAccessException)
        {
            return "unknown";
        }
    }
}
