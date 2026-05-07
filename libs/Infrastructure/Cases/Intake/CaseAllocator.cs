using Conduct.Domain.CaseTypes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Conduct.Infrastructure.Cases.Intake;

// Allocates the next CaseNumber for (TenantId, Year, LobShortCode) and renders it via the
// CaseType's NumberFormat template.
//
// Allocation is a single atomic upsert — concurrent intakes for the same tuple will serialize
// on the row's primary-key lock, never hand out the same number twice. Format is computed in
// C# (not SQL) so the template can change without touching DB.
//
// Tenant scoping comes from the ambient TenantConnectionInterceptor (set by the consumer's
// BeginScope before this is called) — the upsert WHERE clause is implicit via RLS.
public sealed class CaseAllocator(ConductDbContext db)
{
    public sealed record AllocatedNumber(long Sequence, string CaseNumber);

    public async Task<AllocatedNumber> AllocateAsync(
        Guid tenantId, string lobShortCode, int year, CaseType caseType, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        long allocated;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO "CaseNumberSeqs" AS s
                    ("Id", "TenantId", "Year", "LobShortCode", "NextValue", "CreatedAt", "UpdatedAt")
                VALUES (@id, @tenant, @year, @lob, 2, now(), now())
                ON CONFLICT ("TenantId", "Year", "LobShortCode") DO UPDATE
                    SET "NextValue" = s."NextValue" + 1,
                        "UpdatedAt" = now()
                RETURNING ("NextValue" - 1)::bigint;
                """;
            // Parameters as raw Npgsql so we can target uuid/text/int explicitly.
            cmd.Parameters.Add(new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = Guid.NewGuid() });
            cmd.Parameters.Add(new NpgsqlParameter("@tenant", NpgsqlDbType.Uuid) { Value = tenantId });
            cmd.Parameters.Add(new NpgsqlParameter("@year", NpgsqlDbType.Integer) { Value = year });
            cmd.Parameters.Add(new NpgsqlParameter("@lob", NpgsqlDbType.Text) { Value = lobShortCode });

            var raw = await cmd.ExecuteScalarAsync(ct);
            allocated = Convert.ToInt64(raw);
        }

        var caseNumber = Render(caseType.NumberFormat, year, lobShortCode, allocated);
        return new AllocatedNumber(allocated, caseNumber);
    }

    // Tiny template renderer. Supports ONLY {year}, {lobCode}, {seq[:fmt]}.
    // Anything else passes through unchanged.
    //
    // Seq format spec is whitelisted to digits + zero-padding patterns (e.g. "000000",
    // "D6"). User-persona reviewer flagged that admin-editable templates could otherwise
    // accept format-specifier shapes like {seq:000;X;X} which open injection / output-
    // shaping risks downstream. Whitelist is a closed-set guard.
    internal static string Render(string template, int year, string lobShortCode, long seq)
    {
        var s = template;
        s = s.Replace("{year}", year.ToString());
        s = s.Replace("{lobCode}", lobShortCode);

        // {seq:<fmt>} or {seq}
        var i = 0;
        while ((i = s.IndexOf("{seq", i, StringComparison.Ordinal)) >= 0)
        {
            var end = s.IndexOf('}', i);
            if (end < 0) break;
            var token = s[i..(end + 1)]; // e.g., "{seq:000000}"
            string fmt;
            if (token == "{seq}")
            {
                fmt = seq.ToString();
            }
            else
            {
                var colon = token.IndexOf(':');
                var spec = colon > 0 ? token[(colon + 1)..^1] : "";
                if (!IsAllowedSeqFormat(spec))
                {
                    throw new InvalidOperationException(
                        $"CaseType.NumberFormat seq spec '{spec}' is not allowed; allowed: digits or 'D' + digits");
                }
                fmt = seq.ToString(spec);
            }
            s = s.Remove(i, token.Length).Insert(i, fmt);
            i += fmt.Length;
        }
        return s;
    }

    // Allow only zero-padded numeric specs ("000000") and decimal specs ("D6"). No
    // composites, no sections, no escape chars.
    private static bool IsAllowedSeqFormat(string spec)
    {
        if (string.IsNullOrEmpty(spec)) return true;
        if (spec.All(char.IsDigit)) return true;
        if ((spec[0] == 'D' || spec[0] == 'd') &&
            (spec.Length == 1 || spec.AsSpan(1).ToArray().All(char.IsDigit))) return true;
        return false;
    }
}
