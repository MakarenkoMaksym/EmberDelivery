namespace EmberDelivery.AppHost.Utils;

public sealed record ContainerImageReference(string? Registry, string Image, string Tag)
{
    // Accepts "[registry/]image:tag". A tag is mandatory and "latest" is rejected: infra.json exists to pin versions,
    // so silently falling back to a floating tag would defeat it and let local runs drift from integration tests.
    public static ContainerImageReference Parse(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        string value = reference.Trim();

        if (value.Contains('@', StringComparison.Ordinal))
        {
            throw new FormatException($"Container image '{value}' uses a digest; pin it with an explicit tag instead.");
        }

        int lastSlash = value.LastIndexOf('/');
        int tagSeparator = value.LastIndexOf(':');

        // A colon before the last slash belongs to a registry port (e.g. "localhost:5000/app"), not to a tag.
        if (tagSeparator <= lastSlash || tagSeparator == value.Length - 1)
        {
            throw new FormatException($"Container image '{value}' must specify an explicit tag.");
        }

        string tag = value[(tagSeparator + 1)..];
        if (string.Equals(tag, "latest", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"Container image '{value}' must not use the floating 'latest' tag.");
        }

        string repository = value[..tagSeparator];
        int firstSlash = repository.IndexOf('/', StringComparison.Ordinal);

        // Docker's own rule: the first path segment is a registry only if it looks like a host.
        if (firstSlash > 0 && IsRegistryHost(repository[..firstSlash]))
        {
            return new ContainerImageReference(repository[..firstSlash], repository[(firstSlash + 1)..], tag);
        }

        return new ContainerImageReference(null, repository, tag);
    }

    public override string ToString() =>
        Registry is null ? $"{Image}:{Tag}" : $"{Registry}/{Image}:{Tag}";

    private static bool IsRegistryHost(string segment) =>
        segment.Contains('.', StringComparison.Ordinal)
        || segment.Contains(':', StringComparison.Ordinal)
        || string.Equals(segment, "localhost", StringComparison.Ordinal);
}
