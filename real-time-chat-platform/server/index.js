const express = require('express');
const http = require('http');
const socketIo = require('socket.io');
const cors = require('cors');
const { v4: uuidv4 } = require('uuid');
const fs = require('fs').promises;
const path = require('path');

const app = express();
const server = http.createServer(app);
const io = socketIo(server, {
  cors: {
    origin: "http://localhost:3000",
    methods: ["GET", "POST"]
  }
});

app.use(cors());
app.use(express.json());

// In-memory storage (in production, use a proper database)
let users = new Map();
let messages = [];
let onlineUsers = new Map();

// Data persistence
const DATA_DIR = path.join(__dirname, 'data');
const MESSAGES_FILE = path.join(DATA_DIR, 'messages.json');
const USERS_FILE = path.join(DATA_DIR, 'users.json');

// Initialize data directory and load existing data
async function initializeData() {
  try {
    await fs.mkdir(DATA_DIR, { recursive: true });
    
    // Load messages
    try {
      const messagesData = await fs.readFile(MESSAGES_FILE, 'utf8');
      messages = JSON.parse(messagesData);
    } catch (error) {
      console.log('No existing messages file, starting fresh');
    }
    
    // Load users
    try {
      const usersData = await fs.readFile(USERS_FILE, 'utf8');
      const usersArray = JSON.parse(usersData);
      users = new Map(usersArray);
    } catch (error) {
      console.log('No existing users file, starting fresh');
    }
  } catch (error) {
    console.error('Error initializing data:', error);
  }
}

// Save data to files
async function saveMessages() {
  try {
    await fs.writeFile(MESSAGES_FILE, JSON.stringify(messages, null, 2));
  } catch (error) {
    console.error('Error saving messages:', error);
  }
}

async function saveUsers() {
  try {
    const usersArray = Array.from(users.entries());
    await fs.writeFile(USERS_FILE, JSON.stringify(usersArray, null, 2));
  } catch (error) {
    console.error('Error saving users:', error);
  }
}

// API Routes
app.get('/api/health', (req, res) => {
  res.json({ status: 'ok', timestamp: new Date().toISOString() });
});

// User registration/login
app.post('/api/auth/register', async (req, res) => {
  const { username, email } = req.body;
  
  if (!username || !email) {
    return res.status(400).json({ error: 'Username and email are required' });
  }
  
  // Check if user already exists
  const existingUser = Array.from(users.values()).find(user => 
    user.username === username || user.email === email
  );
  
  if (existingUser) {
    return res.status(400).json({ error: 'User already exists' });
  }
  
  const userId = uuidv4();
  const user = {
    id: userId,
    username,
    email,
    createdAt: new Date().toISOString(),
    avatar: `https://ui-avatars.com/api/?name=${encodeURIComponent(username)}&background=random`
  };
  
  users.set(userId, user);
  await saveUsers();
  
  res.json({ user, token: userId }); // Simple token for demo
});

app.post('/api/auth/login', (req, res) => {
  const { username } = req.body;
  
  if (!username) {
    return res.status(400).json({ error: 'Username is required' });
  }
  
  const user = Array.from(users.values()).find(u => u.username === username);
  
  if (!user) {
    return res.status(404).json({ error: 'User not found' });
  }
  
  res.json({ user, token: user.id });
});

// Get messages
app.get('/api/messages', (req, res) => {
  const { limit = 50, offset = 0 } = req.query;
  const paginatedMessages = messages
    .slice(-limit - offset, messages.length - offset)
    .map(msg => ({
      ...msg,
      user: users.get(msg.userId) || { username: 'Unknown User' }
    }));
  
  res.json({
    messages: paginatedMessages,
    total: messages.length,
    hasMore: messages.length > parseInt(limit) + parseInt(offset)
  });
});

// Get online users
app.get('/api/users/online', (req, res) => {
  const onlineUsersList = Array.from(onlineUsers.values());
  res.json(onlineUsersList);
});

// Get all users
app.get('/api/users', (req, res) => {
  const usersList = Array.from(users.values()).map(user => ({
    id: user.id,
    username: user.username,
    avatar: user.avatar,
    isOnline: onlineUsers.has(user.id)
  }));
  res.json(usersList);
});

// Socket.IO connection handling
io.on('connection', (socket) => {
  console.log('New client connected:', socket.id);
  
  // User joins
  socket.on('join', (userData) => {
    if (!userData || !userData.id) {
      socket.emit('error', { message: 'Invalid user data' });
      return;
    }
    
    const user = users.get(userData.id);
    if (!user) {
      socket.emit('error', { message: 'User not found' });
      return;
    }
    
    socket.userId = userData.id;
    socket.user = user;
    
    // Add to online users
    onlineUsers.set(userData.id, {
      ...user,
      socketId: socket.id,
      lastSeen: new Date().toISOString()
    });
    
    // Join user to general room
    socket.join('general');
    
    // Notify others that user is online
    socket.broadcast.emit('user_online', {
      user: user,
      timestamp: new Date().toISOString()
    });
    
    // Send current online users to the new user
    socket.emit('online_users', Array.from(onlineUsers.values()));
    
    console.log(`User ${user.username} joined`);
  });
  
  // Handle new message
  socket.on('send_message', async (messageData) => {
    if (!socket.userId || !messageData.content) {
      socket.emit('error', { message: 'Invalid message data' });
      return;
    }
    
    const user = users.get(socket.userId);
    if (!user) {
      socket.emit('error', { message: 'User not found' });
      return;
    }
    
    const message = {
      id: uuidv4(),
      content: messageData.content.trim(),
      userId: socket.userId,
      timestamp: new Date().toISOString(),
      type: messageData.type || 'text'
    };
    
    messages.push(message);
    await saveMessages();
    
    // Send message to all users in the room
    const messageWithUser = {
      ...message,
      user: user
    };
    
    io.to('general').emit('new_message', messageWithUser);
    
    // Send notification to offline users (in a real app, you'd use push notifications)
    const offlineUsers = Array.from(users.values()).filter(u => 
      u.id !== socket.userId && !onlineUsers.has(u.id)
    );
    
    console.log(`Message from ${user.username}: ${message.content}`);
  });
  
  // Handle typing indicators
  socket.on('typing_start', () => {
    if (socket.user) {
      socket.broadcast.to('general').emit('user_typing', {
        user: socket.user,
        isTyping: true
      });
    }
  });
  
  socket.on('typing_stop', () => {
    if (socket.user) {
      socket.broadcast.to('general').emit('user_typing', {
        user: socket.user,
        isTyping: false
      });
    }
  });
  
  // Handle disconnection
  socket.on('disconnect', () => {
    if (socket.userId) {
      // Remove from online users
      onlineUsers.delete(socket.userId);
      
      // Notify others that user is offline
      if (socket.user) {
        socket.broadcast.emit('user_offline', {
          user: socket.user,
          timestamp: new Date().toISOString()
        });
        
        console.log(`User ${socket.user.username} disconnected`);
      }
    }
    
    console.log('Client disconnected:', socket.id);
  });
});

// Initialize data and start server
initializeData().then(() => {
  const PORT = process.env.PORT || 5000;
  server.listen(PORT, () => {
    console.log(`Server running on port ${PORT}`);
    console.log(`CORS enabled for http://localhost:3000`);
  });
});

// Graceful shutdown
process.on('SIGTERM', async () => {
  console.log('SIGTERM received, shutting down gracefully');
  await saveMessages();
  await saveUsers();
  server.close(() => {
    console.log('Server closed');
    process.exit(0);
  });
}); 