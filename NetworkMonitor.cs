using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NHMonitor;

/// <summary>
/// Débit de la carte réseau active : celle qui est connectée et possède une passerelle par défaut
/// (c'est elle qui porte le trafic Internet). Les débits sont en octets par seconde.
/// </summary>
public sealed class NetworkMonitor
{
    private static readonly TimeSpan SelectionMaxAge = TimeSpan.FromSeconds(30);

    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _selectionAge = new();
    private volatile bool _selectionDirty = true;
    private NetworkInterface? _selected;
    private string? _interfaceId;
    private long _lastReceived;
    private long _lastSent;

    public NetworkMonitor()
    {
        // Lister toutes les cartes est coûteux : on ne le refait que lorsque Windows signale un changement
        // (câble, Wi-Fi, VPN...), avec une réévaluation de sécurité toutes les 30 s.
        NetworkChange.NetworkAddressChanged += (_, _) => _selectionDirty = true;
        NetworkChange.NetworkAvailabilityChanged += (_, _) => _selectionDirty = true;
    }

    public (string? Name, double? DownBps, double? UpBps) Read()
    {
        if (_selectionDirty || _selectionAge.Elapsed > SelectionMaxAge)
        {
            _selectionDirty = false;
            _selected = FindActiveInterface();
            _selectionAge.Restart();
        }

        var nic = _selected;
        if (nic is null)
        {
            _interfaceId = null;
            return (null, null, null);
        }

        IPInterfaceStatistics stats;
        try { stats = nic.GetIPStatistics(); }
        catch (NetworkInformationException)
        {
            // Carte retirée entre deux relevés : nouvelle sélection au prochain passage.
            _selectionDirty = true;
            return (nic.Name, null, null);
        }

        long received = stats.BytesReceived;
        long sent = stats.BytesSent;
        double elapsed = _clock.Elapsed.TotalSeconds;

        double? down = null, up = null;
        // Premier relevé (ou changement de carte) : pas encore de référence pour calculer un débit.
        if (_interfaceId == nic.Id && elapsed > 0)
        {
            down = Math.Max(0, received - _lastReceived) / elapsed;
            up = Math.Max(0, sent - _lastSent) / elapsed;
        }

        _interfaceId = nic.Id;
        _lastReceived = received;
        _lastSent = sent;
        _clock.Restart();

        return (nic.Name, down, up);
    }

    private static NetworkInterface? FindActiveInterface()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(n => (Nic: n, Props: n.GetIPProperties()))
                .Where(x => x.Props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork
                        ? !g.Address.Equals(System.Net.IPAddress.Any)
                        : !g.Address.Equals(System.Net.IPAddress.IPv6Any)))
                // Plusieurs cartes connectées : Windows privilégie le filaire, on fait de même.
                .OrderBy(x => TypePriority(x.Nic.NetworkInterfaceType))
                .Select(x => x.Nic)
                .FirstOrDefault();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static int TypePriority(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => 0,
        NetworkInterfaceType.Wireless80211 => 1,
        _ => 2,
    };

    /// <summary>Formate un débit en octets/s : "850 o/s", "12 Ko/s", "1.5 Mo/s".</summary>
    public static string FormatRate(double? bytesPerSecond)
    {
        if (bytesPerSecond is not { } v) return "--";
        string[] units = ["o/s", "Ko/s", "Mo/s", "Go/s"];
        int u = 0;
        // Bascule à 1000 (et non 1024) pour ne jamais dépasser 3 chiffres.
        while (v >= 1000 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        return v < 10 && u > 0 ? $"{v:0.0} {units[u]}" : $"{v:0} {units[u]}";
    }
}
