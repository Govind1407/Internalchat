using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ChatAPI.Models;
using ChatAPI.Services;

namespace ChatAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        /// <summary>
        /// Register a new user
        /// </summary>
        /// <param name="createUserDto">User registration data</param>
        /// <returns>Authentication response with user data and JWT token</returns>
        [HttpPost("register")]
        public async Task<ActionResult<AuthResponseDto>> Register([FromBody] CreateUserDto createUserDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _authService.RegisterAsync(createUserDto);
                
                if (result == null)
                {
                    return BadRequest(new { error = "Username or email already exists" });
                }

                _logger.LogInformation("User registered successfully: {Username}", createUserDto.Username);
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user registration");
                return StatusCode(500, new { error = "Internal server error during registration" });
            }
        }

        /// <summary>
        /// Authenticate user and return JWT token
        /// </summary>
        /// <param name="loginDto">Login credentials</param>
        /// <returns>Authentication response with user data and JWT token</returns>
        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto loginDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _authService.LoginAsync(loginDto);
                
                if (result == null)
                {
                    return Unauthorized(new { error = "Invalid username/email or password" });
                }

                _logger.LogInformation("User logged in successfully: {UsernameOrEmail}", loginDto.UsernameOrEmail);
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user login");
                return StatusCode(500, new { error = "Internal server error during login" });
            }
        }

        /// <summary>
        /// Get current authenticated user information
        /// </summary>
        /// <returns>Current user data</returns>
        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<UserDto>> GetCurrentUser()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Unauthorized(new { error = "Invalid token" });
                }

                var user = await _authService.GetUserByIdAsync(userId.Value);
                
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current user");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Update current user profile
        /// </summary>
        /// <param name="updateUserDto">User update data</param>
        /// <returns>Updated user data</returns>
        [HttpPut("me")]
        [Authorize]
        public async Task<ActionResult<UserDto>> UpdateCurrentUser([FromBody] UpdateUserDto updateUserDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Unauthorized(new { error = "Invalid token" });
                }

                var result = await _authService.UpdateUserAsync(userId.Value, updateUserDto);
                
                if (result == null)
                {
                    return BadRequest(new { error = "Unable to update user. Username or email may already exist." });
                }

                _logger.LogInformation("User updated successfully: {UserId}", userId.Value);
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user");
                return StatusCode(500, new { error = "Internal server error during update" });
            }
        }

        /// <summary>
        /// Validate JWT token
        /// </summary>
        /// <param name="token">JWT token to validate</param>
        /// <returns>Token validation result</returns>
        [HttpPost("validate-token")]
        public async Task<ActionResult<object>> ValidateToken([FromBody] string token)
        {
            try
            {
                if (string.IsNullOrEmpty(token))
                {
                    return BadRequest(new { error = "Token is required" });
                }

                var isValid = await _authService.ValidateTokenAsync(token);
                
                return Ok(new { isValid, timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating token");
                return StatusCode(500, new { error = "Internal server error during token validation" });
            }
        }

        /// <summary>
        /// Refresh JWT token (logout and login again for now)
        /// </summary>
        /// <returns>Instructions for token refresh</returns>
        [HttpPost("refresh")]
        [Authorize]
        public ActionResult<object> RefreshToken()
        {
            // For simplicity, we're not implementing refresh tokens
            // In production, you would implement proper refresh token logic
            return Ok(new 
            { 
                message = "To refresh your token, please log in again",
                refreshEndpoint = "/api/auth/login"
            });
        }

        /// <summary>
        /// Logout user (client-side token removal)
        /// </summary>
        /// <returns>Logout confirmation</returns>
        [HttpPost("logout")]
        [Authorize]
        public ActionResult<object> Logout()
        {
            var userId = GetCurrentUserId();
            _logger.LogInformation("User logged out: {UserId}", userId);
            
            return Ok(new 
            { 
                message = "Logged out successfully. Please remove the token from client storage.",
                timestamp = DateTime.UtcNow
            });
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            return userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId) ? userId : null;
        }
    }
} 