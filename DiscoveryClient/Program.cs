using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceDiscovery;

// TODO: Support HTTPS with hostname and issuer validation 

class Program
{
    private static readonly HttpClient HttpClient = new HttpClient();
    private static readonly Dictionary<string, ServiceInfo> DiscoveredServices = new Dictionary<string, ServiceInfo>();
    private static readonly BehaviorSubject<bool> AllServicesDiscovered = new BehaviorSubject<bool>(false);
    private static readonly MdnsServiceDiscovery MdnsService = new MdnsServiceDiscovery();

    static async Task Main(string[] args)
    {
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent the process from terminating immediately.
            Console.WriteLine("Exiting... Please wait for cleanup.");

            // Stop all announcements and discoveries
            MdnsService.StopContinuousAnnouncementForAllServices();
            MdnsService.StopContinuousDiscoveryForAllServices();

            // Dispose of the MdnsServiceDiscovery instance
            MdnsService.Dispose();

            // Additional cleanup logic can go here

            Environment.Exit(0); // Terminate the application after cleanup
        };

        if (args.Length == 0 || !int.TryParse(args[0], out int serviceNumber) || serviceNumber < 1 || serviceNumber > 3) {
            Console.WriteLine("Please specify a service number between 1 and 3.");
            return;
        }

        var role = $"role{serviceNumber}";
        var port = 8080 + serviceNumber;
        var serviceName = $"MyService{serviceNumber}";

        // Announce this service continuously
        MdnsService.StartContinuousAnnouncement(serviceName, port, new List<string> { role });

        // Subscribe to discovered services
        MdnsService.ServiceFound.Subscribe(serviceInfo => {
            var discoveredServiceName = serviceInfo.ServiceName;
            var discoveredRole = serviceInfo.Tags.FirstOrDefault();
            if (discoveredRole != null) {
                lock (DiscoveredServices) {
                    if (!DiscoveredServices.ContainsKey(discoveredRole)) {
                        DiscoveredServices[discoveredRole] = serviceInfo;
                        if (DiscoveredServices.Count == 3) {
                            AllServicesDiscovered.OnNext(true);
                        }
                    }
                }
            }
        });

        // Start continuous discovery of other services
        foreach (var i in Enumerable.Range(1, 3)) {
            if (i != serviceNumber) { // Don't discover itself
                MdnsService.StartContinuousDiscovery($"MyService{i}", new List<string> { $"role{i}" });
            }
        }

        // Setup and start the web server
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers();
        builder.WebHost.UseUrls($"http://*:{port}");

        var app = builder.Build();

        app.MapGet($"/{serviceName}", () => role switch {
            "role1" => "Hello",
            "role2" => "world",
            "role3" => "!",
            _ => "Unknown role"
        });

        _ = app.RunAsync();

        // Wait for all services to be discovered
        await AllServicesDiscovered.Where(x => x).FirstAsync();

        // Call other services' APIs based on their roles
        var hello = await CallServiceApi("role1");
        var world = await CallServiceApi("role2");
        var exclamation = await CallServiceApi("role3");

        Console.WriteLine($"{hello} {world}{exclamation}");

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();

        // Stop all announcements and discoveries
        MdnsService.StopContinuousAnnouncementForAllServices();
        MdnsService.StopContinuousDiscoveryForAllServices();

        MdnsService.Dispose();
    }

    private static async Task<string> CallServiceApi(string role)
    {
        if (DiscoveredServices.TryGetValue(role, out ServiceInfo? serviceInfo)) {
            var url = $"http://{serviceInfo.Host}:{serviceInfo.Port}/{serviceInfo.ServiceName}/";

            try {
                var response = await HttpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException e) {
                Console.WriteLine($"Error calling the service API: {e.Message}");
                return $"Failed to call service with role {role}";
            }
        }
        else {
            throw new InvalidOperationException($"Service with role {role} not found");
        }
    }
}
