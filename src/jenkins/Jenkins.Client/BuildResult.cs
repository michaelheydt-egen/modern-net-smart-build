using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jenkins.Client;

[JsonConverter(typeof(BuildResultJsonConverter))]
public enum BuildResult
{
    Success,
    Failure,
    Unstable,
    Aborted,
    NotBuilt
}

internal sealed class BuildResultJsonConverter : JsonStringEnumConverter<BuildResult>
{
    public BuildResultJsonConverter()
        : base(namingPolicy: JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false) { }
}
