using System.Net;
using Aluki.Runtime.Abstractions.Skills.Calendar;

namespace Aluki.Runtime.Calendar.Connect;

/// <summary>
/// Renders the human-facing HTML for the connect experience: the consent page that
/// explains what will happen before any OAuth begins, and the success/error result
/// pages shown after the provider redirects back. Pure functions — no I/O — so they
/// are unit-testable and shared by both the Functions worker and the Host.
/// </summary>
public static class CalendarConsentPages
{
    public static string ProviderDisplayName(CalendarProvider provider) => provider switch
    {
        CalendarProvider.Outlook => "Outlook",
        CalendarProvider.Google => "Google Calendar",
        _ => provider.ToString(),
    };

    /// <summary>
    /// The pre-consent page. Explains the permissions and data handling, then offers a
    /// form that POSTs the signed token to <paramref name="beginUrl"/> only if the user
    /// agrees. The OAuth flow does not start until that button is pressed.
    /// </summary>
    public static string RenderConsent(CalendarProvider provider, string beginUrl, string token)
    {
        var name = WebUtility.HtmlEncode(ProviderDisplayName(provider));
        var action = WebUtility.HtmlEncode(beginUrl);
        var tok = WebUtility.HtmlEncode(token);

        return Page($"Conectar {name}", $$"""
            <h1>Conectar tu calendario de {{name}}</h1>
            <p>Para poder agendar tus citas, Aluki necesita tu permiso para crear eventos
               en tu calendario de <strong>{{name}}</strong>.</p>

            <div class="card">
              <h2>Qué vas a autorizar</h2>
              <ul>
                <li>✅ Crear y gestionar <strong>eventos de calendario</strong> que tú le pidas a Aluki.</li>
                <li>🔒 Tus credenciales se guardan <strong>cifradas</strong>; Aluki nunca ve tu contraseña.</li>
                <li>🙅 No leemos tu correo ni otros datos de tu cuenta.</li>
                <li>↩️ Puedes <strong>desconectar</strong> cuando quieras.</li>
              </ul>
            </div>

            <p>Al continuar te llevaremos a la pantalla de inicio de sesión <strong>oficial de {{name}}</strong>,
               donde podrás revisar y aceptar los permisos.</p>

            <form method="post" action="{{action}}">
              <input type="hidden" name="token" value="{{tok}}" />
              <button type="submit" class="btn">Conectar de forma segura</button>
            </form>
            <p class="muted">Si no fuiste tú quien lo solicitó, simplemente cierra esta página.</p>
            """);
    }

    public static string RenderSuccess(CalendarProvider provider)
    {
        var name = WebUtility.HtmlEncode(ProviderDisplayName(provider));
        return Page("Calendario conectado", $$"""
            <h1>✅ ¡Listo!</h1>
            <p>Tu calendario de <strong>{{name}}</strong> quedó conectado de forma segura.</p>
            <p>Ya puedes volver a <strong>WhatsApp</strong> y pedirme que agende tus citas.</p>
            """);
    }

    public static string RenderError(string message)
    {
        var msg = WebUtility.HtmlEncode(message);
        return Page("No se pudo conectar", $$"""
            <h1>⚠️ No pudimos completar la conexión</h1>
            <p>{{msg}}</p>
            <p>Vuelve a WhatsApp e inténtalo de nuevo solicitando un nuevo enlace.</p>
            """);
    }

    public static string RenderExpired() =>
        RenderError("Este enlace ya no es válido o expiró. Pídele a Aluki un enlace nuevo.");

    private static string Page(string title, string bodyHtml) => $$"""
        <!DOCTYPE html>
        <html lang="es">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <meta name="robots" content="noindex" />
          <title>{{WebUtility.HtmlEncode(title)}}</title>
          <style>
            :root { color-scheme: light dark; }
            body { font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
                   margin: 0; padding: 24px; line-height: 1.5;
                   display: flex; justify-content: center; }
            main { max-width: 520px; width: 100%; }
            h1 { font-size: 1.4rem; }
            h2 { font-size: 1.05rem; margin: 0 0 8px; }
            ul { padding-left: 1.2em; } li { margin: 6px 0; }
            .card { background: rgba(127,127,127,0.12); border-radius: 12px; padding: 16px; margin: 16px 0; }
            .btn { display: inline-block; width: 100%; box-sizing: border-box; border: 0;
                   background: #2563eb; color: #fff; font-size: 1rem; font-weight: 600;
                   padding: 14px 20px; border-radius: 10px; cursor: pointer; }
            .btn:hover { background: #1d4ed8; }
            .muted { color: #888; font-size: 0.85rem; margin-top: 16px; }
          </style>
        </head>
        <body><main>
        {{bodyHtml}}
        </main></body>
        </html>
        """;
}
