using Microsoft.AspNetCore.Mvc;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;

namespace CPTM.Manutencao.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest dadosLogin)
        {
            var token = _authService.RealizarLogin(dadosLogin.Matricula, dadosLogin.Senha);

            if (token == null)
                return Unauthorized(new { message = "Matrícula ou senha inválidos." });

            return Ok(new { token });
        }
    }
}
