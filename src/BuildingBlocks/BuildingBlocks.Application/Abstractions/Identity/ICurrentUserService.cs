namespace EmberDelivery.BuildingBlocks.Application.Abstractions.Identity;

public interface ICurrentUserService
{
    Guid GetUserId();

    bool IsAuthenticated { get; }

    bool IsInRole(string role);
}
