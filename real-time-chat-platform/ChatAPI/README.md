# Real-time Chat API - C# ASP.NET Core

A comprehensive real-time chat and messaging API built with ASP.NET Core 8, SignalR, Entity Framework Core, and JWT authentication. Features real-time messaging, user presence, notifications, and comprehensive chat management.

## 🚀 Features

### Core Features
- **Real-time Messaging**: Instant messaging using SignalR
- **User Authentication**: JWT-based authentication with secure token handling
- **User Presence**: Real-time online/offline status tracking
- **Typing Indicators**: Live typing status updates
- **Message History**: Persistent message storage with pagination
- **Chat Rooms**: Support for public, private, and group chat rooms
- **User Management**: Complete user registration, login, and profile management

### Advanced Features
- **Real-time Notifications**: Comprehensive notification system with multiple types
- **Message Reactions**: Emoji reactions to messages
- **Message Mentions**: @username mentions with notifications
- **File Attachments**: Support for file uploads and sharing
- **Message Threading**: Reply to specific messages
- **User Roles**: Admin, moderator, and member roles in chat rooms
- **Notification Settings**: Granular notification preferences per user
- **Do Not Disturb**: Configurable quiet hours

### Technical Features
- **SignalR Hubs**: Real-time bidirectional communication
- **Entity Framework Core**: Robust data persistence
- **JWT Authentication**: Secure API authentication
- **Swagger/OpenAPI**: Comprehensive API documentation
- **Health Checks**: Built-in health monitoring
- **Logging**: Structured logging with Serilog
- **CORS Support**: Cross-origin resource sharing configured for Angular

## 📁 Project Structure

```
ChatAPI/
├── Controllers/              # API Controllers
│   ├── AuthController.cs     # Authentication endpoints
│   ├── UsersController.cs    # User management
│   ├── ChatRoomsController.cs # Chat room management
│   ├── MessagesController.cs # Message operations
│   └── NotificationsController.cs # Notification management
├── Data/
│   └── ChatDbContext.cs     # Entity Framework DbContext
├── Hubs/
│   └── ChatHub.cs           # SignalR hub for real-time communication
├── Models/                  # Data models and DTOs
│   ├── User.cs              # User models and DTOs
│   ├── Message.cs           # Message models and DTOs
│   ├── ChatRoom.cs          # Chat room models and DTOs
│   └── Notification.cs      # Notification models and DTOs
├── Services/                # Business logic services
│   ├── IAuthService.cs      # Authentication service interface
│   ├── AuthService.cs       # Authentication service implementation
│   ├── INotificationService.cs # Notification service interface
│   └── NotificationService.cs  # Notification service implementation
├── Program.cs               # Application entry point and configuration
├── ChatAPI.csproj          # Project file with dependencies
└── README.md               # This documentation
```

## 🛠️ Setup and Installation

### Prerequisites
- **.NET 8 SDK** or later
- **SQL Server** (optional - uses In-Memory database by default)
- **Visual Studio 2022** or **VS Code** with C# extension

### Getting Started

1. **Clone or navigate to the project directory:**
   ```bash
   cd real-time-chat-platform/ChatAPI
   ```

2. **Restore NuGet packages:**
   ```bash
   dotnet restore
   ```

3. **Run the application:**
   ```bash
   dotnet run
   ```

4. **Access the API:**
   - **Swagger UI**: https://localhost:7000 (or http://localhost:5000)
   - **SignalR Hub**: wss://localhost:7000/chatHub
   - **Health Check**: https://localhost:7000/health

### Configuration

#### appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.AspNetCore.SignalR": "Debug",
      "Microsoft.AspNetCore.Http.Connections": "Debug"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=ChatDB;Trusted_Connection=true;MultipleActiveResultSets=true"
  },
  "Jwt": {
    "Secret": "your-super-secret-jwt-key-change-this-in-production!",
    "Issuer": "ChatAPI",
    "Audience": "ChatApp",
    "ExpiryInDays": 7
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/chatapi-.txt",
          "rollingInterval": "Day"
        }
      }
    ]
  }
}
```

#### Database Configuration

**In-Memory Database (Default):**
No additional configuration needed. Perfect for development and testing.

**SQL Server:**
Update the `ConnectionStrings:DefaultConnection` in `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=ChatDB;Trusted_Connection=true;MultipleActiveResultSets=true"
  }
}
```

**Entity Framework Migrations (for SQL Server):**
```bash
# Add migration
dotnet ef migrations add InitialCreate

