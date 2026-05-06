var builder = DistributedApplication.CreateBuilder(args);

// Postgres w/ pgvector image — single instance, separate logical DBs per service if/when needed
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("conduct-pg-data")
    .WithPgAdmin();

var conductDb = postgres.AddDatabase("conductdb");

// Kafka — bank-standard messaging; local container in dev, Azure Event Hubs (Kafka API) in prod
var kafka = builder.AddKafka("kafka")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("conduct-kafka-data")
    .WithKafkaUI(); // browser UI for inspecting topics during dev

// Keycloak — dev mode w/ realm import from infra/keycloak/realm
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.3")
    .WithArgs("start-dev", "--import-realm")
    .WithEnvironment("KEYCLOAK_ADMIN", "admin")
    .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin")
    .WithBindMount("../../infra/keycloak/realm", "/opt/keycloak/data/import")
    .WithHttpEndpoint(port: 8088, targetPort: 8080, name: "http")
    .WithLifetime(ContainerLifetime.Persistent);

var api = builder.AddProject<Projects.Conduct_Api>("api")
    .WithReference(conductDb)
    .WithReference(kafka)
    .WaitFor(conductDb)
    .WaitFor(kafka);

// Vite dev server (internal — reached only by BFF in dev)
var web = builder.AddViteApp("web", "../web", "dev")
    .WithPnpm(install: true)
    .WithHttpEndpoint(port: 5173, env: "PORT");

var bff = builder.AddProject<Projects.Conduct_Bff>("bff")
    .WithReference(api)
    .WithReference(web)
    .WithEnvironment("Auth__Authority", () => $"{keycloak.GetEndpoint("http").Url}/realms/conduct")
    .WaitFor(api)
    .WaitFor(web)
    .WaitFor(keycloak)
    .WithExternalHttpEndpoints();

builder.Build().Run();
