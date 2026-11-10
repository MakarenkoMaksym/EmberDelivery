namespace EmberDelivery.AppHost.Utils;

public static class ContainerResourceBuilderExtensions
{
    public static IResourceBuilder<T> WithPinnedImage<T>(
        this IResourceBuilder<T> builder,
        ContainerImageReference image)
        where T : ContainerResource
    {
        builder.WithImage(image.Image, image.Tag);

        // Integrations default to their own registry (docker.io, quay.io); only override it when infra.json names one,
        // otherwise a short name like "postgres" keeps resolving against Docker Hub.
        if (image.Registry is not null)
        {
            builder.WithImageRegistry(image.Registry);
        }

        return builder;
    }
}
