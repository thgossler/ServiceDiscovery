namespace ServiceDiscovery;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

// TODO: Make values configurable

/// <summary>
/// Implements DNS-SD (DNS Service Discovery - RFC 6763) over Multicast DNS (mDNS - RFC 6762),
/// allowing services to announce themselves and discover other services in the same local network.
/// </summary>
public class MdnsServiceDiscovery : IDisposable
{
    private const string MulticastAddress = "224.0.0.251";
    private const int MulticastPort = 5353;
    private const int MdnsTtl = 255;
    private readonly UdpClient _udpClient;
    private readonly IPEndPoint _multicastEndPoint;
    private readonly Subject<ServiceInfo> _serviceFoundSubject = new();
    private readonly Timer? _discoveryTimer;
    private readonly Timer? _announcementTimer;
    private readonly Dictionary<string, DateTime> _discoveredServices = new();
    private readonly Dictionary<Guid, ManagedService> _managedServices = new();
    private readonly TimeSpan _serviceTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Provides an observable stream of discovered services.
    /// </summary>
    public IObservable<ServiceInfo> ServiceFound => _serviceFoundSubject;

    /// <summary>
    /// Initializes the UDP client, joins the mDNS multicast group, and starts listening for mDNS messages.
    /// </summary>
    public MdnsServiceDiscovery()
    {
        _multicastEndPoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort);
        _udpClient = new UdpClient();
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpClient.ExclusiveAddressUse = false;
        _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
        _udpClient.JoinMulticastGroup(_multicastEndPoint.Address, MdnsTtl);

