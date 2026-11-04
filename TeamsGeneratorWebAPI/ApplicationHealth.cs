namespace TeamsGeneratorWebAPI;

public static class ApplicationHealth
{
    // MVID identifies the compiled API module, including dirty-source builds, not a checkout or setting.
    public static string Revision { get; } = typeof(ApplicationHealth).Module.ModuleVersionId.ToString("N");
    public const string RevisionHeader = "X-Teamify-Revision";
    public const string RuntimeHeader = "X-Teamify-Runtime";
    public const string ReadinessHeader = "X-Teamify-Readiness";

    public static IApplicationBuilder UseDeploymentIdentity(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            // Opt-in probes can attribute each route response during overlapping worker recycle.
            if (context.Request.Headers[ReadinessHeader] == "1") Stamp(context);
            await next(context);
        });

    private static void Stamp(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers[RevisionHeader] = Revision;
        context.Response.Headers[RuntimeHeader] = Environment.Version.ToString();
    }

    public static IResult Read(HttpContext context)
    {
        Stamp(context);
        return Results.Ok(new { status = "healthy" });
    }
}
