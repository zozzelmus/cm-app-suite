using AwesomeAssertions;
using Conduct.Infrastructure.Seed;
using Json.Schema;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Conduct.Api.Tests.Seed;

[Collection("postgres")]
public class DefaultCaseTypeSchemaTests(PostgresFixture pg)
{
    [Fact]
    public async Task DefaultCaseType_FieldsSchema_ParsesAsValidJsonSchema_2020_12()
    {
        await using var db = await pg.CreateFreshDbAsync();
        await new Seeder(db).SeedAsync();

        var ct = await db.CaseTypes.SingleAsync(x => x.Key == SeedConstants.DefaultCaseTypeKey);

        var act = () => JsonSchema.FromText(ct.FieldsSchemaJson);
        act.Should().NotThrow();
        var schema = act();
        schema.Should().NotBeNull();
    }

    [Fact]
    public async Task DefaultCaseType_FieldsSchema_AcceptsValidPayload()
    {
        await using var db = await pg.CreateFreshDbAsync();
        await new Seeder(db).SeedAsync();

        var ct = await db.CaseTypes.SingleAsync(x => x.Key == SeedConstants.DefaultCaseTypeKey);
        var schema = JsonSchema.FromText(ct.FieldsSchemaJson);

        using var payload = JsonDocument.Parse(/* lang=json */ """
        { "summary": "Suspected PAD violation", "occurredAt": "2026-04-15T14:30:00Z", "severity": "High" }
        """);

        var result = schema.Evaluate(payload.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task DefaultCaseType_FieldsSchema_RejectsMissingRequired()
    {
        await using var db = await pg.CreateFreshDbAsync();
        await new Seeder(db).SeedAsync();

        var ct = await db.CaseTypes.SingleAsync(x => x.Key == SeedConstants.DefaultCaseTypeKey);
        var schema = JsonSchema.FromText(ct.FieldsSchemaJson);

        using var payload = JsonDocument.Parse(/* lang=json */ """
        { "occurredAt": "2026-04-15T14:30:00Z" }
        """); // missing required "summary"

        var result = schema.Evaluate(payload.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task DefaultCaseType_FieldsSchema_RejectsInvalidEnumValue()
    {
        await using var db = await pg.CreateFreshDbAsync();
        await new Seeder(db).SeedAsync();

        var ct = await db.CaseTypes.SingleAsync(x => x.Key == SeedConstants.DefaultCaseTypeKey);
        var schema = JsonSchema.FromText(ct.FieldsSchemaJson);

        using var payload = JsonDocument.Parse(/* lang=json */ """
        { "summary": "x", "severity": "Catastrophic" }
        """); // not in enum

        var result = schema.Evaluate(payload.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task DefaultCaseType_LifecycleJson_ContainsExpectedStates()
    {
        await using var db = await pg.CreateFreshDbAsync();
        await new Seeder(db).SeedAsync();

        var ct = await db.CaseTypes.SingleAsync(x => x.Key == SeedConstants.DefaultCaseTypeKey);
        var lifecycle = JsonNode.Parse(ct.LifecycleJson)!.AsObject();
        var states = lifecycle["states"]!.AsArray()
            .Select(s => s!["name"]!.GetValue<string>())
            .ToList();

        states.Should().Contain(["Open", "Triaged", "Investigating", "PendingDecision", "Closed"]);
    }
}
