using Deployment.Domain.Abstractions;

namespace Deployment.Domain.AspireApps;

public interface IAspireApplicationRepository : IRepository<AspireApplication, Guid>
{
    Task<AspireApplication?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve the app a CI publish targets: matches an explicit <see cref="AspireApplication.SourceKey"/>,
    /// else falls back to name-matching for apps without one.
    /// </summary>
    Task<AspireApplication?> FindBySourceKeyAsync(string appName, CancellationToken cancellationToken = default);
}
