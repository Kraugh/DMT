using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using DMT.Setup.Models;

namespace DMT.Setup.Services;

public sealed class PortInspectionService
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int ErrorInsufficientBuffer = 122;
    private const int TcpTableOwnerPidListener = 3;

    public IReadOnlyList<ListeningEndpoint> Inspect()
    {
        var serviceMap = ReadServiceMap();
        var rows = new List<RawListener>();
        rows.AddRange(ReadTable(AfInet));
        rows.AddRange(ReadTable(AfInet6));

        return rows
            .GroupBy(x => (x.Address, x.Port, x.ProcessId))
            .Select(g => Enrich(g.First(), serviceMap))
            .OrderBy(x => x.Port).ThenBy(x => x.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static int SuggestPort(IEnumerable<ListeningEndpoint> endpoints, int preferred = 8443)
    {
        var used = endpoints.Select(x => x.Port).ToHashSet();
        int[] candidates = [preferred, 8444, 8445, 9443, 10443, 11443];
        foreach (var port in candidates)
            if (!used.Contains(port)) return port;
        for (var port = 12000; port <= 12999; port++)
            if (!used.Contains(port)) return port;
        return 0;
    }

    private static ListeningEndpoint Enrich(RawListener row, IReadOnlyDictionary<int, string> services)
    {
        var name = row.ProcessId == 4 ? "System" : "";
        var path = "";
        try
        {
            using var p = Process.GetProcessById(row.ProcessId);
            name = p.ProcessName;
            try { path = p.MainModule?.FileName ?? ""; } catch { }
        }
        catch { }

        var isLoopback = IPAddress.TryParse(row.Address, out var ip) && IPAddress.IsLoopback(ip);
        var isAny = row.Address is "0.0.0.0" or "::";
        return new ListeningEndpoint
        {
            Address = row.Address,
            Port = row.Port,
            ProcessId = row.ProcessId,
            ProcessName = name,
            ProcessPath = path,
            Services = services.TryGetValue(row.ProcessId, out var s) ? s : "",
            Exposure = isLoopback ? "local" : isAny ? "all" : "specific",
            IsLoopback = isLoopback,
            IsAny = isAny
        };
    }

    private static IReadOnlyDictionary<int, string> ReadServiceMap()
    {
        var result = new Dictionary<int, HashSet<string>>();
        try
        {
            var psi = new ProcessStartInfo("tasklist.exe", "/svc /fo csv /nh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p is null) return new Dictionary<int, string>();
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            foreach (var line in text.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = ParseCsv(line);
                if (fields.Count < 3 || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)) continue;
                if (!result.TryGetValue(pid, out var names)) result[pid] = names = new(StringComparer.OrdinalIgnoreCase);
                foreach (var s in fields[2].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (!s.Equals("N/A", StringComparison.OrdinalIgnoreCase)) names.Add(s);
            }
        }
        catch { }
        return result.ToDictionary(x => x.Key, x => string.Join(", ", x.Value.OrderBy(v => v)));
    }

    private static List<string> ParseCsv(string line)
    {
        var fields = new List<string>(); var sb = new StringBuilder(); bool quoted = false;
        for (int i=0;i<line.Length;i++)
        {
            var c=line[i];
            if (c=='\"') { if (quoted && i+1<line.Length && line[i+1]=='\"') { sb.Append('\"'); i++; } else quoted=!quoted; }
            else if (c==',' && !quoted) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString()); return fields;
    }

    private static IEnumerable<RawListener> ReadTable(int family)
    {
        int size = 0;
        var first = GetExtendedTcpTable(IntPtr.Zero, ref size, true, family, TcpTableOwnerPidListener, 0);
        if (first != ErrorInsufficientBuffer || size <= 0) yield break;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, true, family, TcpTableOwnerPidListener, 0) != 0) yield break;
            var count = Marshal.ReadInt32(buffer);
            var ptr = IntPtr.Add(buffer, 4);
            if (family == AfInet)
            {
                var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
                for (int i=0;i<count;i++)
                {
                    var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(IntPtr.Add(ptr, i*rowSize));
                    var addr = new IPAddress(BitConverter.GetBytes(row.LocalAddr)).ToString();
                    yield return new(addr, DecodePort(row.LocalPort), unchecked((int)row.OwningPid));
                }
            }
            else
            {
                var rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
                for (int i=0;i<count;i++)
                {
                    var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(IntPtr.Add(ptr, i*rowSize));
                    var addr = new IPAddress(row.LocalAddr, row.LocalScopeId).ToString();
                    yield return new(addr, DecodePort(row.LocalPort), unchecked((int)row.OwningPid));
                }
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static int DecodePort(uint value) => (int)(((value & 0xFF) << 8) | ((value >> 8) & 0xFF));

    [DllImport("iphlpapi.dll", SetLastError=true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int ipVersion, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid { public uint State, LocalAddr, LocalPort, RemoteAddr, RemotePort, OwningPid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=16)] public byte[] LocalAddr;
        public uint LocalScopeId, LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=16)] public byte[] RemoteAddr;
        public uint RemoteScopeId, RemotePort, State, OwningPid;
    }

    private sealed record RawListener(string Address, int Port, int ProcessId);
}
