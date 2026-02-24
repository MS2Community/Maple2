using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Maple2.Server.Web.Controllers;

[Route("/")]
public class HealthCheckController : ControllerBase {

    [HttpGet("")]
    public IResult Get() {
        return Results.Ok();
    }
}
