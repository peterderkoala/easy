using System.Net;
using Amazon.S3;
using Easy.Data;
using Easy.MainApi.Participants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Easy.MainApi.Documents;

// S06, issues 36 and 41: any examiner reads the latest documentation PDF in the browser's own viewer (fetch with
// bearer, blob URL, iframe). Streamed from SeaweedFS with the participant's SSE-C key: inline, never cached, no ranges.
public static class DownloadDocumentation
{
    public static void MapDownloadDocumentation(this WebApplication app) =>
        app.MapGet("/api/participants/{participantId:guid}/documentation/pdf", async (Guid participantId, HttpContext context,
            EasyDb db, ParticipantKeys keys, DocumentStore store, CancellationToken ct) =>
        {
            if (await db.Participants.FindAsync([participantId], ct) is not { } participant)
                return Results.NotFound();
            if (participant.KeyForgotten)
                return Results.Problem("The participant was forgotten; their documents are gone.", statusCode: 410);
            if (await db.Documents.Where(d => d.ParticipantId == participantId).OrderByDescending(d => d.UploadedAt).FirstOrDefaultAsync(ct) is not { } document)
                return Results.NotFound();

            Stream pdf;
            try
            {
                using var key = keys.Open(participant);
                pdf = await store.OpenRead(document.ObjectKey, key, ct);
            }
            catch (AmazonS3Exception e) when (e.StatusCode is HttpStatusCode.NotFound)
            {
                return Results.Problem("The participant was forgotten; their documents are gone.", statusCode: 410); // a forget ran meanwhile
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline").ToString();
            return Results.Stream(pdf, "application/pdf"); // range processing off by default
        }).RequireAuthorization(p => p.RequireRole("examiner")); // admins hold it too
}
