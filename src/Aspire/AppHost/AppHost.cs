IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Ordering_Api>("ordering-api");

await builder.Build().RunAsync();
