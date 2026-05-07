var builder = DistributedApplication.CreateBuilder(args);

// Postgres w/ pgvector image — single instance, separate logical DBs per service if/when needed
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("conduct-pg-data")
    .WithPgAdmin();

var conductDb = postgres.AddDatabase("conductdb");

// Kafka — bank-standard messaging; local container in dev, Azure Event Hubs (Kafka API) in prod
//
// EPHEMERAL (no persistence) on purpose: the Confluent dev image bakes
// `advertised.listeners` into broker config on first launch using the host port Aspire
// picked at THAT moment. On a subsequent run, if Docker remaps the container to a
// different host port, the advertised listener is stale and clients fail to connect.
// Letting the container be ephemeral forces fresh broker config each AppHost run.
// Trade-off: lose topic data across restarts — fine for dev/POC.
var kafka = builder.AddKafka("kafka")
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

// Vite dev server (internal — reached only by BFF in dev).
// AddViteApp already attaches a default http endpoint; do NOT call WithHttpEndpoint here
// or Aspire throws "Endpoint with name 'http' already exists".
var web = builder.AddViteApp("web", "../web", "dev")
    .WithPnpm(install: true);

var bff = builder.AddProject<Projects.Conduct_Bff>("bff")
    .WithReference(api)
    .WithReference(web)
    .WithEnvironment("Auth__Authority", () => $"{keycloak.GetEndpoint("http").Url}/realms/conduct")
    .WaitFor(api)
    .WaitFor(web)
    .WaitFor(keycloak)
    .WithExternalHttpEndpoints();

builder.Build().Run();
