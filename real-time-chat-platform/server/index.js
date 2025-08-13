const express = require('express');
const http = require('http');
const socketIo = require('socket.io');
const cors = require('cors');
const jwt = require('jsonwebtoken');
const { v4: uuidv4 } = require('uuid');
const fs = require('fs').promises;
const path = require('path');
const multer = require('multer');
const mime = require('mime-types');
const bcrypt = require('bcryptjs');
const ChatDatabase = require('./database');

const app = express();
const server = http.createServer(app);
const io = socketIo(server, {
  cors: {
    origin: ["http://localhost:3000", "http://localhost:4200"], // Added Angular dev server port
    methods: ["GET", "POST"],
    credentials: true
  }
});

// Data persistence directories - define these first
const DATA_DIR = path.join(__dirname, 'data');
const UPLOADS_DIR = path.join(__dirname, 'uploads');

// JWT configuration
const JWT_SECRET = process.env.JWT_SECRET || 'your-super-secret-jwt-key-change-in-production';
const JWT_EXPIRY = process.env.JWT_EXPIRY || '24h'; // Token expires in 24 hours

// Initialize database
let db;

app.use(cors({
  origin: ["http://localhost:3000", "http://localhost:4200"],
  credentials: true
}));
app.use(express.json());

// Middleware to authenticate requests
const authenticateUser = (req, res, next) => {
  const authToken = req.headers['authorization'];
  
  if (!authToken) {
    return res.status(401).json({ error: 'Authentication token required' });
  }
  
  // Extract token from "Bearer TOKEN" format
  const token = authToken.startsWith('Bearer ') ? authToken.slice(7) : authToken;
  
  try {
    // Verify JWT token
    const decoded = jwt.verify(token, JWT_SECRET);
    
    // Check if user still exists
    const user = db.getUserById(decoded.userId);
    if (!user) {
      return res.status(401).json({ error: 'User not found' });
    }
    
    req.user = user;
    req.userId = decoded.userId;
    next();
  } catch (error) {
    if (error.name === 'TokenExpiredError') {
      return res.status(401).json({ error: 'Token expired', code: 'TOKEN_EXPIRED' });
    } else if (error.name === 'JsonWebTokenError') {
      return res.status(401).json({ error: 'Invalid token', code: 'INVALID_TOKEN' });
    } else {
      return res.status(401).json({ error: 'Authentication failed' });
    }
  }
};

// Serve uploaded files statically with proper headers
app.use('/uploads', (req, res, next) => {
  res.header('Access-Control-Allow-Origin', '*');
  res.header('Access-Control-Allow-Methods', 'GET');
  res.header('Access-Control-Allow-Headers', 'Origin, X-Requested-With, Content-Type, Accept');
  next();
}, express.static(UPLOADS_DIR));

// In-memory storage for real-time features
let onlineUsers = new Map();

// Configure multer for file uploads
const storage = multer.diskStorage({
  destination: function (req, file, cb) {
    cb(null, UPLOADS_DIR);
  },
  filename: function (req, file, cb) {
    const uniqueSuffix = Date.now() + '-' + Math.round(Math.random() * 1E9);
    const originalName = Buffer.from(file.originalname, 'latin1').toString('utf8');
    const sanitizedName = originalName.replace(/[^a-zA-Z0-9.\-_]/g, '_');
    cb(null, `${uniqueSuffix}-${sanitizedName}`);
  }
});

const upload = multer({
  storage: storage,
  limits: {
    fileSize: 50 * 1024 * 1024 // 50MB limit
  },
  fileFilter: function (req, file, cb) {
    // Allow all file types but could be restricted for security
    cb(null, true);
  }
});

// Initialize data directory and database
async function initializeData() {
  try {
    await fs.mkdir(DATA_DIR, { recursive: true });
    await fs.mkdir(UPLOADS_DIR, { recursive: true });
    
    // Initialize database
    db = new ChatDatabase();
    console.log('Database initialized successfully');
    
    // Validate and fix any groups with invalid admins
    const fixedGroups = db.validateAndFixGroupAdmins();
    if (fixedGroups.length > 0) {
      console.log(`Fixed ${fixedGroups.length} groups with invalid admins`);
    }
    
    // Set up periodic cleanup of old notifications (run every 24 hours)
    setInterval(() => {
      try {
        const result = db.cleanupOldNotifications();
        if (result.changes > 0) {
          console.log(`Cleaned up ${result.changes} old notifications`);
        }
      } catch (error) {
        console.error('Error during notification cleanup:', error);
      }
    }, 24 * 60 * 60 * 1000); // 24 hours
    
  } catch (error) {
    console.error('Error initializing data:', error);
  }
}

// API Routes
app.get('/api/health', (req, res) => {
  res.json({ status: 'ok', timestamp: new Date().toISOString() });
});

