# EmberDelivery

Distributed food-delivery platform built with **.NET 10**, **.NET Aspire**, **Clean Architecture**, **Tactical DDD**, and **MassTransit Saga Orchestration**.

## Getting Started

```bash
# Build the entire solution (Warnings are treated as errors)
dotnet build EmberDelivery.slnx

# Run Domain, Saga State Machine, and Architecture tests
dotnet test EmberDelivery.slnx --filter "FullyQualifiedName!~IntegrationTests"

# Run the distributed application via .NET Aspire AppHost
dotnet run --project src/Aspire/AppHost
```

## License

This project is licensed under the [MIT License](https://www.google.com/search?q=LICENSE).