# Update database
dotnet ef database update
```

## 🔧 API Endpoints

### Authentication
- `POST /api/auth/register` - Register new user
- `POST /api/auth/login` - User login
- `GET /api/auth/me` - Get current user info

### Users
- `GET /api/users` - Get all users
- `GET /api/users/{id}` - Get user by ID
- `PUT /api/users/{id}` - Update user profile
- `GET /api/users/online` - Get online users

### Chat Rooms
- `GET /api/chatrooms` - Get chat rooms
- `POST /api/chatrooms` - Create chat room
- `GET /api/chatrooms/{id}` - Get chat room details
- `PUT /api/chatrooms/{id}` - Update chat room
- `DELETE /api/chatrooms/{id}` - Delete chat room
- `POST /api/chatrooms/{id}/members` - Add member to chat room
- `DELETE /api/chatrooms/{id}/members/{userId}` - Remove member

### Messages
- `GET /api/messages` - Get messages (with filtering)
- `POST /api/messages` - Send message
- `PUT /api/messages/{id}` - Edit message
- `DELETE /api/messages/{id}` - Delete message
- `POST /api/messages/{id}/reactions` - Add reaction
- `DELETE /api/messages/{id}/reactions/{reactionId}` - Remove reaction

### Notifications
- `GET /api/notifications` - Get user notifications
- `POST /api/notifications/mark-read` - Mark notifications as read
- `DELETE /api/notifications/{id}` - Delete notification
- `GET /api/notifications/settings` - Get notification settings
- `PUT /api/notifications/settings` - Update notification settings
- `GET /api/notifications/stats` - Get notification statistics

### Real-time Events (SignalR)
- `JoinChatRoom(chatRoomId)` - Join a chat room
- `LeaveChatRoom(chatRoomId)` - Leave a chat room
- `SendMessage(chatRoomId, content, replyToMessageId?)` - Send a message
- `StartTyping(chatRoomId)` - Start typing indicator
- `StopTyping(chatRoomId)` - Stop typing indicator

## 📡 SignalR Hub Events

### Client → Server Events
```typescript
// Join a chat room
connection.invoke("JoinChatRoom", chatRoomId);

// Send a message
connection.invoke("SendMessage", chatRoomId, messageContent, replyToMessageId);

// Typing indicators
connection.invoke("StartTyping", chatRoomId);
connection.invoke("StopTyping", chatRoomId);
```

### Server → Client Events
```typescript
// New message received
connection.on("NewMessage", (message) => {
    // Handle new message
});

// User online/offline
connection.on("UserOnline", (user) => {
    // Handle user coming online
});

connection.on("UserOffline", (user) => {
    // Handle user going offline
});

// Typing indicators
connection.on("UserTyping", (typingData) => {
    // Handle typing indicator
});

// Notifications
connection.on("Notification", (notification) => {
    // Handle real-time notification
});

// Mentions
connection.on("Mention", (mentionData) => {
    // Handle being mentioned
});
```

## 🔐 Authentication

### JWT Token Format
```json
{
  "sub": "user-id-guid",
  "name": "username",
  "email": "user@example.com",
  "avatar": "avatar-url",
  "created_at": "2024-01-01T00:00:00Z",
  "iss": "ChatAPI",
  "aud": "ChatApp",
  "exp": 1640995200
}
```

### Using JWT in Requests
```http
Authorization: Bearer <your-jwt-token>
```

### SignalR Authentication
```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/chatHub", {
        accessTokenFactory: () => {
            return localStorage.getItem("jwt-token");
        }
    })
    .build();
