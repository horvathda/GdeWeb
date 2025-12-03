using GdeWebDB.Interfaces;
using GdeWebDB.Services;
using GdeWebDB.Utilities;
using GdeWebModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Swashbuckle.AspNetCore.Annotations;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Mime;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace GdeWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [DisableRateLimiting]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IAuthService _authService;
        private readonly ILogService _logService;

        public AuthController(IConfiguration configuration, IAuthService authService, ILogService logService)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(authService);
            ArgumentNullException.ThrowIfNull(logService);

            this._configuration = configuration;
            this._authService = authService;
            this._logService = logService;
        }

        // -----------------------------
        //  MEGLÉVŐ LOGIN + TOKEN KÓD
        // -----------------------------
        #region Existing Code

        [HttpPost]
        [Route("Login")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<LoginResultModel> Login([FromBody] LoginModel credential)
        {
            LoginResultModel loginResult = await _authService.Login(credential);

            if (loginResult.Result.Success)
            {
                string token = Utilities.Utilities.GenerateToken(loginResult, _configuration);
                loginResult.Token = token;

                double time = Convert.ToDouble(_configuration["Jwt:ExpireMinutes"]);
                ResultModel resultModel = await _authService.AddUserTokenExpirationDate(
                    loginResult.Id, token, DateTime.Now.AddHours(time)
                );
            }
            return loginResult;
        }

        [HttpPost]
        [Route("GetUserFromToken")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<LoginUserModel> GetUserFromToken([FromBody] LoginTokenModel token)
        {
            int userId = Utilities.Utilities.GetUserIdFromToken(token.Token);

            if (userId == -1)
            {
                return new LoginUserModel() { Result = ResultTypes.UserAuthenticateError };
            }

            double time = Convert.ToDouble(_configuration["Jwt:ExpireInHours"]);
            ResultModel result = await _authService.GetUserTokenExpirationDate(userId, DateTime.Now.AddHours(time));

            if (!result.Success)
            {
                return new LoginUserModel() { Result = ResultTypes.UserAuthenticateError };
            }

            LoginUserModel user = await _authService.GetUser(userId);
            user.Token = token.Token;
            return user;
        }

        #endregion

        // -----------------------------
        //  GOOGLE OAUTH 2.0 INTEGRÁCIÓ
        // -----------------------------
        #region Google OAuth

        /// <summary>
        /// Google OAuth bejelentkezés indítása
        /// </summary>
        [HttpGet("LoginGoogle")]
        public IActionResult LoginGoogle()
        {
            var props = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                RedirectUri = Url.Action("GoogleCallback")
            };

            return Challenge(props, "Google"); // A Program.cs-ben regisztrált név
        }

        /// <summary>
        /// Google OAuth visszahívás
        /// </summary>
        [HttpGet("GoogleCallback")]
        public async Task<IActionResult> GoogleCallback()
        {
            var result = await HttpContext.AuthenticateAsync("Google");

            if (!result.Succeeded)
                return Unauthorized("Google auth failed.");

            var claims = result.Principal.Claims;

            string email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            string name = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
            string picture = claims.FirstOrDefault(c => c.Type == "picture")?.Value;

            if (email == null)
                return BadRequest("Google did not return an email.");

            // Létrehozzuk vagy megkeressük a felhasználót
            LoginResultModel login = await _authService.LoginWithGoogle(email, name, picture);

            if (!login.Result.Success)
                return Unauthorized("User creation/login failed.");

            // JWT generálás
            string token = Utilities.Utilities.GenerateToken(login, _configuration);
            login.Token = token;

            await _authService.AddUserTokenExpirationDate(
                login.Id,
                token,
                DateTime.Now.AddHours(Convert.ToDouble(_configuration["Jwt:ExpireMinutes"]))
            );

            // Visszaadhatod JSON-ben:

            var frontendUrl = _configuration["websiteUrl"] ?? "https://localhost:7294";

            return Redirect($"{frontendUrl}/google-success?token={token}");

            // vagy átirányítod a frontend felé:
            // return Redirect($"https://frontend-url?token={token}");
        }

        #endregion
    }
}