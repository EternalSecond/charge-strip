using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

internal sealed class BatteryReading
{
    public readonly string Name, Icon, Status;
    public readonly int? Percent;

    public BatteryReading(string name, string icon, int? percent, string status)
    {
        Name = name;
        Icon = icon;
        Percent = percent;
        Status = percent.HasValue ? status : "Заряд недоступний";
    }

    public string Level { get { return Percent.HasValue ? Percent.Value + "%" : "—"; } }
}

internal static class Batteries
{
    private static readonly Guid Hid = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
    private static readonly Guid BluetoothBattery = new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5");
    private static readonly Guid DeviceName = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
    private const string Nova = "Arctis Nova Pro Wireless";

    public static List<BatteryReading> Read()
    {
        var rows = new List<BatteryReading>();
        var power = SystemInformation.PowerStatus;
        var flags = power.BatteryChargeStatus;
        if (flags != BatteryChargeStatus.Unknown && (flags & BatteryChargeStatus.NoSystemBattery) == 0)
        {
            int level = (int)Math.Round(power.BatteryLifePercent * 100);
            rows.Add(new BatteryReading("Батарея ПК", "\uE850", level >= 0 && level <= 100 ? (int?)level : null,
                (flags & BatteryChargeStatus.Charging) != 0 ? "Заряджається" :
                power.PowerLineStatus == PowerLineStatus.Online ? "Підключено до живлення" : "Працює від батареї"));
        }

        foreach (var bus in new[] { "BTHENUM", "BTHLE", "BTHHFENUM" })
        {
            foreach (string id in Devices(bus))
            {
                uint node;
                if (CM_Locate_DevNode(out node, id, 0) != 0) continue;
                byte[] value = Property(node, BluetoothBattery, 2, 3);
                if (value == null || value.Length != 1 || value[0] > 100) continue;
                byte[] name = Property(node, DeviceName, 14, 18) ?? Property(node, DeviceName, 2, 18);
                string label = name == null ? "Bluetooth" : System.Text.Encoding.Unicode.GetString(name).TrimEnd('\0');
                label = System.Text.RegularExpressions.Regex.Replace(label, @"\s+Hands-?Free(?:\s+(?:AG|HF))?$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!rows.Any(r => r.Icon == "\uE702" && r.Name == label))
                    rows.Add(new BatteryReading(label, "\uE702", value[0], "Рівень від Windows"));
            }
        }

        var paths = HidPaths().Where(p => p.IndexOf("vid_1038&pid_12e0&mi_04", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        bool basePresent = paths.Length > 0 || Devices("USB").Any(p => p.StartsWith(@"USB\VID_1038&PID_12E0", StringComparison.OrdinalIgnoreCase));
        if (basePresent)
        {
            byte[] packet = paths.Select(ReadNova).FirstOrDefault(p => p != null);
            // Windows report offsets for the Nova Pro USB base, including report ID.
            bool online = packet != null && (packet[15] == 8 || packet[15] == 2);
            int? headset = online && packet[6] <= 8 ? (int?)(packet[6] * 100 / 8) : null;
            // A zero dock level is also returned for an empty slot.
            bool dockCharging = packet != null && packet[2] == 1;
            int? dock = packet != null && packet[7] <= 8 && (packet[7] != 0 || dockCharging) ? (int?)(packet[7] * 100 / 8) : null;
            rows.Add(new BatteryReading(Nova + " · гарнітура", "\uE7F6", headset, packet != null && packet[15] == 2 ? "Заряджається" : "Підключено · 2,4 ГГц"));
            rows.Add(new BatteryReading(Nova + " · батарея в базі", "\uE850", dock,
                dock == 100 ? "Заряджено" : dockCharging ? "Заряджається" : "У базі"));
        }
        return rows;
    }


    private static byte[] ReadNova(string path)
    {
        using (var handle = CreateFile(path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
        {
            if (handle.IsInvalid)
            {
                return null;
            }
            IntPtr data;
            if (!HidD_GetPreparsedData(handle, out data)) return null;
            var caps = new ushort[32];
            try { if (HidP_GetCaps(data, caps) != 0x110000) return null; }
            finally { HidD_FreePreparsedData(data); }
            if (caps[1] != 0xffc0 || caps[2] < 16 || caps[3] < 2) return null;

            try
            {
                using (var stream = new FileStream(handle, FileAccess.ReadWrite, 1, true))
                {
                    var request = new byte[caps[3]];
                    request[0] = 6;
                    request[1] = 0xb0;
                    if (!WaitIo(stream.WriteAsync(request, 0, request.Length), handle, 800)) return null;

                    var response = new byte[caps[2]];
                    var clock = Stopwatch.StartNew();
                    while (clock.ElapsedMilliseconds < 900)
                    {
                        var read = stream.ReadAsync(response, 0, response.Length);
                        if (!WaitIo(read, handle, 900 - (int)clock.ElapsedMilliseconds)) break;
                        if (read.Result >= 16 && response[0] == 6 && response[1] == 0xb0) return response;
                    }
                }
            }
            catch (IOException) { }
            catch (AggregateException) { }
            return null;
        }
    }

    private static bool WaitIo(Task task, SafeFileHandle handle, int timeout)
    {
        if (task.Wait(Math.Max(0, timeout))) return true;
        CancelIoEx(handle, IntPtr.Zero);
        return false;
    }

    private static string[] Devices(string enumerator)
    {
        uint size;
        const uint presentEnumerator = 0x101;
        if (CM_Get_Device_ID_List_Size(out size, enumerator, presentEnumerator) != 0) return new string[0];
        var data = new char[size];
        return CM_Get_Device_ID_List(enumerator, data, size, presentEnumerator) == 0
            ? new string(data).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
    }

    private static string[] HidPaths()
    {
        Guid guid = Hid;
        uint size;
        if (CM_Get_Device_Interface_List_Size(out size, ref guid, null, 0) != 0) return new string[0];
        var data = new char[size];
        return CM_Get_Device_Interface_List(ref guid, null, data, size, 0) == 0
            ? new string(data).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Key { public Guid Format; public uint Id; }

    private static byte[] Property(uint node, Guid format, uint id, uint expectedType)
    {
        var key = new Key { Format = format, Id = id };
        uint size = 0, type;
        if (CM_Get_DevNode_Property(node, ref key, out type, null, ref size, 0) != 26 || size > 4096) return null;
        var bytes = new byte[size];
        return CM_Get_DevNode_Property(node, ref key, out type, bytes, ref size, 0) == 0 && type == expectedType ? bytes : null;
    }

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_Size(out uint size, string filter, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List(string filter, [Out] char[] data, uint size, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_Size(out uint size, ref Guid guid, string id, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List(ref Guid guid, string id, [Out] char[] data, uint size, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    private static extern int CM_Get_DevNode_Property(uint node, ref Key key, out uint type, [Out] byte[] data, ref uint size, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll")]
    private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
    [DllImport("hid.dll")]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")]
    private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr data, [Out] ushort[] caps);
}