        // Start listening for incoming mDNS responses
        Task.Run(() => ListenForResponses());
    }

    /// <summary>
    /// Asynchronously listens for incoming mDNS responses and processes them.
    /// </summary>
    private async Task ListenForResponses()
    {
        while (true) {
            try {
                var result = await _udpClient.ReceiveAsync();
                var message = Encoding.UTF8.GetString(result.Buffer);
                HandleServiceResponse(message);
            }
            catch (Exception ex) {
                Console.WriteLine($"Error receiving mDNS response: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Processes incoming mDNS response messages, extracting service information and notifying observers.
    /// </summary>
    /// <param name="message">The received mDNS message.</param>
    private void HandleServiceResponse(string message)
    {
        var lines = message.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var serviceNameLine = lines.FirstOrDefault(line => line.StartsWith("_services._dns-sd._udp.local PTR"));
        var srvRecordLine = lines.FirstOrDefault(line => line.Contains("SRV"));
        var txtRecordLine = lines.FirstOrDefault(line => line.Contains("TXT"));

        if (serviceNameLine != null && srvRecordLine != null && txtRecordLine != null) {
            var serviceKey = serviceNameLine.Split(' ')[2].Split("._")[0]; // Extract service name
            var portPart = srvRecordLine.Split(' ')[srvRecordLine.Split(' ').Length - 2]; // Extract port
            var hostPart = srvRecordLine.Split(' ').Last().TrimEnd('.'); // Extract host
            var tagsString = txtRecordLine.Split(new[] { "\"tags=" }, StringSplitOptions.None).LastOrDefault()?.TrimEnd('\"');
            var tags = tagsString?.Split(',').ToList() ?? new List<string>();

            int.TryParse(portPart, out int port);

            // Create a ServiceInfo object with the extracted details
            var serviceInfo = new ServiceInfo(serviceKey, hostPart, port, tags);

            // Notify observers with the new ServiceInfo
            _serviceFoundSubject.OnNext(serviceInfo);
        }
    }

    /// <summary>
    /// Announces a service with the specified name, port, and tags, starting continuous announcement.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    /// <param name="port">The port on which the service is running.</param>
    /// <param name="tags">A list of tags associated with the service.</param>
    public void AnnounceService(string serviceName, int port, List<string> tags)
    {
        var managedService = new ManagedService(serviceName, port, tags);
        _managedServices.Add(managedService.Id, managedService);

        // Create and start a timer for continuous announcement
        managedService.AnnouncementTimer = new Timer(5000);
        managedService.AnnouncementTimer.Elapsed += (sender, e) => SendAnnouncement(managedService);
        managedService.AnnouncementTimer.Start();
    }

    /// <summary>
    /// Sends an mDNS announcement for a specific service.
    /// </summary>
    /// <param name="service">The service to announce.</param>
    private void SendAnnouncement(ManagedService service)
    {
        try {
            var message = CreateAnnouncementMessage(service.ServiceName, service.Port, service.Tags);
            var messageBytes = Encoding.UTF8.GetBytes(message);
            _udpClient.Send(messageBytes, messageBytes.Length, _multicastEndPoint);
        }
        catch (Exception ex) {
            Console.WriteLine($"Error announcing service: {ex.Message}");
        }
    }

    /// <summary>
    /// Initiates discovery of services with the specified name and tags, starting continuous discovery.
    /// </summary>
    /// <param name="serviceName">The name of the service to discover.</param>
    /// <param name="tags">A list of tags to filter the services.</param>
    public void DiscoverService(string serviceName, List<string> tags)
    {
        var managedService = new ManagedService(serviceName, 0, tags); // Host and Port are not needed for discovery
        _managedServices.Add(managedService.Id, managedService);

        // Create and start a timer for continuous discovery
        managedService.DiscoveryTimer = new Timer(5000);
        managedService.DiscoveryTimer.Elapsed += (sender, e) => SendDiscoveryRequest(managedService);
        managedService.DiscoveryTimer.Start();
    }

    /// <summary>
    /// Sends an mDNS discovery request for a specific service.
    /// </summary>
    /// <param name="service">The service to discover.</param>
    private void SendDiscoveryRequest(ManagedService service)
    {
        try {
            var message = CreateDiscoveryMessage(service.ServiceName, service.Tags);
            var messageBytes = Encoding.UTF8.GetBytes(message);
            _udpClient.Send(messageBytes, messageBytes.Length, _multicastEndPoint);
        }
        catch (Exception ex) {
            Console.WriteLine($"Error discovering service: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts continuous discovery for services with the specified name and tags.
    /// </summary>
    /// <param name="serviceName">The name of the service to discover.</param>
    /// <param name="tags">A list of tags to filter the services.</param>
    public void StartContinuousDiscovery(string serviceName, List<string> tags)
    {
        var managedService = _managedServices.Values.FirstOrDefault(s => s.ServiceName == serviceName && s.Tags.SequenceEqual(tags));
        if (managedService == null) {
            managedService = new ManagedService(serviceName, 0, tags); // Host and Port are not needed for discovery
            _managedServices.Add(managedService.Id, managedService);
        }

        if (managedService.DiscoveryTimer == null) {
            managedService.DiscoveryTimer = new Timer(5000);
            managedService.DiscoveryTimer.Elapsed += (sender, e) => SendDiscoveryRequest(managedService);
        }
        managedService.DiscoveryTimer.Start();
    }

    /// <summary>
    /// Stops continuous discovery for a specific service identified by its name and tags.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    /// <param name="tags">A list of tags associated with the service.</param>
    public void StopContinuousDiscovery(string serviceName, List<string> tags)
    {
        var managedService = _managedServices.Values.FirstOrDefault(s => s.ServiceName == serviceName && s.Tags.SequenceEqual(tags));
        if (managedService != null && managedService.DiscoveryTimer != null) {
            managedService.DiscoveryTimer.Stop();
            managedService.DiscoveryTimer.Dispose();
            managedService.DiscoveryTimer = null;
            Console.WriteLine($"Continuous discovery stopped for service: {serviceName}");
        }
        else {
            Console.WriteLine($"Service not found or discovery already stopped: {serviceName}");
        }
    }

    /// <summary>
    /// Stops continuous discovery of all services.
    /// </summary>
    public void StopContinuousDiscoveryForAllServices()
    {
        foreach (var managedService in _managedServices.Values) {
            if (managedService.DiscoveryTimer != null) {
                managedService.DiscoveryTimer.Stop();
                managedService.DiscoveryTimer.Dispose();
                managedService.DiscoveryTimer = null;
                Console.WriteLine($"Continuous discovery stopped for service: {managedService.ServiceName}");
            }
        }
    }

    /// <summary>
    /// Starts continuous announcement for a service with the specified name, port, and tags.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    /// <param name="port">The port on which the service is running.</param>
    /// <param name="tags">A list of tags associated with the service.</param>
    public void StartContinuousAnnouncement(string serviceName, int port, List<string> tags)
    {
        var managedService = _managedServices.Values.FirstOrDefault(s => s.ServiceName == serviceName && s.Tags.SequenceEqual(tags));
        if (managedService == null) {
            managedService = new ManagedService(serviceName, port, tags);
            _managedServices.Add(managedService.Id, managedService);
        }
        else {
            managedService.Port = port; // Update port in case it has changed
        }

        if (managedService.AnnouncementTimer == null) {
            managedService.AnnouncementTimer = new Timer(5000);
            managedService.AnnouncementTimer.Elapsed += (sender, e) => SendAnnouncement(managedService);
        }
        managedService.AnnouncementTimer.Start();
    }

    /// <summary>
    /// Stops continuous announcement for a specific service identified by its name and tags.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    /// <param name="tags">A list of tags associated with the service.</param>
    public void StopContinuousAnnouncement(string serviceName, List<string> tags)
    {
        var managedService = _managedServices.Values.FirstOrDefault(s => s.ServiceName == serviceName && s.Tags.SequenceEqual(tags));
        if (managedService != null && managedService.AnnouncementTimer != null) {
            managedService.AnnouncementTimer.Stop();
            managedService.AnnouncementTimer.Dispose();
            managedService.AnnouncementTimer = null;
            Console.WriteLine($"Continuous announcement stopped for service: {serviceName}");
        }
        else {
            Console.WriteLine($"Service not found or announcement already stopped: {serviceName}");
        }
    }

    /// <summary>
    /// Stops continuous announcement of all services.
    /// </summary>
    public void StopContinuousAnnouncementForAllServices()
    {
        foreach (var managedService in _managedServices.Values) {
            if (managedService.AnnouncementTimer != null) {
                managedService.AnnouncementTimer.Stop();
                managedService.AnnouncementTimer.Dispose();
                managedService.AnnouncementTimer = null;
                Console.WriteLine($"Continuous announcement stopped for service: {managedService.ServiceName}");
            }
        }
    }

    /// <summary>
    /// Cleans up services that have not been active within the specified timeout period.
    /// </summary>
    private void CleanupOldServices()
    {
        var now = DateTime.Now;
        var expiredServiceIds = _managedServices
            .Where(kv => now - kv.Value.LastActivity > _serviceTimeout)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var serviceId in expiredServiceIds) {
            var service = _managedServices[serviceId];
            Console.WriteLine($"Service {service.ServiceName} has been deregistered due to timeout.");
            service.Dispose(); // Properly dispose of the service
            _managedServices.Remove(serviceId);
        }
    }
    /// <summary>
    /// Creates an mDNS announcement message for a given service.
    /// </summary>
    /// <param name="serviceName">The name of the service to announce.</param>
    /// <param name="port">The port on which the service is running.</param>
    /// <param name="tags">A list of tags associated with the service.</param>
    /// <returns>A string representing the mDNS announcement message.</returns>
    private string CreateAnnouncementMessage(string serviceName, int port, List<string> tags)
    {
        var tagsString = string.Join(",", tags);
        return $"_services._dns-sd._udp.local PTR {serviceName}._tcp.local\r\n{serviceName}._tcp.local SRV 0 0 {port} {Dns.GetHostName()}.local\r\n{serviceName}._tcp.local TXT \"tags={tagsString}\"\r\n";
    }

    /// <summary>
    /// Creates an mDNS discovery message for a given service.
    /// </summary>
    /// <param name="serviceName">The name of the service to discover.</param>
    /// <param name="tags">A list of tags to filter the services.</param>
    /// <returns>A string representing the mDNS discovery message.</returns>
    private string CreateDiscoveryMessage(string serviceName, List<string> tags)
    {
        var tagsString = string.Join(",", tags);
        return $"_services._dns-sd._udp.local PTR {serviceName}._tcp.local\r\n{serviceName}._tcp.local TXT \"tags={tagsString}\"\r\n";
    }

    /// <summary>
    /// Disposes of the mDNS service discovery resources, including managed services and the UDP client.
    /// </summary>
    public void Dispose()
    {
        // Dispose of all ManagedService instances
        foreach (var service in _managedServices.Values) {
            service.Dispose(); // This will stop and dispose of the timers within each ManagedService
        }
        _managedServices.Clear(); // Clear the dictionary after disposing of the services

        // Dispose of other resources held by MdnsServiceDiscovery
        _udpClient?.DropMulticastGroup(_multicastEndPoint.Address);
        _udpClient?.Close();
        _udpClient?.Dispose();

        // If there are any other timers or disposable resources directly managed by MdnsServiceDiscovery, dispose of them here
        // For example:
        // _discoveryTimer?.Dispose();
        // _announcementTimer?.Dispose();

        // Complete the service found subject to release any subscribers
        _serviceFoundSubject?.OnCompleted();
        _serviceFoundSubject?.Dispose();
    }

    /// <summary>
    /// Removes a service by its unique identifier, stopping its announcement and discovery.
    /// </summary>
    /// <param name="serviceId">The unique identifier of the service to remove.</param>
    public void RemoveService(Guid serviceId)
    {
        if (_managedServices.TryGetValue(serviceId, out var service)) {
            service.Dispose(); // Properly dispose of the service
            _managedServices.Remove(serviceId);
            Console.WriteLine($"Service {service.ServiceName} has been manually removed.");
        }
    }
}

/// <summary>
/// Represents information about a discovered service.
/// </summary>
public class ServiceInfo
{
    public string ServiceName { get; set; }
    public List<string> Tags { get; set; }
    public string Host { get; set; } 
    public int Port { get; set; }

    public ServiceInfo(string serviceName, string host, int port, List<string> tags)
    {
        ServiceName = serviceName;
        Host = host;
        Port = port;
        Tags = tags;
    }
}

/// <summary>
/// Manages the state and timers for a service being announced or discovered.
/// </summary>
public class ManagedService : IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public string ServiceName { get; set; }
    public int Port { get; set; }
    public List<string> Tags { get; set; }
    public Timer? AnnouncementTimer { get; set; }
    public Timer? DiscoveryTimer { get; set; }
    public DateTime LastActivity { get; internal set; }

    public ManagedService(string serviceName, int port, List<string> tags)
    {
        ServiceName = serviceName;
        Port = port;
        Tags = tags;
    }

    /// <summary>
    /// Disposes of the timers associated with this service, stopping any ongoing announcement or discovery.
    /// </summary>
    public void Dispose()
    {
        AnnouncementTimer?.Stop();
        AnnouncementTimer?.Dispose();
        DiscoveryTimer?.Stop();
        DiscoveryTimer?.Dispose();
    }
}
