using Easy.Contracts;
using Easy.Data;
using Easy.MainApi.Participants;
using Easy.Platform;
using Microsoft.EntityFrameworkCore;
using Rebus.Bus;

namespace Easy.MainApi.Documents;

// S06, ADR 0002: proxy upload. Raw application/pdf body, buffered (at most Upload:MaxBytes), checked for the PDF
// magic bytes, written to SeaweedFS under the participant key, then Document row, status and DocumentUploaded commit
// together through UseOutbox.
public static class UploadDocument
{
    public static void MapUploadDocument(this WebApplication app) =>
        app.MapPost("/api/participants/{participantId:guid}/documents", async (Guid participantId, HttpRequest request,
            EasyDb db, ParticipantKeys keys, DocumentStore store, IBus bus, IConfiguration config, CancellationToken ct) =>
        {
            long max = Upload.MaxBytes(config);
            using var buffer = new MemoryStream();
            if (request.ContentLength > max || !await CopyCapped(request.Body, buffer, max, ct))
                return Results.Problem($"File larger than {max} bytes.", statusCode: 413);
            byte[] pdf = buffer.ToArray();
            if (!pdf.AsSpan().StartsWith("%PDF-"u8))
                return Results.Problem("Only PDF files.", statusCode: 415);

            // Row lock until commit: a concurrent first submit waits, or this upload sees it.
            var participant = await db.Participants.FromSql($"SELECT * FROM participants WHERE id = {participantId} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (participant is null || participant.KeyForgotten)
                return Results.Problem("Unknown participant.", statusCode: 404);
            if (participant.DocumentationStatus is DocumentationStatus.InProgress or DocumentationStatus.Graded)
                return Results.Problem("Grading has begun; the documentation can no longer be replaced.", statusCode: 409); // issue 29, case 2

            // ponytail: an object written before a failed commit stays orphaned (cleanup out of scope)
            Guid id = Guid.NewGuid();
            var document = new Document { Id = id, ParticipantId = participantId, ObjectKey = DocumentStore.Key(participantId, id), Size = pdf.Length, UploadedAt = DateTimeOffset.UtcNow };
            using (var key = keys.Open(participant))
                await store.Put(document.ObjectKey, pdf, key, ct);

            db.Documents.Add(document);
            participant.DocumentationStatus ??= DocumentationStatus.Submitted; // first upload only (S14)
            await db.SaveChangesAsync(ct);
            await bus.Publish(new DocumentUploaded(document.Id, participantId));
            return Results.Created((string?)null, new { document.Id });
        }).RequireAuthorization(p => p.RequireRole("examiner"));

    // TestServer and chunked bodies have no Kestrel limit, so the endpoint counts too.
    static async Task<bool> CopyCapped(Stream body, MemoryStream buffer, long max, CancellationToken ct)
    {
        var chunk = new byte[81920];
        for (int n; (n = await body.ReadAsync(chunk, ct)) > 0;)
        {
            if (buffer.Length + n > max)
                return false;
            buffer.Write(chunk, 0, n);
        }
        return true;
    }
}