// User registration/login
app.post('/api/auth/register', async (req, res) => {
  const { username, email, password } = req.body;
  
  if (!username || !email || !password) {
    return res.status(400).json({ error: 'Username, email, and password are required' });
  }
  
  if (password.length < 6) {
    return res.status(400).json({ error: 'Password must be at least 6 characters long' });
  }
  
  // Check if user already exists
  const existingUserByUsername = db.getUserByUsername(username);
  const existingUserByEmail = db.getUserByEmail(email);
  
  if (existingUserByUsername || existingUserByEmail) {
    return res.status(400).json({ error: 'User already exists' });
  }
  
  try {
    // Hash the password
    const saltRounds = 10;
    const hashedPassword = await bcrypt.hash(password, saltRounds);
    
    const userId = uuidv4();
    const user = {
      id: userId,
      username,
      email,
      password: hashedPassword,
      createdAt: new Date().toISOString(),
      avatar: `https://ui-avatars.com/api/?name=${encodeURIComponent(username)}&background=random`
    };
    
    db.createUser(user);
    
    // Generate JWT token
    const token = jwt.sign(
      { userId: user.id, username: user.username },
      JWT_SECRET,
      { expiresIn: JWT_EXPIRY }
    );
    
    // Don't send password in response
    const userResponse = {
      id: user.id,
      username: user.username,
      email: user.email,
      createdAt: user.createdAt,
      avatar: user.avatar
    };
    
    res.json({ user: userResponse, token });
  } catch (error) {
    console.error('Error registering user:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
});

app.post('/api/auth/login', async (req, res) => {
  const { username, password } = req.body;
  
  if (!username || !password) {
    return res.status(400).json({ error: 'Username and password are required' });
  }
  
  const user = db.getUserByUsername(username);
  
  if (!user) {
    return res.status(401).json({ error: 'Invalid username or password' });
  }
  
  try {
    // Check password
    const isValidPassword = await bcrypt.compare(password, user.password);
    
    if (!isValidPassword) {
      return res.status(401).json({ error: 'Invalid username or password' });
    }
    
    // Generate JWT token
    const token = jwt.sign(
      { userId: user.id, username: user.username },
      JWT_SECRET,
      { expiresIn: JWT_EXPIRY }
    );
    
    // Don't send password in response
    const userResponse = {
      id: user.id,
      username: user.username,
      email: user.email,
      createdAt: user.created_at,
      avatar: user.avatar
    };
    
    res.json({ user: userResponse, token });
  } catch (error) {
    console.error('Error logging in user:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
});

// Token verification endpoint
app.post('/api/auth/verify', (req, res) => {
  const authToken = req.headers['authorization'];
  
  if (!authToken) {
    return res.status(401).json({ valid: false, error: 'No token provided' });
  }
  
  const token = authToken.startsWith('Bearer ') ? authToken.slice(7) : authToken;
  
  try {
    const decoded = jwt.verify(token, JWT_SECRET);
    const user = db.getUserById(decoded.userId);
    
    if (!user) {
      return res.status(401).json({ valid: false, error: 'User not found' });
    }
    
    res.json({ 
      valid: true, 
      user: {
        id: user.id,
        username: user.username,
        email: user.email,
        createdAt: user.created_at,
        avatar: user.avatar
      },
      expiresAt: decoded.exp * 1000 // Convert to milliseconds
    });
  } catch (error) {
    if (error.name === 'TokenExpiredError') {
      return res.status(401).json({ valid: false, error: 'Token expired', code: 'TOKEN_EXPIRED' });
    } else if (error.name === 'JsonWebTokenError') {
      return res.status(401).json({ valid: false, error: 'Invalid token', code: 'INVALID_TOKEN' });
    } else {
      return res.status(401).json({ valid: false, error: 'Token verification failed' });
    }
  }
});

// Get messages
app.get('/api/messages', (req, res) => {
  const { limit = 50, offset = 0 } = req.query;
  const messages = db.getGeneralMessages(parseInt(limit), parseInt(offset));
  const total = db.getTotalMessages('general');
  
  // Reverse to get chronological order (newest last)
  const paginatedMessages = messages.reverse().map(msg => ({
    id: msg.id,
    content: msg.content,
    userId: msg.user_id,
    timestamp: msg.timestamp,
    type: msg.message_type,
    attachments: db.getMessageFiles(msg.id),
    user: {
      id: msg.user_id,
      username: msg.username,
      avatar: msg.avatar
    }
  }));
  
  res.json({
    messages: paginatedMessages,
    total: total,
    hasMore: total > parseInt(limit) + parseInt(offset)
  });
});

// Get online users
app.get('/api/users/online', (req, res) => {
  const onlineUsersList = Array.from(onlineUsers.values());
  res.json(onlineUsersList);
});

// Get all users
app.get('/api/users', (req, res) => {
  const users = db.getAllUsers();
  const usersList = users.map(user => ({
    id: user.id,
    username: user.username,
    avatar: user.avatar,
    isOnline: onlineUsers.has(user.id)
  }));
  res.json(usersList);
});

// Helper function to format group data for client
function formatGroupForClient(group, onlineUsers) {
  return {
    id: group.id,
    name: group.name,
    description: group.description,
    admin: group.admin_id,
    adminDetails: group.admin,
    avatar: group.avatar,
    createdAt: group.created_at,
    updatedAt: group.updated_at,
    members: group.members.map(m => m.id),
    memberDetails: group.members.map(m => ({
      id: m.id,
      username: m.username,
      avatar: m.avatar,
      isOnline: onlineUsers.has(m.id),
      joinedAt: m.joined_at,
      isAdmin: m.id === group.admin_id
    }))
  };
}

// Helper function to add notification
async function addNotification(userId, notification) {
  try {
    const notificationData = {
      id: notification.id || uuidv4(), // Use provided ID or generate new one
      userId: userId,
      type: notification.type,
      title: notification.title,
      message: notification.message,
      senderId: notification.senderId || (notification.sender ? notification.sender.id : null),
      groupId: notification.groupId || (notification.group ? notification.group.id : null),
      messageId: notification.messageId || null,
      timestamp: notification.timestamp
    };
    
    const result = db.createNotification(notificationData);
    console.log(`Notification stored in DB for user ${userId}: ${notificationData.title}`);
    return notificationData; // Return the stored notification data
  } catch (error) {
    console.error('Error adding notification:', error);
    throw error;
  }
}

// Get private messages between two users
app.get('/api/messages/private/:userId', authenticateUser, (req, res) => {
  const { userId } = req.params;
  const currentUserId = req.userId;
  
  const messages = db.getPrivateMessages(currentUserId, userId);
  
  const messagesWithUsers = messages.map(msg => ({
    id: msg.id,
    content: msg.content,
    userId: msg.user_id,
    recipientId: msg.recipient_id,
    timestamp: msg.timestamp,
    type: msg.message_type,
    isPrivate: true,
    attachments: db.getMessageFiles(msg.id),
    user: {
      id: msg.user_id,
      username: msg.username,
      avatar: msg.avatar
    },
    recipient: db.getUserById(msg.recipient_id)
  }));
  
  res.json({
    messages: messagesWithUsers,
    total: messagesWithUsers.length
  });
});

// Get all conversations for a user
app.get('/api/conversations', authenticateUser, (req, res) => {
  const currentUserId = req.userId;
  
  const conversations = db.getUserConversations(currentUserId);
  
  const userConversations = conversations.map(conv => {
    const otherUser = db.getUserById(conv.other_user_id);
    return {
      userId: conv.other_user_id,
      user: {
        id: otherUser.id,
        username: otherUser.username,
        avatar: otherUser.avatar
      },
      lastMessage: {
        content: conv.last_message_content,
        timestamp: conv.last_message_time,
        user: otherUser
      },
      unreadCount: 0, // TODO: Implement unread count tracking
      isOnline: onlineUsers.has(conv.other_user_id)
    };
  });
  
  res.json(userConversations);
});

// Get notifications for a user
app.get('/api/notifications', authenticateUser, (req, res) => {
  const currentUserId = req.userId; // From authenticated user
  const { limit = 100, offset = 0 } = req.query;
  
  console.log(`Getting notifications for user: ${currentUserId}`);
  
  const userNotifications = db.getUserNotifications(currentUserId, parseInt(limit), parseInt(offset));
  const unreadCount = db.getUnreadNotificationCount(currentUserId);
  
  console.log(`Found ${userNotifications.length} notifications for user ${currentUserId}, ${unreadCount} unread`);
  
  res.json({
    notifications: userNotifications.map(notif => ({
      id: notif.id,
      type: notif.type,
      title: notif.title,
      message: notif.message,
      sender: notif.sender_id ? {
        id: notif.sender_id,
        username: notif.sender_username,
        avatar: notif.sender_avatar
      } : null,
      group: notif.group_id ? {
        id: notif.group_id,
        name: notif.group_name
      } : null,
      timestamp: notif.timestamp,
      read: Boolean(notif.is_read),
      messageId: notif.message_id
    })),
    count: userNotifications.length,
    unreadCount: unreadCount
  });
});

// Mark notification as read
app.put('/api/notifications/:notificationId/read', authenticateUser, (req, res) => {
  const { notificationId } = req.params;
  const currentUserId = req.userId; // From authenticated user
  
  console.log(`Attempting to mark notification as read: ${notificationId} for user: ${currentUserId}`);
  
  // First check if notification exists at all
  const notification = db.getNotificationById(notificationId);
  if (!notification) {
    console.log(`Notification ${notificationId} not found in database - it may have been already processed`);
    // Return success even if notification doesn't exist (it might have been batch processed)
    const unreadCount = db.getUnreadNotificationCount(currentUserId);
    return res.json({ 
      success: true, 
      message: 'Notification already processed',
      unreadCount,
      alreadyProcessed: true
    });
  }
  
  console.log(`Found notification: ${JSON.stringify(notification)}`);
  
  // Check if notification belongs to current user
  if (notification.user_id !== currentUserId) {
    console.log(`Notification ${notificationId} belongs to user ${notification.user_id}, but current user is ${currentUserId}`);
    return res.status(403).json({ error: 'Notification does not belong to user' });
  }
  
  // Check if notification is already read
  if (notification.is_read) {
    console.log(`Notification ${notificationId} is already marked as read`);
    const unreadCount = db.getUnreadNotificationCount(currentUserId);
    return res.json({ 
      success: true, 
      message: 'Notification already marked as read',
      unreadCount,
      alreadyRead: true
    });
  }
  
  const result = db.markNotificationAsRead(notificationId, currentUserId);
  console.log(`Mark as read result: ${JSON.stringify(result)}`);
  
  if (result.changes === 0) {
    console.log(`No changes made when marking notification ${notificationId} as read`);
    // Return success even if no changes (might have been marked by another process)
    const unreadCount = db.getUnreadNotificationCount(currentUserId);
    return res.json({ 
      success: true, 
      message: 'Notification already processed',
      unreadCount,
      noChanges: true
    });
  }
  
  const unreadCount = db.getUnreadNotificationCount(currentUserId);
  console.log(`Successfully marked notification ${notificationId} as read. Remaining unread: ${unreadCount}`);
  
  res.json({ success: true, unreadCount });
});

// Mark all notifications as read
app.put('/api/notifications/read-all', authenticateUser, (req, res) => {
  const currentUserId = req.userId; // From authenticated user
  
  db.markAllNotificationsAsRead(currentUserId);
  res.json({ success: true, unreadCount: 0 });
});

// Batch mark notifications as read
app.put('/api/notifications/batch-read', authenticateUser, (req, res) => {
  try {
    const { notificationIds } = req.body;
    const currentUserId = req.userId; // From authenticated user

    console.log(`Batch marking notifications for user ${currentUserId}:`, notificationIds);

    if (!Array.isArray(notificationIds) || notificationIds.length === 0) {
      return res.status(400).json({ error: 'Invalid notification IDs array' });
    }

    // Mark all specified notifications as read
    let markedCount = 0;
    const failedIds = [];
    
    notificationIds.forEach(notificationId => {
      // Check if notification exists and belongs to user
      const notification = db.getNotificationById(notificationId);
      if (!notification) {
        console.log(`Batch: Notification ${notificationId} not found`);
        failedIds.push(notificationId);
        return;
      }
      
      if (notification.user_id !== currentUserId) {
        console.log(`Batch: Notification ${notificationId} belongs to different user`);
        failedIds.push(notificationId);
        return;
      }
      
      const result = db.markNotificationAsRead(notificationId, currentUserId);
      if (result.changes > 0) {
        markedCount++;
        console.log(`Batch: Successfully marked notification ${notificationId} as read`);
      } else {
        console.log(`Batch: Failed to mark notification ${notificationId} as read`);
        failedIds.push(notificationId);
      }
    });
    
    const unreadCount = db.getUnreadNotificationCount(currentUserId);
    
    console.log(`Batch operation completed: ${markedCount} marked, ${failedIds.length} failed`);
    
    res.json({ 
      success: true, 
      message: `${markedCount} notifications marked as read`,
      unreadCount,
      markedCount,
      failedCount: failedIds.length,
      failedIds
    });
  } catch (error) {
    console.error('Error batch marking notifications as read:', error);
    res.status(500).json({ error: 'Failed to batch mark notifications as read' });
  }
});

// Delete a specific notification
app.delete('/api/notifications/:notificationId', authenticateUser, (req, res) => {
  const { notificationId } = req.params;
  const currentUserId = req.userId; // From authenticated user
  
  db.deleteNotification(notificationId, currentUserId);
  const unreadCount = db.getUnreadNotificationCount(currentUserId);
  res.json({ success: true, unreadCount });
});

// Clear all notifications for a user
app.delete('/api/notifications', authenticateUser, (req, res) => {
  const currentUserId = req.userId; // From authenticated user
  
  db.clearUserNotifications(currentUserId);
  res.json({ success: true, unreadCount: 0 });
});

// Group-related API endpoints

// Get all groups for a user
app.get('/api/groups', (req, res) => {
  const currentUserId = req.headers['user-id'];
  
  if (!currentUserId) {
    return res.status(401).json({ error: 'User ID required' });
  }
  
  const userGroups = db.getUserGroups(currentUserId);
  
  const groupsWithDetails = userGroups.map(group => ({
    id: group.id,
    name: group.name,
    description: group.description,
    admin: group.admin_id,
    adminDetails: group.admin, // Add full admin details
    avatar: group.avatar,
    createdAt: group.created_at,
    updatedAt: group.updated_at,
    members: group.members.map(member => member.id),
    memberDetails: group.members.map(member => ({
      id: member.id,
      username: member.username,
      avatar: member.avatar,
      isOnline: onlineUsers.has(member.id),
      joinedAt: member.joined_at,
      isAdmin: member.id === group.admin_id
    }))
  }));
  
  res.json({
    groups: groupsWithDetails,
    total: groupsWithDetails.length
  });
});

// Create a new group
app.post('/api/groups', async (req, res) => {
  const { name, members = [] } = req.body;
  const currentUserId = req.headers['user-id'];
  
  if (!currentUserId) {
    return res.status(401).json({ error: 'User ID required' });
  }
  
  if (!name || !name.trim()) {
    return res.status(400).json({ error: 'Group name is required' });
  }
  
  const groupId = uuidv4();
  const group = {
    id: groupId,
    name: name.trim(),
    admin: currentUserId,
    members: [currentUserId, ...members.filter(id => id !== currentUserId)],
    createdAt: new Date().toISOString(),
    description: '',
    avatar: `https://ui-avatars.com/api/?name=${encodeURIComponent(name)}&background=random`
  };
  
  try {
    db.createGroup(group);
    
    // Get group with member details
    const createdGroup = db.getGroupById(groupId);
    const groupWithDetails = {
      id: createdGroup.id,
      name: createdGroup.name,
      description: createdGroup.description,
      admin: createdGroup.admin_id,
      avatar: createdGroup.avatar,
      createdAt: createdGroup.created_at,
      members: createdGroup.members.map(member => member.id),
      memberDetails: createdGroup.members.map(member => ({
        id: member.id,
        username: member.username,
        avatar: member.avatar,
        isOnline: onlineUsers.has(member.id),
        joinedAt: member.joined_at
      }))
    };
    
    res.json({ group: groupWithDetails });
  } catch (error) {
    console.error('Error creating group:', error);
    res.status(500).json({ error: 'Failed to create group' });
  }
});

// Get group details
app.get('/api/groups/:groupId', (req, res) => {
  const { groupId } = req.params;
  const currentUserId = req.headers['user-id'];
  
  if (!currentUserId) {
    return res.status(401).json({ error: 'User ID required' });
  }
  
  const group = db.getGroupById(groupId);
  
  if (!group) {
    return res.status(404).json({ error: 'Group not found' });
  }
  
  const isMember = group.members.some(member => member.id === currentUserId);
  if (!isMember) {
    return res.status(403).json({ error: 'Access denied' });
  }
  
  const groupWithDetails = {
    id: group.id,
    name: group.name,
    description: group.description,
    admin: group.admin_id,
    adminDetails: group.admin, // Add full admin details
    avatar: group.avatar,
    createdAt: group.created_at,
    updatedAt: group.updated_at,
    members: group.members.map(member => member.id),
    memberDetails: group.members.map(member => ({
      id: member.id,
      username: member.username,
      avatar: member.avatar,
      isOnline: onlineUsers.has(member.id),
      joinedAt: member.joined_at,
      isAdmin: member.id === group.admin_id
    }))
  };
  
  res.json({ group: groupWithDetails });
});

// Update group details
app.put('/api/groups/:groupId', async (req, res) => {
  const { groupId } = req.params;
  const { name, description } = req.body;
  const currentUserId = req.headers['user-id'];
  
  if (!currentUserId) {
    return res.status(401).json({ error: 'User ID required' });
  }
  
  const group = db.getGroupById(groupId);
  
  if (!group) {
    return res.status(404).json({ error: 'Group not found' });
  }
  
  if (group.admin_id !== currentUserId) {
    return res.status(403).json({ error: 'Only group admin can update group details' });
  }
  
  const updates = {};
  if (name && name.trim()) {
    updates.name = name.trim();
  }
  if (description !== undefined) {
    updates.description = description;
  }
  updates.updatedAt = new Date().toISOString();
  
  try {
    db.updateGroup(groupId, updates);
    const updatedGroup = db.getGroupById(groupId);
    res.json({ group: updatedGroup });
  } catch (error) {
    console.error('Error updating group:', error);
    res.status(500).json({ error: 'Failed to update group' });
  }
});

// Add member to group
app.post('/api/groups/:groupId/members', async (req, res) => {
  const { groupId } = req.params;
  const { userId } = req.body;
  const currentUserId = req.headers['user-id'];
  
  if (!currentUserId) {
    return res.status(401).json({ error: 'User ID required' });
  }
  
  const group = db.getGroupById(groupId);
  
  if (!group) {
    return res.status(404).json({ error: 'Group not found' });
  }
  
  if (group.admin_id !== currentUserId) {
    return res.status(403).json({ error: 'Only group admin can add members' });
  }
  
  const user = db.getUserById(userId);
  if (!user) {
    return res.status(404).json({ error: 'User not found' });
  }
  
  const isMember = group.members.some(member => member.id === userId);
  if (isMember) {
    return res.status(400).json({ error: 'User is already a member' });
  }
  
  try {
    db.addGroupMember(groupId, userId);
    const updatedGroup = db.getGroupById(groupId);
    res.json({ success: true, group: updatedGroup });
  } catch (error) {
    console.error('Error adding group member:', error);
    res.status(500).json({ error: 'Failed to add member' });
  }
});

// Remove member from group
app.delete('/api/groups/:groupId/members/:userId', async (req, res) => {
  const { groupId, userId } = req.params;
  const currentUserId = req.headers['user-id'];
  
  if (!currentUserId) {
    return res.status(401).json({ error: 'User ID required' });
  }
  
  const group = db.getGroupById(groupId);
  
  if (!group) {
    return res.status(404).json({ error: 'Group not found' });
  }
  
  // Allow admin to remove anyone, or allow users to remove themselves
  if (group.admin_id !== currentUserId && userId !== currentUserId) {
    return res.status(403).json({ error: 'Access denied' });
  }
  
  const isMember = group.members.some(member => member.id === userId);
  if (!isMember) {
    return res.status(400).json({ error: 'User is not a member' });
  }
  
  if (userId === group.admin_id) {
    return res.status(400).json({ error: 'Admin cannot be removed from group' });
  }
  
  try {
    db.removeGroupMember(groupId, userId);
    const updatedGroup = db.getGroupById(groupId);
    res.json({ success: true, group: updatedGroup });
  } catch (error) {
    console.error('Error removing group member:', error);
    res.status(500).json({ error: 'Failed to remove member' });
  }
});

// Delete group
app.delete('/api/groups/:groupId', async (req, res) => {
  const { groupId } = req.params;
  const currentUserId = req.headers['user-id'];
  
  if (!currentUserId) {
    return res.status(401).json({ error: 'User ID required' });
  }
  
  const group = db.getGroupById(groupId);
  
  if (!group) {
    return res.status(404).json({ error: 'Group not found' });
  }
  
  if (group.admin_id !== currentUserId) {
    return res.status(403).json({ error: 'Only group admin can delete group' });
  }
  
  try {
    db.deleteGroup(groupId);
    res.json({ success: true });
  } catch (error) {
    console.error('Error deleting group:', error);
    res.status(500).json({ error: 'Failed to delete group' });
  }
});

// Get group messages
app.get('/api/groups/:groupId/messages', (req, res) => {
  const { groupId } = req.params;
  const { limit = 50, offset = 0 } = req.query;
  const currentUserId = req.headers['user-id'];
  
  if (!currentUserId) {
    return res.status(401).json({ error: 'User ID required' });
  }
  
  const group = db.getGroupById(groupId);
  
  if (!group) {
    return res.status(404).json({ error: 'Group not found' });
  }
  
  const isMember = group.members.some(member => member.id === currentUserId);
  if (!isMember) {
    return res.status(403).json({ error: 'Access denied' });
  }
  
  const messages = db.getGroupMessages(groupId, parseInt(limit), parseInt(offset));
  const total = db.getTotalGroupMessages(groupId);
  
  // Reverse to get chronological order (newest last)
  const paginatedMessages = messages.reverse().map(msg => ({
    id: msg.id,
    content: msg.content,
    userId: msg.user_id,
    groupId: msg.group_id,
    timestamp: msg.timestamp,
    type: msg.message_type,
    isGroup: true,
    attachments: db.getMessageFiles(msg.id),
    user: {
      id: msg.user_id,
      username: msg.username,
      avatar: msg.avatar
    }
  }));
  
  res.json({
    messages: paginatedMessages,
    total: total,
    hasMore: total > parseInt(limit) + parseInt(offset)
  });
});

// File upload endpoint
app.post('/api/upload', upload.single('file'), (req, res) => {
  try {
    if (!req.file) {
      return res.status(400).json({ error: 'No file uploaded' });
    }

    const fileInfo = {
      id: uuidv4(),
      originalName: Buffer.from(req.file.originalname, 'latin1').toString('utf8'),
      filename: req.file.filename,
      mimetype: req.file.mimetype,
      size: req.file.size,
      uploadDate: new Date().toISOString(),
      url: `http://localhost:5000/uploads/${req.file.filename}` // Full URL for downloads
    };

    res.json({
      success: true,
      file: fileInfo
    });
  } catch (error) {
    console.error('Error uploading file:', error);
    res.status(500).json({ error: 'Failed to upload file' });
  }
});

// Get file info endpoint
app.get('/api/files/:filename', async (req, res) => {
  try {
    const filename = req.params.filename;
    const filePath = path.join(UPLOADS_DIR, filename);
    
    // Check if file exists
    try {
      await fs.access(filePath);
    } catch (error) {
      return res.status(404).json({ error: 'File not found' });
    }

    const stats = await fs.stat(filePath);
    const mimeType = mime.lookup(filename) || 'application/octet-stream';

    res.json({
      filename,
      size: stats.size,
      mimetype: mimeType,
      url: `http://localhost:5000/uploads/${filename}`,
      uploadDate: stats.birthtime
    });
  } catch (error) {
    console.error('Error getting file info:', error);
    res.status(500).json({ error: 'Failed to get file info' });
  }
});

// Socket.IO connection handling
io.on('connection', (socket) => {
  console.log('New client connected:', socket.id);
  
  // User joins
  socket.on('join', (userData) => {
    if (!userData || !userData.token) {
      socket.emit('error', { message: 'Authentication token required' });
      return;
    }
    
    try {
      // Verify JWT token
      const decoded = jwt.verify(userData.token, JWT_SECRET);
      const user = db.getUserById(decoded.userId);
      
      if (!user) {
        socket.emit('error', { message: 'User not found' });
        return;
      }
      
      socket.userId = decoded.userId;
      socket.user = {
        id: user.id,
        username: user.username,
        email: user.email,
        avatar: user.avatar
      };
      
      // Add to online users
      onlineUsers.set(decoded.userId, {
        ...socket.user,
        socketId: socket.id,
        lastSeen: new Date().toISOString()
      });
      
      // Join user to general room
      socket.join('general');
      
      // Join user to their group rooms
      const userGroups = db.getUserGroups(decoded.userId);
      userGroups.forEach(group => {
        socket.join(`group_${group.id}`);
      });
      
      // Notify others that user is online
      socket.broadcast.emit('user_online', {
        user: socket.user,
        timestamp: new Date().toISOString()
      });
      
      // Send current online users to the new user
      socket.emit('online_users', Array.from(onlineUsers.values()));
      
      // Send stored notifications to the user
      try {
        const storedNotifications = db.getUserNotifications(decoded.userId, 50, 0); // Get last 50 notifications
        if (storedNotifications.length > 0) {
          const formattedNotifications = storedNotifications.map(notif => ({
            id: notif.id,
            type: notif.type,
            title: notif.title,
            message: notif.message,
            sender: notif.sender_id ? {
              id: notif.sender_id,
              username: notif.sender_username,
              avatar: notif.sender_avatar
            } : null,
            group: notif.group_id ? {
              id: notif.group_id,
              name: notif.group_name
            } : null,
            timestamp: notif.timestamp,
            read: Boolean(notif.is_read),
            messageId: notif.message_id
          }));
          
          socket.emit('stored_notifications', {
            notifications: formattedNotifications,
            count: formattedNotifications.length
          });
        }
      } catch (error) {
        console.error('Error loading stored notifications for user:', error);
      }
      
      console.log(`User ${socket.user.username} joined`);
    } catch (error) {
      if (error.name === 'TokenExpiredError') {
        socket.emit('error', { message: 'Token expired', code: 'TOKEN_EXPIRED' });
      } else if (error.name === 'JsonWebTokenError') {
        socket.emit('error', { message: 'Invalid token', code: 'INVALID_TOKEN' });
      } else {
        socket.emit('error', { message: 'Authentication failed' });
      }
      return;
    }
  });
  
  // Handle new message
  socket.on('send_message', async (messageData) => {
    if (!socket.userId || (!messageData.content && (!messageData.attachments || messageData.attachments.length === 0))) {
      socket.emit('error', { message: 'Invalid message data - must have content or attachments' });
      return;
    }
    
    const user = db.getUserById(socket.userId);
    if (!user) {
      socket.emit('error', { message: 'User not found' });
      return;
    }
    
    const message = {
      id: uuidv4(),
      content: messageData.content ? messageData.content.trim() : '',
      userId: socket.userId,
      timestamp: new Date().toISOString(),
      type: messageData.type || 'text',
      chatType: 'general'
    };
    
    try {
      // Save message to database
      db.createMessage(message);
      
      // Save file attachments if any
      if (messageData.attachments && messageData.attachments.length > 0) {
        messageData.attachments.forEach(attachment => {
          // Ensure all required properties are present
          if (!attachment.id || !attachment.originalName || !attachment.filename || 
              !attachment.mimetype || !attachment.size || !attachment.uploadDate || !attachment.url) {
            console.error('Missing required attachment properties:', attachment);
            return; // Skip this attachment
          }
          
          const fileData = {
            id: attachment.id,
            messageId: message.id,
            originalName: attachment.originalName,
            filename: attachment.filename,
            mimetype: attachment.mimetype,
            size: attachment.size,
            url: attachment.url,
            uploadDate: attachment.uploadDate
          };
          
          try {
            db.createFile(fileData);
          } catch (fileError) {
            console.error('Error creating file record:', fileError);
            console.error('File data:', fileData);
          }
        });
      }
      
      // Get the stored file attachments
      const storedAttachments = db.getMessageFiles(message.id);
      
      // Send message to all users in the room
      const messageWithUser = {
        id: message.id,
        content: message.content,
        userId: message.userId,
        timestamp: message.timestamp,
        type: message.type,
        attachments: storedAttachments,
        user: {
          id: user.id,
          username: user.username,
          avatar: user.avatar
        }
      };
      
      io.to('general').emit('new_message', messageWithUser);
      
      // Create notifications for all users except the sender for general chat
      const allUsers = db.getAllUsers();
      for (const notifyUser of allUsers) {
        if (notifyUser.id !== socket.userId) {
          const notificationId = uuidv4();
          const generalNotification = {
            id: notificationId,
            type: 'general_message',
            title: `New message from ${user.username}`,
            message: message.content || (storedAttachments && storedAttachments.length > 0 ? `Sent ${storedAttachments.length} file(s)` : 'Sent a message'),
            senderId: user.id,
            groupId: null,
            messageId: message.id,
            timestamp: new Date().toISOString()
          };
          
          const storedNotification = await addNotification(notifyUser.id, generalNotification);
          
          // Send real-time notification to online users
          if (onlineUsers.has(notifyUser.id)) {
            const userSocket = Array.from(io.sockets.sockets.values())
              .find(s => s.userId === notifyUser.id);
            if (userSocket) {
              userSocket.emit('general_message_notification', {
                id: storedNotification.id,
                type: 'general_message',
                title: storedNotification.title,
                message: storedNotification.message,
                sender: {
                  id: user.id,
                  username: user.username,
                  avatar: user.avatar
                },
                timestamp: storedNotification.timestamp,
                read: false,
                messageId: message.id
              });
            }
          }
        }
      }
      
      console.log(`Message from ${user.username}: ${message.content || (storedAttachments && storedAttachments.length > 0 ? `[${storedAttachments.length} file(s)]` : '[empty message]')}`);
    } catch (error) {
      console.error('Error saving message:', error);
      socket.emit('error', { message: 'Failed to send message' });
    }
  });
  
  // Handle private messages
  socket.on('send_private_message', async (messageData) => {
    if (!socket.userId || (!messageData.content && (!messageData.attachments || messageData.attachments.length === 0)) || !messageData.recipientId) {
      socket.emit('error', { message: 'Invalid private message data - must have content or attachments and recipient' });
      return;
    }
    
    const sender = db.getUserById(socket.userId);
    const recipient = db.getUserById(messageData.recipientId);
    
    if (!sender || !recipient) {
      socket.emit('error', { message: 'Sender or recipient not found' });
      return;
    }
    
    const message = {
      id: uuidv4(),
      content: messageData.content ? messageData.content.trim() : '',
      userId: socket.userId,
      recipientId: messageData.recipientId,
      timestamp: new Date().toISOString(),
      type: messageData.type || 'text',
      chatType: 'private'
    };
    
    try {
      // Save message to database
      db.createMessage(message);
      
      // Save file attachments if any (private message)
      if (messageData.attachments && messageData.attachments.length > 0) {
        messageData.attachments.forEach(attachment => {
          // Ensure all required properties are present
          if (!attachment.id || !attachment.originalName || !attachment.filename || 
              !attachment.mimetype || !attachment.size || !attachment.uploadDate || !attachment.url) {
            console.error('Missing required attachment properties:', attachment);
            return; // Skip this attachment
          }
          
          const fileData = {
            id: attachment.id,
            messageId: message.id,
            originalName: attachment.originalName,
            filename: attachment.filename,
            mimetype: attachment.mimetype,
            size: attachment.size,
            url: attachment.url,
            uploadDate: attachment.uploadDate
          };
          
          try {
            db.createFile(fileData);
          } catch (fileError) {
            console.error('Error creating file record:', fileError);
            console.error('File data:', fileData);
          }
        });
      }
      
      // Get the stored file attachments
      const storedAttachments = db.getMessageFiles(message.id);
      
      // Send message with user data to both sender and recipient
      const messageWithUsers = {
        id: message.id,
        content: message.content,
        userId: message.userId,
        recipientId: message.recipientId,
        timestamp: message.timestamp,
        type: message.type,
        isPrivate: true,
        attachments: storedAttachments,
        user: {
          id: sender.id,
          username: sender.username,
          avatar: sender.avatar
        },
        recipient: {
          id: recipient.id,
          username: recipient.username,
          avatar: recipient.avatar
        }
      };
      
      // Send to recipient if online
      const recipientSocket = Array.from(io.sockets.sockets.values())
        .find(s => s.userId === messageData.recipientId);
      
      // Always store notification in database for private message
      const notificationId = uuidv4();
      const privateNotification = {
        id: notificationId,
        type: 'private_message',
        title: `New message from ${sender.username}`,
        message: message.content || (storedAttachments && storedAttachments.length > 0 ? `Sent ${storedAttachments.length} file(s)` : 'Sent a message'),
        senderId: sender.id,
        groupId: null,
        messageId: message.id,
        timestamp: new Date().toISOString()
      };
      
      const storedNotification = await addNotification(messageData.recipientId, privateNotification);
      
      if (recipientSocket) {
        recipientSocket.emit('private_message', messageWithUsers);
        
        // Send real-time notification to recipient for private message
        recipientSocket.emit('private_message_notification', {
          id: storedNotification.id,
          type: 'private_message',
          title: storedNotification.title,
          message: storedNotification.message,
          sender: {
            id: sender.id,
            username: sender.username,
            avatar: sender.avatar
          },
          timestamp: storedNotification.timestamp,
          read: false,
          messageId: message.id
        });
      } else {
        console.log(`User ${recipient.username} is offline, notification stored for later delivery`);
      }
      
      // Send to sender (for confirmation and multi-device sync)
      socket.emit('private_message', messageWithUsers);
      
      console.log(`Private message from ${sender.username} to ${recipient.username}: ${message.content || (storedAttachments && storedAttachments.length > 0 ? `[${storedAttachments.length} file(s)]` : '[empty message]')}`);
    } catch (error) {
      console.error('Error saving private message:', error);
      socket.emit('error', { message: 'Failed to send private message' });
    }
  });
  
  // Handle group messages
  socket.on('send_group_message', async (messageData) => {
    if (!socket.userId || (!messageData.content && (!messageData.attachments || messageData.attachments.length === 0)) || !messageData.groupId) {
      socket.emit('error', { message: 'Invalid group message data - must have content or attachments and groupId' });
      return;
    }
    
    const sender = db.getUserById(socket.userId);
    const group = db.getGroupById(messageData.groupId);
    
    if (!sender) {
      socket.emit('error', { message: 'Sender not found' });
      return;
    }
    
    if (!group) {
      socket.emit('error', { message: 'Group not found' });
      return;
    }
    
    const isMember = group.members.some(member => member.id === socket.userId);
    if (!isMember) {
      socket.emit('error', { message: 'You are not a member of this group' });
      return;
    }
    
    const message = {
      id: uuidv4(),
      content: messageData.content ? messageData.content.trim() : '',
      userId: socket.userId,
      groupId: messageData.groupId,
      timestamp: new Date().toISOString(),
      type: messageData.type || 'text',
      chatType: 'group'
    };
    
    try {
      // Save message to database
      db.createMessage(message);
      
      // Save file attachments if any (group message)
      if (messageData.attachments && messageData.attachments.length > 0) {
        messageData.attachments.forEach(attachment => {
          // Ensure all required properties are present
          if (!attachment.id || !attachment.originalName || !attachment.filename || 
              !attachment.mimetype || !attachment.size || !attachment.uploadDate || !attachment.url) {
            console.error('Missing required attachment properties:', attachment);
            return; // Skip this attachment
          }
          
          const fileData = {
            id: attachment.id,
            messageId: message.id,
            originalName: attachment.originalName,
            filename: attachment.filename,
            mimetype: attachment.mimetype,
            size: attachment.size,
            url: attachment.url,
            uploadDate: attachment.uploadDate
          };
          
          try {
            db.createFile(fileData);
          } catch (fileError) {
            console.error('Error creating file record:', fileError);
            console.error('File data:', fileData);
          }
        });
      }
      
      // Get the stored file attachments
      const storedAttachments = db.getMessageFiles(message.id);
      
      // Send message with user data to all group members
      const messageWithUser = {
        id: message.id,
        content: message.content,
        userId: message.userId,
        groupId: message.groupId,
        timestamp: message.timestamp,
        type: message.type,
        isGroup: true,
        attachments: storedAttachments,
        user: {
          id: sender.id,
          username: sender.username,
          avatar: sender.avatar
        },
        group: {
          id: group.id,
          name: group.name,
          avatar: group.avatar
        }
      };
      
      // Send to all group members
      io.to(`group_${messageData.groupId}`).emit('group_message', messageWithUser);
      
      // Send notifications to all group members (excluding sender)
      const allGroupMembers = group.members.filter(member => member.id !== socket.userId);
      
      // Store notifications in database for all members
      for (const member of allGroupMembers) {
        const notificationId = uuidv4();
        const groupNotification = {
          id: notificationId,
          type: 'group_message',
          title: `New message in ${group.name}`,
          message: `${sender.username}: ${message.content || (storedAttachments && storedAttachments.length > 0 ? `Sent ${storedAttachments.length} file(s)` : 'Sent a message')}`,
          senderId: sender.id,
          groupId: group.id,
          messageId: message.id,
          timestamp: new Date().toISOString()
        };
        
        const storedNotification = await addNotification(member.id, groupNotification);
        
        // Send real-time notification to online members
        if (onlineUsers.has(member.id)) {
          const memberSocket = Array.from(io.sockets.sockets.values())
            .find(s => s.userId === member.id);
          if (memberSocket) {
            memberSocket.emit('group_message_notification', {
              id: storedNotification.id,
              type: 'group_message',
              title: storedNotification.title,
              message: storedNotification.message,
              sender: {
                id: sender.id,
                username: sender.username,
                avatar: sender.avatar
              },
              group: {
                id: group.id,
                name: group.name,
                avatar: group.avatar
              },
              timestamp: storedNotification.timestamp,
              read: false,
              messageId: message.id
            });
          }
        }
      }
      
      console.log(`Group message from ${sender.username} in ${group.name}: ${message.content || (storedAttachments && storedAttachments.length > 0 ? `[${storedAttachments.length} file(s)]` : '[empty message]')}`);
    } catch (error) {
      console.error('Error saving group message:', error);
      socket.emit('error', { message: 'Failed to send group message' });
    }
  });
  
  // Handle getting group message history
  socket.on('get_group_messages', (data) => {
    if (!socket.userId || !data.groupId) {
      socket.emit('error', { message: 'Invalid request for group messages' });
      return;
    }
    
    const group = db.getGroupById(data.groupId);
    if (!group) {
      socket.emit('error', { message: 'Group not found' });
      return;
    }
    
    const isMember = group.members.some(member => member.id === socket.userId);
    if (!isMember) {
      socket.emit('error', { message: 'You are not a member of this group' });
      return;
    }
    
    const messages = db.getGroupMessages(data.groupId, 100, 0); // Get last 100 messages
    
    const messagesWithUsers = messages.reverse().map(msg => ({
      id: msg.id,
      content: msg.content,
      userId: msg.user_id,
      groupId: msg.group_id,
      timestamp: msg.timestamp,
      type: msg.message_type,
      isGroup: true,
      attachments: db.getMessageFiles(msg.id),
      user: {
        id: msg.user_id,
        username: msg.username,
        avatar: msg.avatar
      },
      group: {
        id: group.id,
        name: group.name,
        avatar: group.avatar
      }
    }));
    
    socket.emit('group_message_history', {
      groupId: data.groupId,
      messages: messagesWithUsers
    });
  });
  
  // Handle group management events
  socket.on('create_group', async (groupData) => {
    if (!socket.userId || !groupData.name || !groupData.name.trim()) {
      socket.emit('error', { message: 'Invalid group data - name is required' });
      return;
    }
    
    const groupId = uuidv4();
    const group = {
      id: groupId,
      name: groupData.name.trim(),
      admin: socket.userId,
      members: [socket.userId, ...(groupData.members || []).filter(id => id !== socket.userId)],
      createdAt: new Date().toISOString(),
      description: groupData.description || '',
      avatar: `https://ui-avatars.com/api/?name=${encodeURIComponent(groupData.name)}&background=random`
    };
    
    try {
      db.createGroup(group);
      
      // Join creator to group room
      socket.join(`group_${groupId}`);
      
      // Get created group with member details
      const createdGroup = db.getGroupById(groupId);
      
      // Join other members to group room if they're online
      createdGroup.members.forEach(member => {
        if (member.id !== socket.userId) {
          const memberSocket = Array.from(io.sockets.sockets.values())
            .find(s => s.userId === member.id);
          if (memberSocket) {
            memberSocket.join(`group_${groupId}`);
            memberSocket.emit('group_created', {
              group: {
                id: createdGroup.id,
                name: createdGroup.name,
                description: createdGroup.description,
                admin: createdGroup.admin_id,
                adminDetails: createdGroup.admin,
                avatar: createdGroup.avatar,
                createdAt: createdGroup.created_at,
                members: createdGroup.members.map(m => m.id),
                memberDetails: createdGroup.members.map(m => ({
                  id: m.id,
                  username: m.username,
                  avatar: m.avatar,
                  isOnline: onlineUsers.has(m.id),
                  joinedAt: m.joined_at,
                  isAdmin: m.id === createdGroup.admin_id
                }))
              }
            });
          }
        }
      });
      
      // Send group creation confirmation to creator
      socket.emit('group_created', {
        group: {
          id: createdGroup.id,
          name: createdGroup.name,
          description: createdGroup.description,
          admin: createdGroup.admin_id,
          adminDetails: createdGroup.admin,
          avatar: createdGroup.avatar,
          createdAt: createdGroup.created_at,
          members: createdGroup.members.map(m => m.id),
          memberDetails: createdGroup.members.map(m => ({
            id: m.id,
            username: m.username,
            avatar: m.avatar,
            isOnline: onlineUsers.has(m.id),
            joinedAt: m.joined_at,
            isAdmin: m.id === createdGroup.admin_id
          }))
        }
      });
      
      console.log(`Group "${group.name}" created by ${socket.user?.username}`);
    } catch (error) {
      console.error('Error creating group:', error);
      socket.emit('error', { message: 'Failed to create group' });
    }
  });
  
  socket.on('add_group_member', async (data) => {
    if (!socket.userId || !data.groupId || !data.userId) {
      socket.emit('error', { message: 'Invalid data for adding group member' });
      return;
    }
    
    const group = db.getGroupById(data.groupId);
    
    if (!group) {
      socket.emit('error', { message: 'Group not found' });
      return;
    }
    
    if (group.admin_id !== socket.userId) {
      socket.emit('error', { message: 'Only group admin can add members' });
      return;
    }
    
    const user = db.getUserById(data.userId);
    if (!user) {
      socket.emit('error', { message: 'User not found' });
      return;
    }
    
    const isMember = group.members.some(member => member.id === data.userId);
    if (isMember) {
      socket.emit('error', { message: 'User is already a member' });
      return;
    }
    
    try {
      db.addGroupMember(data.groupId, data.userId);
      const updatedGroup = db.getGroupById(data.groupId);
      
      // Join new member to group room if online
      const memberSocket = Array.from(io.sockets.sockets.values())
        .find(s => s.userId === data.userId);
      if (memberSocket) {
        memberSocket.join(`group_${data.groupId}`);
        memberSocket.emit('added_to_group', { 
          group: formatGroupForClient(updatedGroup, onlineUsers)
        });
      }
      
      // Notify all group members with complete updated group data
      io.to(`group_${data.groupId}`).emit('group_member_added', {
        groupId: data.groupId,
        userId: data.userId,
        user: {
          id: user.id,
          username: user.username,
          avatar: user.avatar
        },
        group: formatGroupForClient(updatedGroup, onlineUsers)
      });
      
      // Send group refresh event to all group members for dialog updates
      io.to(`group_${data.groupId}`).emit('group_updated', {
        group: formatGroupForClient(updatedGroup, onlineUsers)
      });
      
      // Send updated management data specifically to the admin
      const adminSocket = Array.from(io.sockets.sockets.values())
        .find(s => s.userId === updatedGroup.admin_id);
      if (adminSocket) {
        const managementData = db.getGroupManagementData(data.groupId, updatedGroup.admin_id);
        if (managementData) {
          const responseData = {
            group: {
              id: managementData.group.id,
              name: managementData.group.name,
              description: managementData.group.description,
              admin: managementData.group.admin_id,
              adminDetails: managementData.group.admin,
              avatar: managementData.group.avatar,
              createdAt: managementData.group.created_at,
              updatedAt: managementData.group.updated_at
            },
            currentMembers: managementData.currentMembers.map(member => ({
              id: member.id,
              username: member.username,
              avatar: member.avatar,
              isOnline: onlineUsers.has(member.id),
              joinedAt: member.joined_at,
              isAdmin: member.id === managementData.group.admin_id,
              canRemove: member.id !== managementData.group.admin_id
            })),
            availableToAdd: managementData.availableToAdd.map(user => ({
              id: user.id,
              username: user.username,
              avatar: user.avatar,
              isOnline: onlineUsers.has(user.id)
            })),
            memberCount: managementData.memberCount,
            canAddMore: managementData.canAddMore
          };
          
          adminSocket.emit('group_management_updated', responseData);
        }
      }
      
      console.log(`User ${data.userId} added to group ${updatedGroup.name}`);
    } catch (error) {
      console.error('Error adding group member:', error);
      socket.emit('error', { message: 'Failed to add member' });
    }
  });
  
  socket.on('remove_group_member', async (data) => {
    if (!socket.userId || !data.groupId || !data.userId) {
      socket.emit('error', { message: 'Invalid data for removing group member' });
      return;
    }
    
    const group = db.getGroupById(data.groupId);
    
    if (!group) {
      socket.emit('error', { message: 'Group not found' });
      return;
    }
    
    // Allow admin to remove anyone, or allow users to remove themselves
    if (group.admin_id !== socket.userId && data.userId !== socket.userId) {
      socket.emit('error', { message: 'Access denied' });
      return;
    }
    
    const isMember = group.members.some(member => member.id === data.userId);
    if (!isMember) {
      socket.emit('error', { message: 'User is not a member' });
      return;
    }
    
    if (data.userId === group.admin_id) {
      socket.emit('error', { message: 'Admin cannot be removed from group' });
      return;
    }
    
    try {
      db.removeGroupMember(data.groupId, data.userId);
      const updatedGroup = db.getGroupById(data.groupId);
      const user = db.getUserById(data.userId);
      
      // Remove member from group room
      const memberSocket = Array.from(io.sockets.sockets.values())
        .find(s => s.userId === data.userId);
      if (memberSocket) {
        memberSocket.leave(`group_${data.groupId}`);
        memberSocket.emit('removed_from_group', { 
          groupId: data.groupId, 
          group: formatGroupForClient(updatedGroup, onlineUsers)
        });
      }
      
      // Notify remaining group members with complete updated group data
      io.to(`group_${data.groupId}`).emit('group_member_removed', {
        groupId: data.groupId,
        userId: data.userId,
        user: {
          id: user.id,
          username: user.username,
          avatar: user.avatar
        },
        group: formatGroupForClient(updatedGroup, onlineUsers)
      });
      
      // Send group refresh event to all remaining group members for dialog updates
      io.to(`group_${data.groupId}`).emit('group_updated', {
        group: formatGroupForClient(updatedGroup, onlineUsers)
      });
      
      // Send updated management data specifically to the admin
      const adminSocket = Array.from(io.sockets.sockets.values())
        .find(s => s.userId === updatedGroup.admin_id);
      if (adminSocket) {
        const managementData = db.getGroupManagementData(data.groupId, updatedGroup.admin_id);
        if (managementData) {
          const responseData = {
            group: {
              id: managementData.group.id,
              name: managementData.group.name,
              description: managementData.group.description,
              admin: managementData.group.admin_id,
              adminDetails: managementData.group.admin,
              avatar: managementData.group.avatar,
              createdAt: managementData.group.created_at,
              updatedAt: managementData.group.updated_at
            },
            currentMembers: managementData.currentMembers.map(member => ({
              id: member.id,
              username: member.username,
              avatar: member.avatar,
              isOnline: onlineUsers.has(member.id),
              joinedAt: member.joined_at,
              isAdmin: member.id === managementData.group.admin_id,
              canRemove: member.id !== managementData.group.admin_id
            })),
            availableToAdd: managementData.availableToAdd.map(user => ({
              id: user.id,
              username: user.username,
              avatar: user.avatar,
              isOnline: onlineUsers.has(user.id)
            })),
            memberCount: managementData.memberCount,
            canAddMore: managementData.canAddMore
          };
          
          adminSocket.emit('group_management_updated', responseData);
        }
      }
      
      console.log(`User ${data.userId} removed from group ${updatedGroup.name}`);
    } catch (error) {
      console.error('Error removing group member:', error);
      socket.emit('error', { message: 'Failed to remove member' });
    }
  });
  
  socket.on('delete_group', async (data) => {
    if (!socket.userId || !data.groupId) {
      socket.emit('error', { message: 'Invalid data for deleting group' });
      return;
    }
    
    const group = db.getGroupById(data.groupId);
    
    if (!group) {
      socket.emit('error', { message: 'Group not found' });
      return;
    }
    
    if (group.admin_id !== socket.userId) {
      socket.emit('error', { message: 'Only group admin can delete group' });
      return;
    }
    
    try {
      // Notify all group members before deletion
      io.to(`group_${data.groupId}`).emit('group_deleted', {
        groupId: data.groupId,
        group: group
      });
      
      // Remove all members from group room
      group.members.forEach(member => {
        const memberSocket = Array.from(io.sockets.sockets.values())
          .find(s => s.userId === member.id);
        if (memberSocket) {
          memberSocket.leave(`group_${data.groupId}`);
        }
      });
      
      db.deleteGroup(data.groupId);
      
      console.log(`Group ${group.name} deleted by ${socket.user?.username}`);
    } catch (error) {
      console.error('Error deleting group:', error);
      socket.emit('error', { message: 'Failed to delete group' });
    }
  });
  
  // Handle getting private message history
  socket.on('get_private_messages', (data) => {
    if (!socket.userId || !data.userId) {
      socket.emit('error', { message: 'Invalid request for private messages' });
      return;
    }
    
    const messages = db.getPrivateMessages(socket.userId, data.userId);
    
    const messagesWithUsers = messages.map(msg => ({
      id: msg.id,
      content: msg.content,
      userId: msg.user_id,
      recipientId: msg.recipient_id,
      timestamp: msg.timestamp,
      type: msg.message_type,
      isPrivate: true,
      attachments: db.getMessageFiles(msg.id),
      user: {
        id: msg.user_id,
        username: msg.username,
        avatar: msg.avatar
      },
      recipient: db.getUserById(msg.recipient_id)
    }));
    
    socket.emit('private_message_history', {
      userId: data.userId,
      messages: messagesWithUsers
    });
  });
  
  // Handle notification events
  socket.on('mark_notification_read', async (data) => {
    if (!socket.userId || !data.notificationId) {
      socket.emit('error', { message: 'Invalid notification data' });
      return;
    }
    
    try {
      const result = db.markNotificationAsRead(data.notificationId, socket.userId);
      if (result.changes === 0) {
        socket.emit('error', { message: 'Notification not found or does not belong to user' });
        return;
      }
      
      const unreadCount = db.getUnreadNotificationCount(socket.userId);
      socket.emit('notification_marked_read', { 
        notificationId: data.notificationId,
        unreadCount 
      });
    } catch (error) {
      console.error('Error marking notification as read:', error);
      socket.emit('error', { message: 'Failed to mark notification as read' });
    }
  });
  
  socket.on('mark_all_notifications_read', async () => {
    if (!socket.userId) {
      socket.emit('error', { message: 'User not authenticated' });
      return;
    }
    
    try {
      db.markAllNotificationsAsRead(socket.userId);
      socket.emit('all_notifications_marked_read', { unreadCount: 0 });
    } catch (error) {
      console.error('Error marking all notifications as read:', error);
      socket.emit('error', { message: 'Failed to mark all notifications as read' });
    }
  });
  
  socket.on('delete_notification', async (data) => {
    if (!socket.userId || !data.notificationId) {
      socket.emit('error', { message: 'Invalid notification data' });
      return;
    }
    
    try {
      db.deleteNotification(data.notificationId, socket.userId);
      const unreadCount = db.getUnreadNotificationCount(socket.userId);
      socket.emit('notification_deleted', { 
        notificationId: data.notificationId,
        unreadCount 
      });
    } catch (error) {
      console.error('Error deleting notification:', error);
      socket.emit('error', { message: 'Failed to delete notification' });
    }
  });
  
  socket.on('clear_notifications', async () => {
    if (!socket.userId) {
      socket.emit('error', { message: 'User not authenticated' });
      return;
    }
    
    try {
      db.clearUserNotifications(socket.userId);
      socket.emit('notifications_cleared', { unreadCount: 0 });
    } catch (error) {
      console.error('Error clearing notifications:', error);
      socket.emit('error', { message: 'Failed to clear notifications' });
    }
  });
  
  socket.on('get_notifications', () => {
    if (!socket.userId) {
      socket.emit('error', { message: 'User not authenticated' });
      return;
    }
    
    try {
      const userNotifications = db.getUserNotifications(socket.userId);
      const unreadCount = db.getUnreadNotificationCount(socket.userId);
      
      const formattedNotifications = userNotifications.map(notif => ({
        id: notif.id,
        type: notif.type,
        title: notif.title,
        message: notif.message,
        sender: notif.sender_id ? {
          id: notif.sender_id,
          username: notif.sender_username,
          avatar: notif.sender_avatar
        } : null,
        group: notif.group_id ? {
          id: notif.group_id,
          name: notif.group_name
        } : null,
        timestamp: notif.timestamp,
        read: Boolean(notif.is_read),
        messageId: notif.message_id
      }));
      
      socket.emit('notifications_list', {
        notifications: formattedNotifications,
        count: formattedNotifications.length,
        unreadCount
      });
    } catch (error) {
      console.error('Error getting notifications:', error);
      socket.emit('error', { message: 'Failed to get notifications' });
    }
  });
  
  // Get refreshed group data
  socket.on('refresh_group', (data) => {
    if (!socket.userId || !data.groupId) {
      socket.emit('error', { message: 'Invalid request for group refresh' });
      return;
    }
    
    try {
      const group = db.getGroupById(data.groupId);
      if (!group) {
        socket.emit('error', { message: 'Group not found' });
        return;
      }
      
      const isMember = group.members.some(member => member.id === socket.userId);
      if (!isMember) {
        socket.emit('error', { message: 'You are not a member of this group' });
        return;
      }
      
      socket.emit('group_refreshed', {
        group: formatGroupForClient(group, onlineUsers)
      });
    } catch (error) {
      console.error('Error refreshing group:', error);
      socket.emit('error', { message: 'Failed to refresh group' });
    }
  });
  
  // Get group management data for admin
  socket.on('get_group_management', (data) => {
    if (!socket.userId || !data.groupId) {
      socket.emit('error', { message: 'Invalid request for group management data' });
      return;
    }
    
    try {
      const managementData = db.getGroupManagementData(data.groupId, socket.userId);
      if (!managementData) {
        socket.emit('error', { message: 'Group not found or access denied' });
        return;
      }
      
      const responseData = {
        group: {
          id: managementData.group.id,
          name: managementData.group.name,
          description: managementData.group.description,
          admin: managementData.group.admin_id,
          adminDetails: managementData.group.admin,
          avatar: managementData.group.avatar,
          createdAt: managementData.group.created_at,
          updatedAt: managementData.group.updated_at
        },
        currentMembers: managementData.currentMembers.map(member => ({
          id: member.id,
          username: member.username,
          avatar: member.avatar,
          isOnline: onlineUsers.has(member.id),
          joinedAt: member.joined_at,
          isAdmin: member.id === managementData.group.admin_id,
          canRemove: member.id !== managementData.group.admin_id
        })),
        availableToAdd: managementData.availableToAdd.map(user => ({
          id: user.id,
          username: user.username,
          avatar: user.avatar,
          isOnline: onlineUsers.has(user.id)
        })),
        memberCount: managementData.memberCount,
        canAddMore: managementData.canAddMore
      };
      
      socket.emit('group_management_data', responseData);
    } catch (error) {
      console.error('Error getting group management data:', error);
      socket.emit('error', { message: 'Failed to get group management data' });
    }
  });
  
  // Handle typing indicators
  socket.on('typing_start', (data) => {
    if (socket.user) {
      if (data && data.recipientId) {
        // Private typing indicator
        const recipientSocket = Array.from(io.sockets.sockets.values())
          .find(s => s.userId === data.recipientId);
        if (recipientSocket) {
          recipientSocket.emit('user_typing', {
            user: socket.user,
            isTyping: true,
            isPrivate: true
          });
        }
      } else if (data && data.groupId) {
        // Group typing indicator
        const group = db.getGroupById(data.groupId);
        if (group && group.members.some(member => member.id === socket.userId)) {
          socket.broadcast.to(`group_${data.groupId}`).emit('user_typing', {
            user: socket.user,
            isTyping: true,
            isGroup: true,
            groupId: data.groupId
          });
        }
      } else {
        // General chat typing indicator
        socket.broadcast.to('general').emit('user_typing', {
          user: socket.user,
          isTyping: true,
          isPrivate: false
        });
      }
    }
  });
  
  socket.on('typing_stop', (data) => {
    if (socket.user) {
      if (data && data.recipientId) {
        // Private typing indicator
        const recipientSocket = Array.from(io.sockets.sockets.values())
          .find(s => s.userId === data.recipientId);
        if (recipientSocket) {
          recipientSocket.emit('user_typing', {
            user: socket.user,
            isTyping: false,
            isPrivate: true
          });
        }
      } else if (data && data.groupId) {
        // Group typing indicator
        const group = db.getGroupById(data.groupId);
        if (group && group.members.some(member => member.id === socket.userId)) {
          socket.broadcast.to(`group_${data.groupId}`).emit('user_typing', {
            user: socket.user,
            isTyping: false,
            isGroup: true,
            groupId: data.groupId
          });
        }
      } else {
        // General chat typing indicator
        socket.broadcast.to('general').emit('user_typing', {
          user: socket.user,
          isTyping: false,
          isPrivate: false
        });
      }
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
  if (db) {
    db.close();
  }
  server.close(() => {
    console.log('Server closed');
    process.exit(0);
  });
});

process.on('SIGINT', async () => {
  console.log('SIGINT received, shutting down gracefully');
  if (db) {
    db.close();
  }
  server.close(() => {
    console.log('Server closed');
    process.exit(0);
  });
}); 
