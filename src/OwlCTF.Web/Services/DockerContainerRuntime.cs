using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;
using OwlCTF.Options;

namespace OwlCTF.Services;

public sealed class DockerContainerRuntime : IContainerRuntime, IDisposable
{
    private readonly DockerClient client;
    private readonly DynamicInstanceOptions options;
    public DockerContainerRuntime(IOptions<DynamicInstanceOptions> configured)
    {
        options = configured.Value;
        var endpoint = options.DockerEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint)) endpoint = OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock";
        client = new DockerClientConfiguration(new Uri(endpoint)).CreateClient();
    }
    public async Task<ContainerLaunchResult> StartAsync(ContainerLaunchRequest request, CancellationToken ct)
    {
        var portKey = request.ContainerPort + "/tcp";
        var created = await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = request.Image,
            Env = [request.FlagEnvironmentVariable + "=" + request.Flag],
            ExposedPorts = new Dictionary<string, EmptyStruct> { [portKey] = default },
            Labels = new Dictionary<string, string> { ["owlctf.managed"] = "true", ["owlctf.instance"] = request.InstanceId.ToString("N"), ["owlctf.team"] = request.TeamId.ToString("N"), ["owlctf.challenge"] = request.ChallengeId.ToString("N") },
            HostConfig = new HostConfig
            {
                NanoCPUs = request.NanoCpus,
                Memory = request.MemoryBytes,
                MemorySwap = request.MemoryBytes,
                AutoRemove = false,
                CapDrop = ["ALL"],
                CapAdd = ["CHOWN", "SETUID", "SETGID", "NET_BIND_SERVICE"],
                SecurityOpt = ["no-new-privileges:true"],
                PortBindings = new Dictionary<string, IList<PortBinding>> { [portKey] = [new PortBinding { HostPort = "" }] }
            }
        }, ct);
        try
        {
            if (!await client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), ct)) throw new InvalidOperationException("Docker did not start the container.");
            var inspected = await client.Containers.InspectContainerAsync(created.ID, ct);
            var binding = inspected.NetworkSettings.Ports[portKey].FirstOrDefault() ?? throw new InvalidOperationException("Docker did not publish the challenge port.");
            if (!int.TryParse(binding.HostPort, out var hostPort) || hostPort <= 0) throw new InvalidOperationException("Docker returned an invalid host port.");
            return new(created.ID, hostPort);
        }
        catch { await RemoveAsync(created.ID, ct); throw; }
    }
    public async Task StopAndRemoveAsync(string containerId, CancellationToken ct)
    {
        try { await client.Containers.StopContainerAsync(containerId, new ContainerStopParameters { WaitBeforeKillSeconds = (uint)options.StopTimeoutSeconds }, ct); }
        catch (DockerApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.NotModified) { }
        await RemoveAsync(containerId, ct);
    }
    private async Task RemoveAsync(string id, CancellationToken ct)
    {
        try { await client.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, ct); }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
    }
    public void Dispose() => client.Dispose();
}