```

## 🔔 Notification System

### Notification Types
- **Message**: New message notifications
- **Mention**: User mention notifications
- **UserOnline/UserOffline**: User presence notifications
- **PrivateMessage**: Direct message notifications
- **System**: System-generated notifications
- **ChatRoomInvite**: Chat room invitation notifications
- **Error/Success/Warning/Info**: Status notifications

### Notification Priorities
- **Low**: Minor updates (user status changes)
- **Normal**: Regular messages
- **High**: Important messages (mentions, private messages)
- **Urgent**: Critical notifications requiring immediate attention

### Real-time Delivery
Notifications are delivered in real-time via SignalR to all connected clients for the target user.

## 🧪 Testing

### Running Tests
```bash
# Run unit tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Testing SignalR
Use the built-in Swagger UI to test REST API endpoints and SignalR connection:

1. **Open Swagger UI**: https://localhost:7000
2. **Authenticate**: Use the `/api/auth/login` endpoint to get a JWT token
3. **Test SignalR**: Use browser console or testing tools to connect to `/chatHub`

### Example SignalR Test (Browser Console)
```javascript
// Connect to SignalR hub
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/chatHub?access_token=" + yourJwtToken)
    .build();

// Start connection
connection.start().then(() => {
    console.log("Connected to ChatHub");
    
    // Join a chat room
    connection.invoke("JoinChatRoom", "room-id-here");
    
    // Send a message
    connection.invoke("SendMessage", "room-id-here", "Hello World!");
});

// Listen for messages
connection.on("NewMessage", (message) => {
    console.log("New message:", message);
});
```

## 🚀 Deployment

### Development
```bash
dotnet run --environment Development
```

### Production

#### Using Docker
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["ChatAPI.csproj", "."]
RUN dotnet restore "ChatAPI.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "ChatAPI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ChatAPI.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ChatAPI.dll"]
```

#### Using IIS
1. Publish the application:
   ```bash
   dotnet publish -c Release -o ./publish
   ```
2. Deploy to IIS with ASP.NET Core hosting bundle installed
3. Configure application pool to use "No Managed Code"

#### Environment Variables
```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:80;https://+:443
JWT__SECRET=your-production-secret-key
ConnectionStrings__DefaultConnection=your-production-connection-string
```

## 📊 Monitoring and Logging

### Health Checks
- **Endpoint**: `/health`
- **Checks**: Database connectivity, service availability

### Logging
- **Serilog**: Structured logging to console and files
- **Log Files**: `logs/chatapi-{date}.txt`
- **Log Levels**: Information, Warning, Error, Debug

### Metrics
Monitor the following metrics in production:
- Active SignalR connections
- Message throughput
- Database query performance
- Memory usage
- Response times

## 🔧 Customization

### Adding New Notification Types
1. Add new enum value to `NotificationType`
2. Update `NotificationService` with new methods
3. Add corresponding settings to `NotificationSettings`
4. Update client-side handling

### Extending Message Types
1. Add new enum value to `MessageType`
2. Update message handling in `ChatHub`
3. Add client-side rendering support

### Adding New Chat Room Types
1. Add new enum value to `ChatRoomType`
2. Update permissions and access control logic
3. Add UI support for new room type

## 🤝 Integration with Angular Frontend

### Connection Setup
```typescript
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';

const connection = new HubConnectionBuilder()
  .withUrl('https://localhost:7000/chatHub', {
    accessTokenFactory: () => this.authService.getToken()
  })
  .build();
```

### Service Integration
```typescript
// Update Angular service to use new API endpoints
const API_BASE_URL = 'https://localhost:7000/api';

// Authentication
POST ${API_BASE_URL}/auth/login
POST ${API_BASE_URL}/auth/register

// Messages
GET ${API_BASE_URL}/messages?chatRoomId={id}&page={page}
POST ${API_BASE_URL}/messages

// Notifications
GET ${API_BASE_URL}/notifications?page={page}&type={type}
PUT ${API_BASE_URL}/notifications/settings
```

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🙏 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Submit a pull request

## 📞 Support

For questions, issues, or contributions:
- **GitHub Issues**: Create an issue for bug reports or feature requests
- **Documentation**: Check this README and inline code comments
- **API Documentation**: Use Swagger UI at the root URL when running the application

---

**Built with ❤️ using ASP.NET Core 8, SignalR, and Entity Framework Core** 