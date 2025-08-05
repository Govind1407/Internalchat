using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ChatAPI.Data;
using ChatAPI.Models;
using BCrypt.Net;

namespace ChatAPI.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto?> LoginAsync(LoginDto loginDto);
        Task<AuthResponseDto?> RegisterAsync(CreateUserDto createUserDto);
        Task<UserDto?> GetUserByIdAsync(Guid userId);
        Task<UserDto?> UpdateUserAsync(Guid userId, UpdateUserDto updateDto);
        Task<bool> ValidateTokenAsync(string token);
        string GenerateJwtToken(User user);
    }

    public class AuthService : IAuthService
    {
        private readonly ChatDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            ChatDbContext context,
            IConfiguration configuration,
            ILogger<AuthService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<AuthResponseDto?> LoginAsync(LoginDto loginDto)
        {
            try
            {
                // Find user by username or email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => 
                        u.Username.ToLower() == loginDto.UsernameOrEmail.ToLower() ||
                        u.Email.ToLower() == loginDto.UsernameOrEmail.ToLower());

                if (user == null)
                {
                    _logger.LogWarning("Login attempt with invalid username/email: {UsernameOrEmail}",
                        loginDto.UsernameOrEmail);
                    return null;
                }

                // For demo purposes, if no password hash exists, allow login with any password
                // In production, always require proper password verification
                if (!string.IsNullOrEmpty(user.PasswordHash))
                {
                    if (!BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash))
                    {
                        _logger.LogWarning("Invalid password for user: {Username}", user.Username);
                        return null;
                    }
                }

                // Update last seen
                user.LastSeen = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                var token = GenerateJwtToken(user);
                var expiresAt = DateTime.UtcNow.AddDays(7); // Token expires in 7 days

                _logger.LogInformation("User {Username} logged in successfully", user.Username);

                return new AuthResponseDto
                {
                    User = MapToUserDto(user),
                    Token = token,
                    ExpiresAt = expiresAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for {UsernameOrEmail}", loginDto.UsernameOrEmail);
                throw;
            }
        }

        public async Task<AuthResponseDto?> RegisterAsync(CreateUserDto createUserDto)
        {
            try
            {
                // Check if username already exists
                var existingUserByUsername = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username.ToLower() == createUserDto.Username.ToLower());

                if (existingUserByUsername != null)
                {
                    _logger.LogWarning("Registration attempt with existing username: {Username}",
                        createUserDto.Username);
                    return null;
                }

                // Check if email already exists
                var existingUserByEmail = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == createUserDto.Email.ToLower());

                if (existingUserByEmail != null)
                {
                    _logger.LogWarning("Registration attempt with existing email: {Email}",
                        createUserDto.Email);
                    return null;
                }

                // Create new user
                var user = new User
                {
                    Username = createUserDto.Username,
                    Email = createUserDto.Email,
                    Avatar = createUserDto.Avatar ?? GenerateAvatarUrl(createUserDto.Username),
                    Bio = createUserDto.Bio,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow
                };

                // Hash password if provided
                if (!string.IsNullOrEmpty(createUserDto.Password))
                {
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(createUserDto.Password);
                }

                _context.Users.Add(user);

                // Add user to default chat room
                var defaultChatRoom = await _context.ChatRooms
                    .FirstOrDefaultAsync(cr => cr.Name == "General" && cr.Type == ChatRoomType.Public);

                if (defaultChatRoom != null)
                {
                    var userChatRoom = new UserChatRoom
                    {
                        UserId = user.Id,
                        ChatRoomId = defaultChatRoom.Id,
                        Role = UserRole.Member,
                        JoinedAt = DateTime.UtcNow
                    };

                    _context.UserChatRooms.Add(userChatRoom);
                }

                await _context.SaveChangesAsync();

                var token = GenerateJwtToken(user);
                var expiresAt = DateTime.UtcNow.AddDays(7); // Token expires in 7 days

                _logger.LogInformation("New user registered: {Username} ({Email})", user.Username, user.Email);

                return new AuthResponseDto
                {
                    User = MapToUserDto(user),
                    Token = token,
                    ExpiresAt = expiresAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for {Username}", createUserDto.Username);
                throw;
            }
        }

        public async Task<UserDto?> GetUserByIdAsync(Guid userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                return user != null ? MapToUserDto(user) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by ID: {UserId}", userId);
                throw;
            }
        }

        public async Task<UserDto?> UpdateUserAsync(Guid userId, UpdateUserDto updateDto)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                    return null;

                // Check if new username is already taken
                if (!string.IsNullOrEmpty(updateDto.Username) && updateDto.Username != user.Username)
                {
                    var existingUser = await _context.Users
                        .FirstOrDefaultAsync(u => u.Username.ToLower() == updateDto.Username.ToLower());

                    if (existingUser != null)
                    {
                        _logger.LogWarning("Update attempt with existing username: {Username}",
                            updateDto.Username);
                        return null;
                    }

                    user.Username = updateDto.Username;
                }

                // Check if new email is already taken
                if (!string.IsNullOrEmpty(updateDto.Email) && updateDto.Email != user.Email)
                {
                    var existingUser = await _context.Users
                        .FirstOrDefaultAsync(u => u.Email.ToLower() == updateDto.Email.ToLower());

                    if (existingUser != null)
                    {
                        _logger.LogWarning("Update attempt with existing email: {Email}",
                            updateDto.Email);
                        return null;
                    }

                    user.Email = updateDto.Email;
                }

                // Update other fields
                if (!string.IsNullOrEmpty(updateDto.Avatar))
                    user.Avatar = updateDto.Avatar;

                if (updateDto.Bio != null)
                    user.Bio = updateDto.Bio;

                user.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("User updated: {Username} ({UserId})", user.Username, userId);

                return MapToUserDto(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user: {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> ValidateTokenAsync(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(GetJwtSecret());

                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = GetJwtIssuer(),
                    ValidateAudience = true,
                    ValidAudience = GetJwtAudience(),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var validatedToken);
                
                return validatedToken != null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Token validation failed");
                return false;
            }
        }

        public string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(GetJwtSecret());

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Email, user.Email),
                new("avatar", user.Avatar ?? string.Empty),
                new("created_at", user.CreatedAt.ToString("O"))
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddDays(7),
                Issuer = GetJwtIssuer(),
                Audience = GetJwtAudience(),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private static UserDto MapToUserDto(User user)
        {
            return new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Avatar = user.Avatar,
                Bio = user.Bio,
                IsOnline = user.IsOnline,
                LastSeen = user.LastSeen,
                CreatedAt = user.CreatedAt
            };
        }

        private static string GenerateAvatarUrl(string username)
        {
            return $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(username)}&background=random&size=128";
        }

        private string GetJwtSecret()
        {
            return _configuration["Jwt:Secret"] ?? "your-super-secret-jwt-key-change-this-in-production!";
        }

        private string GetJwtIssuer()
        {
            return _configuration["Jwt:Issuer"] ?? "ChatAPI";
        }

        private string GetJwtAudience()
        {
            return _configuration["Jwt:Audience"] ?? "ChatApp";
        }
    }
} 