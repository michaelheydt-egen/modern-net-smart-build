using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.Run.V2;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Deployment.Application.Abstractions;
using Deployment.Domain.Runs;

namespace Deployment.Infrastructure.Gcp;

/// <summary>
/// <see cref="ICloudRunDeployer"/> backed by the Google.Cloud.Run.V2 admin API: set the target
/// service's revision-template image, route 100% traffic to the new revision, create-or-update, and
/// wait for Ready. Returns the ready revision name. Auth via ADC. Salvaged + simplified from the
/// prior deployment service's GoogleCloudRunDeploymentAdapter.
/// </summary>
internal sealed class GoogleCloudRunDeployer : ICloudRunDeployer
{
    private readonly IOptionsMonitor<GoogleCloudRunOptions> _options;
    private readonly ILogger<GoogleCloudRunDeployer> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ServicesClient? _client;

    public GoogleCloudRunDeployer(IOptionsMonitor<GoogleCloudRunOptions> options, ILogger<GoogleCloudRunDeployer> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<string> DeployAsync(CloudRunDeployRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Image))
            throw new DeploymentStepException(StepFailureKind.Config, "No image to deploy.");

        var serviceName = ServiceName.FromProjectLocationService(request.Project, request.Region, request.ServiceName);
        var opts = _options.CurrentValue;
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("[cloudrun] Deploy {Image} -> {Service} (project {Project}, region {Region}).",
            request.Image, serviceName.ServiceId, serviceName.ProjectId, serviceName.LocationId);

        try
        {
            Service? service = null;
            try
            {
                service = await client.GetServiceAsync(serviceName, CallSettings.FromCancellationToken(cancellationToken)).ConfigureAwait(false);
            }
            catch (RpcException rpc) when (rpc.StatusCode == StatusCode.NotFound)
            {
                if (!opts.CreateServiceIfMissing)
                    throw new DeploymentStepException(StepFailureKind.CloudRunNotFound,
                        $"Cloud Run service '{serviceName}' does not exist and CreateServiceIfMissing is off.");
            }

            var creating = service is null;
            service ??= new Service();
            ApplyImage(service, request.Image.Trim());
            RouteAllTrafficToLatest(service);

            var pollSettings = new PollSettings(
                Expiration.FromTimeout(TimeSpan.FromSeconds(Math.Max(1, opts.ReadinessTimeoutSeconds))),
                TimeSpan.FromSeconds(Math.Max(1, opts.ReadinessPollSeconds)));

            var operation = creating
                ? await client.CreateServiceAsync(
                        $"projects/{serviceName.ProjectId}/locations/{serviceName.LocationId}",
                        service, serviceName.ServiceId, CallSettings.FromCancellationToken(cancellationToken)).ConfigureAwait(false)
                : await client.UpdateServiceAsync(service, CallSettings.FromCancellationToken(cancellationToken)).ConfigureAwait(false);

            var completed = await operation.PollUntilCompletedAsync(
                callSettings: CallSettings.FromCancellationToken(cancellationToken), pollSettings: pollSettings).ConfigureAwait(false);

            var revision = completed.Result.LatestReadyRevision ?? string.Empty;
            _logger.LogInformation("[cloudrun] Service '{Service}' {Action}. Ready revision: {Revision}.",
                serviceName.ServiceId, creating ? "created" : "updated", revision);
            return revision;
        }
        catch (RpcException rpc)
        {
            // Categorize the most operationally meaningful Cloud Run failures so the toast can say why.
            var kind = rpc.StatusCode switch
            {
                StatusCode.Unauthenticated or StatusCode.PermissionDenied => StepFailureKind.CloudRunAuth,
                StatusCode.NotFound => StepFailureKind.CloudRunNotFound,
                StatusCode.DeadlineExceeded => StepFailureKind.Timeout,
                _ => StepFailureKind.Unknown,
            };
            throw new DeploymentStepException(kind, $"Cloud Run deploy failed ({rpc.StatusCode}): {rpc.Status.Detail}", rpc);
        }
        catch (TimeoutException tex)
        {
            throw new DeploymentStepException(StepFailureKind.Timeout,
                $"Cloud Run revision did not become ready within {opts.ReadinessTimeoutSeconds}s.", tex);
        }
    }

    private static void ApplyImage(Service service, string image)
    {
        service.Template ??= new RevisionTemplate();
        if (service.Template.Containers.Count == 0) service.Template.Containers.Add(new Container());
        service.Template.Containers[0].Image = image;
        service.Template.Revision = string.Empty;
    }

    private static void RouteAllTrafficToLatest(Service service)
    {
        service.Traffic.Clear();
        service.Traffic.Add(new TrafficTarget { Type = TrafficTargetAllocationType.Latest, Percent = 100 });
    }

    private async Task<ServicesClient> GetClientAsync(CancellationToken ct)
    {
        if (_client is not null) return _client;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return _client ??= await new ServicesClientBuilder().BuildAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
