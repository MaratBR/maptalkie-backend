using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using MapTalkie.Configuration;
using MapTalkie.DB;
using MapTalkie.Domain.Messages.User;
using MapTalkie.Services.AuthService;
using MapTalkie.Services.TokenService;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MapTalkie.Controllers;

[ApiController]
[Route("[controller]")]
public class AuthController : Controller
{
    private readonly IOptions<AuthenticationSettings> _authenticationSettings;
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;
    private readonly UserManager<User> _userManager;

    public AuthController(
        ITokenService tokenService,
        IAuthService authService,
        UserManager<User> userManager,
        IOptions<AuthenticationSettings> authenticationSettings)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _authenticationSettings = authenticationSettings;
        _authService = authService;
    }

    [HttpPost("signin")]
    public Task<ActionResult<LoginResponse>> SignIn(
        [FromBody] LoginRequest body,
        [FromServices] ITokenService tokenService)
    {
        return SignInPrivate(body, false);
    }

    [HttpPost("hybrid-signin")]
    public Task<ActionResult<LoginResponse>> HybridSignIn([FromBody] LoginRequest body)
    {
        return SignInPrivate(body, true);
    }

    private async Task<ActionResult<LoginResponse>> SignInPrivate(LoginRequest body, bool hybrid)
    {
        var user = await _userManager.FindByNameAsync(body.UserName);
        if (user == null)
            return Unauthorized();

        if (await _userManager.CheckPasswordAsync(user, body.Password))
        {
            var refreshToken = await _authService.CreateRefreshToken(user);
            var token = _tokenService.CreateToken(user);


            return MakeLoginResponse(token, refreshToken, hybrid);
        }

        return Unauthorized();
    }

    private ActionResult<LoginResponse> MakeLoginResponse(JwtTokenResult token, IRefreshTokenResult refreshToken,
        bool hybrid)
    {
        var expiresAt = DateTimeOffset.Now + _authenticationSettings.Value.RefreshTokenLifetime;
        if (hybrid)
        {
            Response.Cookies.Append(
                _authenticationSettings.Value.HybridCookieName,
                token.Signature,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.None,
                    Secure = true,
                    Expires = token.ExpiresAt
                });

            Response.Cookies.Append(
                _authenticationSettings.Value.RefreshTokenCookieName,
                refreshToken.Token,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.None,
                    Secure = true,
                    Path = new Uri(Url.RouteUrl("AuthHybridTokenRefresh")!).AbsolutePath,
                    Expires = DateTimeOffset.Now + _authenticationSettings.Value.RefreshTokenLifetime
                });
        }

        return new LoginResponse
        {
            Token = hybrid ? token.TokenBase : token.FullToken,
            RefreshToken = hybrid ? null : refreshToken.Token,
            ExpiresAt = token.ExpiresAt,
            ExpiresAtTimestamp = (int)(token.ExpiresAt - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds
        };
    }

    [HttpPost("refresh")]
    public Task<ActionResult<LoginResponse>> RefreshToken([FromBody] RefreshTokenRequest body)
    {
        return RefreshTokenPrivate(body.RefreshToken, false);
    }

    [HttpPost("hybrid-refresh", Name = "AuthHybridTokenRefresh")]
    public async Task<ActionResult<LoginResponse>> HybridRefreshToken()
    {
        var token = HttpContext.Request.Cookies[_authenticationSettings.Value.RefreshTokenCookieName];
        if (token == null)
            return Unauthorized();
        return await RefreshTokenPrivate(token, true);
    }

    private async Task<ActionResult<LoginResponse>> RefreshTokenPrivate(string refreshToken, bool hybrid)
    {
        var token = await _authService.FindRefreshToken(refreshToken);
        if (token == null)
            return Unauthorized("Refresh token is invalid or expired");
        var user = await _userManager.FindByIdAsync(token.UserId);
        var newRefreshToken = await _authService.RotateToken(token);
        var accessToken = _tokenService.CreateToken(user);
        return MakeLoginResponse(accessToken, newRefreshToken, hybrid);
    }

    [HttpPost("signup")]
    public async Task<ActionResult<SignUpResponse>> SignUp(
        [FromBody] SignUpRequest request,
        [FromServices] UserManager<User> manager,
        [FromServices] IPublishEndpoint publishEndpoint)
    {
        var user = new User
        {
            UserName = request.UserName,
            Email = request.Email,
            EmailConfirmed = false
        };
        await publishEndpoint.Publish<InitialEmailVerification>(
            new InitialEmailVerification(request.Email, user.Id, user.UserName));
        var result = await manager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors);
        return new SignUpResponse { Detail = "User created successfully" };
    }

    [HttpGet("is-authorized")]
    public ActionResult<bool> IsAuthorized()
    {
        return User.HasClaim(claim => claim.Type == ClaimTypes.NameIdentifier);
    }

    public class LoginRequest
    {
        [Required] public string UserName { get; set; } = string.Empty;

        [Required] public string Password { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string? RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public int ExpiresAtTimestamp { get; set; }
    }

    public class SignUpRequest
    {
        [Required] [EmailAddress] public string Email { get; set; } = default!;

        [Required]
        [MinLength(1)]
        [MaxLength(100)]
        public string UserName { get; set; } = string.Empty;

        [Required] [MinLength(8)] public string Password { get; set; } = default!;
    }

    public class SignUpResponse
    {
        public string Detail { get; set; } = string.Empty;
    }

    public class RefreshTokenRequest
    {
        [Required] public string RefreshToken { get; set; } = string.Empty;
    }
}