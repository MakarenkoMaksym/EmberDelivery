using System.Text.Json;

namespace EmberDelivery.AppHost.Utils;

// infra.json is the single source of container versions shared with the Testcontainers-based integration tests,
// so the services are developed against exactly the infrastructure they are tested against.
public sealed class InfrastructureImages
{
    public const string FileName = "infra.json";

    private const string RootSectionName = "Infrastructure";

    private readonly Dictionary<string, ContainerImageReference> _images;

    private InfrastructureImages(Dictionary<string, ContainerImageReference> images)
    {
        _images = images;
    }

    public ContainerImageReference Postgres => Get("Postgres");

    public ContainerImageReference Redis => Get("Redis");

    public ContainerImageReference RabbitMq => Get("RabbitMq");

    public ContainerImageReference Keycloak => Get("Keycloak");

    // The file is linked into the build output, so lookup does not depend on the working directory the AppHost
    // is launched from (IDE, dotnet run, aspire run).
    public static InfrastructureImages LoadFromOutputDirectory() =>
        Load(Path.Combine(AppContext.BaseDirectory, FileName));

    public static InfrastructureImages Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"'{FileName}' was not found next to the AppHost binaries. Ensure it is linked into the AppHost output.",
                path);
        }

        using FileStream stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty(RootSectionName, out JsonElement section)
            || section.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"'{path}' must contain an '{RootSectionName}' object.");
        }

        Dictionary<string, ContainerImageReference> images = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in section.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    $"'{RootSectionName}:{property.Name}' in '{path}' must be an image reference string.");
            }

            images[property.Name] = ContainerImageReference.Parse(property.Value.GetString()!);
        }

        return new InfrastructureImages(images);
    }

    private ContainerImageReference Get(string name) =>
        _images.TryGetValue(name, out ContainerImageReference? image)
            ? image
            : throw new InvalidOperationException($"Image '{RootSectionName}:{name}' is missing from '{FileName}'.");
}
