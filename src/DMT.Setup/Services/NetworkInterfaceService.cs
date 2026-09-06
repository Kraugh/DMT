using System.Net.NetworkInformation;
using System.Net.Sockets;
using DMT.Setup.Models;

namespace DMT.Setup.Services;

public sealed class NetworkInterfaceService
{
    public IReadOnlyList<NetworkInterfaceInfo> Inspect()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(nic =>
            {
                var addresses = nic.GetIPProperties().UnicastAddresses;

                var ipv4 = addresses
                    .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(x => x.Address.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var ipv6 = addresses
                    .Where(x => x.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Where(x => !x.Address.IsIPv6LinkLocal)
                    .Select(x => x.Address.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new NetworkInterfaceInfo
                {
                    Id = nic.Id,
                    Name = nic.Name,
                    Description = nic.Description,
                    InterfaceType = nic.NetworkInterfaceType.ToString(),
                    IPv4Addresses = ipv4,
                    IPv6Addresses = ipv6
                };
            })
            .Where(x => x.IPv4Addresses.Count > 0 || x.IPv6Addresses.Count > 0)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
