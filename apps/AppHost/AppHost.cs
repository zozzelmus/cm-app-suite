var builder = DistributedApplication.CreateBuilder(args);

// Postgres w/ pgvector image — single instance, separate logical DBs per service if/when needed
// Host port pinned (5433, not the default 5432, to avoid clashing with a host-installed
// Postgres on dev machines) so external tools and pgAdmin connections stay stable across
// restarts. Inter-service connectivity already uses Aspire service discovery.
var postgres = builder.AddPostgres("postgres", port: 5433)
    .WithImage("pgvector/pgvector", "pg17")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("conduct-pg-data")
    .WithPgAdmin(c => c.WithHostPort(5050));

var conductDb = postgres.AddDatabase("conductdb");

// Kafka — bank-standard messaging; local container in dev, Azure Event Hubs (Kafka API) in prod.
// Host port pinned (9092) so the advertised listener the broker bakes on first launch always
// matches the host-side mapping on subsequent runs. This sidesteps the Confluent dev-image
// "advertised.listeners stale on Docker re-map" bug that motivated the prior ephemeral hack.
var kafka = builder.AddKafka("kafka", port: 9092)
    .WithKafkaUI(c => c.WithHostPort(9080));

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
// or Aspire throws "Endpoint with name 'http' already exists". Pin port via WithEndpoint
// configurator so HMR and BFF service-discovery resolve to the same address each run.
var web = builder.AddViteApp("web", "../web", "dev")
    .WithPnpm(install: true)
    .WithEndpoint("http", e => e.Port = 5173);

var bff = builder.AddProject<Projects.Conduct_Bff>("bff")
    .WithReference(api)
    .WithReference(web)
    .WithEnvironment("Auth__Authority", () => $"{keycloak.GetEndpoint("http").Url}/realms/conduct")
    .WaitFor(api)
    .WaitFor(web)
    .WaitFor(keycloak)
    .WithExternalHttpEndpoints();

builder.Build().Run();
