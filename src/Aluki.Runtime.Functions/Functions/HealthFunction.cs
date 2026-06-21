using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Aluki.Runtime.Functions.Functions;

public sealed class HealthFunction
{
    [Function("Health")]
    public HttpResponseData Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "health")] HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.WriteString("ok");
        return response;
    }
}
