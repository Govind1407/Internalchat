const Database = require('better-sqlite3');
const path = require('path');

class ChatDatabase {
  constructor() {
    this.dbPath = path.join(__dirname, 'data', 'chat.db');
    this.db = new Database(this.dbPath);
    this.initTables();
  }

  initTables() {
    // Users table
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS users (
        id TEXT PRIMARY KEY,
        username TEXT UNIQUE NOT NULL,
        email TEXT UNIQUE NOT NULL,
        password TEXT NOT NULL,
        created_at TEXT NOT NULL,
        avatar TEXT,
        updated_at TEXT
      )
    `);

    // Groups table
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS groups (
        id TEXT PRIMARY KEY,
        name TEXT NOT NULL,
        description TEXT,
        admin_id TEXT NOT NULL,
        avatar TEXT,
        created_at TEXT NOT NULL,
        updated_at TEXT,
        FOREIGN KEY (admin_id) REFERENCES users(id)
      )
    `);

    // Group members table (many-to-many relationship)
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS group_members (
        group_id TEXT NOT NULL,
        user_id TEXT NOT NULL,
        joined_at TEXT NOT NULL,
        PRIMARY KEY (group_id, user_id),
        FOREIGN KEY (group_id) REFERENCES groups(id) ON DELETE CASCADE,
        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
      )
    `);

    // Messages table (for all types of messages)
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS messages (
        id TEXT PRIMARY KEY,
        content TEXT,
        user_id TEXT NOT NULL,
        recipient_id TEXT,
        group_id TEXT,
        message_type TEXT NOT NULL DEFAULT 'text',
        chat_type TEXT NOT NULL, -- 'general', 'private', 'group'
        timestamp TEXT NOT NULL,
        FOREIGN KEY (user_id) REFERENCES users(id),
        FOREIGN KEY (recipient_id) REFERENCES users(id),
        FOREIGN KEY (group_id) REFERENCES groups(id) ON DELETE CASCADE
      )
    `);

    // Files table - back to filesystem storage
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS files (
        id TEXT PRIMARY KEY,
        message_id TEXT NOT NULL,
        original_name TEXT NOT NULL,
        filename TEXT NOT NULL,
        mimetype TEXT NOT NULL,
        size INTEGER NOT NULL,
        url TEXT NOT NULL,
        upload_date TEXT NOT NULL,
        FOREIGN KEY (message_id) REFERENCES messages(id) ON DELETE CASCADE
      )
    `);

    // Notifications table
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS notifications (
        id TEXT PRIMARY KEY,
        user_id TEXT NOT NULL,
        type TEXT NOT NULL,
        title TEXT NOT NULL,
        message TEXT NOT NULL,
        sender_id TEXT,
        group_id TEXT,
        message_id TEXT,
        is_read INTEGER DEFAULT 0,
        timestamp TEXT NOT NULL,
        FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
        FOREIGN KEY (sender_id) REFERENCES users(id),
        FOREIGN KEY (group_id) REFERENCES groups(id) ON DELETE CASCADE,
        FOREIGN KEY (message_id) REFERENCES messages(id) ON DELETE CASCADE
      )
    `);

    // Create indexes for better performance
    this.db.exec(`
      CREATE INDEX IF NOT EXISTS idx_messages_chat_type ON messages(chat_type);
      CREATE INDEX IF NOT EXISTS idx_messages_timestamp ON messages(timestamp);
      CREATE INDEX IF NOT EXISTS idx_messages_user_id ON messages(user_id);
      CREATE INDEX IF NOT EXISTS idx_messages_recipient_id ON messages(recipient_id);
      CREATE INDEX IF NOT EXISTS idx_messages_group_id ON messages(group_id);
      CREATE INDEX IF NOT EXISTS idx_notifications_user_id ON notifications(user_id);
      CREATE INDEX IF NOT EXISTS idx_notifications_is_read ON notifications(is_read);
      CREATE INDEX IF NOT EXISTS idx_files_message_id ON files(message_id);
    `);
  }

  // User operations
  createUser(user) {
    const stmt = this.db.prepare(`
      INSERT INTO users (id, username, email, password, created_at, avatar)
      VALUES (?, ?, ?, ?, ?, ?)
    `);
    return stmt.run(user.id, user.username, user.email, user.password, user.createdAt, user.avatar);
  }

  getUserById(id) {
    const stmt = this.db.prepare('SELECT * FROM users WHERE id = ?');
    return stmt.get(id);
  }

  getUserByUsername(username) {
    const stmt = this.db.prepare('SELECT * FROM users WHERE username = ?');
    return stmt.get(username);
  }

  getUserByEmail(email) {
    const stmt = this.db.prepare('SELECT * FROM users WHERE email = ?');
    return stmt.get(email);
  }

  getAllUsers() {
    const stmt = this.db.prepare('SELECT id, username, avatar FROM users');
    return stmt.all();
  }

  // Group operations
  createGroup(group) {
    const transaction = this.db.transaction(() => {
      // Insert group
      const groupStmt = this.db.prepare(`
        INSERT INTO groups (id, name, description, admin_id, avatar, created_at)
        VALUES (?, ?, ?, ?, ?, ?)
      `);
      groupStmt.run(group.id, group.name, group.description || '', group.admin, group.avatar, group.createdAt);

      // Add members
      const memberStmt = this.db.prepare(`
        INSERT INTO group_members (group_id, user_id, joined_at)
        VALUES (?, ?, ?)
      `);
      group.members.forEach(memberId => {
        memberStmt.run(group.id, memberId, group.createdAt);
      });
    });
    
    return transaction();
  }

  getGroupById(groupId) {
    const groupStmt = this.db.prepare(`
      SELECT g.*, u.username as admin_username, u.avatar as admin_avatar
      FROM groups g
      LEFT JOIN users u ON g.admin_id = u.id
      WHERE g.id = ?
    `);
    const group = groupStmt.get(groupId);
    
    if (group) {
      // Get only current valid members (users that still exist)
      const membersStmt = this.db.prepare(`
        SELECT u.id, u.username, u.avatar, gm.joined_at
        FROM group_members gm
        JOIN users u ON gm.user_id = u.id
        WHERE gm.group_id = ?
        ORDER BY gm.joined_at ASC
      `);
      group.members = membersStmt.all(groupId);
      
      // Add admin information to the group object
      group.admin = {
        id: group.admin_id,
        username: group.admin_username || 'Unknown Admin',
        avatar: group.admin_avatar || 'https://ui-avatars.com/api/?name=Unknown+Admin&background=gray&color=white'
      };
    }
    
    return group;
  }

  getUserGroups(userId) {
    const stmt = this.db.prepare(`
      SELECT g.*, gm.joined_at, u.username as admin_username, u.avatar as admin_avatar
      FROM groups g
      JOIN group_members gm ON g.id = gm.group_id
      LEFT JOIN users u ON g.admin_id = u.id
      WHERE gm.user_id = ?
      ORDER BY g.created_at DESC
    `);
    const groups = stmt.all(userId);
    
    // Get members for each group (only current valid members)
    groups.forEach(group => {
      const membersStmt = this.db.prepare(`
        SELECT u.id, u.username, u.avatar, gm.joined_at
        FROM group_members gm
        JOIN users u ON gm.user_id = u.id
        WHERE gm.group_id = ?
        ORDER BY gm.joined_at ASC
      `);
      group.members = membersStmt.all(group.id);
      
      // Add admin information to each group
      group.admin = {
        id: group.admin_id,
        username: group.admin_username || 'Unknown Admin',
        avatar: group.admin_avatar || 'https://ui-avatars.com/api/?name=Unknown+Admin&background=gray&color=white'
      };
    });
    
    return groups;
  }

  updateGroup(groupId, updates) {
    const fields = [];
    const values = [];
    
    if (updates.name) {
      fields.push('name = ?');
      values.push(updates.name);
    }
    if (updates.description !== undefined) {
      fields.push('description = ?');
      values.push(updates.description);
    }
    if (updates.updatedAt) {
      fields.push('updated_at = ?');
      values.push(updates.updatedAt);
    }
    
    values.push(groupId);
    
    const stmt = this.db.prepare(`UPDATE groups SET ${fields.join(', ')} WHERE id = ?`);
    return stmt.run(...values);
  }

  addGroupMember(groupId, userId) {
    const stmt = this.db.prepare(`
      INSERT INTO group_members (group_id, user_id, joined_at)
      VALUES (?, ?, ?)
    `);
    return stmt.run(groupId, userId, new Date().toISOString());
  }

  removeGroupMember(groupId, userId) {
    const stmt = this.db.prepare('DELETE FROM group_members WHERE group_id = ? AND user_id = ?');
    return stmt.run(groupId, userId);
  }

  transferGroupAdmin(groupId, newAdminId) {
    const stmt = this.db.prepare('UPDATE groups SET admin_id = ? WHERE id = ?');
    return stmt.run(newAdminId, groupId);
  }

  // Check and fix groups with invalid admins
  validateAndFixGroupAdmins() {
    // First, clean up orphaned group memberships
    const orphanedMembersStmt = this.db.prepare(`
      DELETE FROM group_members 
      WHERE user_id NOT IN (SELECT id FROM users)
    `);
    const orphanedMembersResult = orphanedMembersStmt.run();
    if (orphanedMembersResult.changes > 0) {
      console.log(`Cleaned up ${orphanedMembersResult.changes} orphaned group memberships`);
    }
    
    // Find groups where admin is not a member or admin user doesn't exist
    const orphanedGroupsStmt = this.db.prepare(`
      SELECT DISTINCT g.id, g.name, g.admin_id
      FROM groups g
      LEFT JOIN group_members gm ON g.id = gm.group_id AND g.admin_id = gm.user_id
      LEFT JOIN users u ON g.admin_id = u.id
      WHERE gm.user_id IS NULL OR u.id IS NULL
    `);
    const orphanedGroups = orphanedGroupsStmt.all();
    
    const fixedGroups = [];
    
    orphanedGroups.forEach(group => {
      // Get remaining members of the group
      const membersStmt = this.db.prepare(`
        SELECT gm.user_id, u.username
        FROM group_members gm
        JOIN users u ON gm.user_id = u.id
        WHERE gm.group_id = ?
        ORDER BY gm.joined_at ASC
      `);
      const members = membersStmt.all(group.id);
      
      if (members.length > 0) {
        // Transfer admin to the first remaining member
        const newAdmin = members[0];
        this.transferGroupAdmin(group.id, newAdmin.user_id);
        fixedGroups.push({
          groupId: group.id,
          groupName: group.name,
          oldAdminId: group.admin_id,
          newAdminId: newAdmin.user_id,
          newAdminUsername: newAdmin.username
        });
        console.log(`Fixed group ${group.name}: transferred admin from ${group.admin_id} to ${newAdmin.username}`);
      } else {
        // No members left, delete the group
        this.deleteGroup(group.id);
        console.log(`Deleted empty group ${group.name} with no valid members`);
      }
    });
    
    return fixedGroups;
  }

  deleteGroup(groupId) {
    const stmt = this.db.prepare('DELETE FROM groups WHERE id = ?');
    return stmt.run(groupId);
  }

  // Get group management data for admin dialogs
  getGroupManagementData(groupId, adminId) {
    const group = this.getGroupById(groupId);
    if (!group || group.admin_id !== adminId) {
      return null;
    }
    
    // Get all users except current group members
    const currentMemberIds = group.members.map(m => m.id);
    const placeholders = currentMemberIds.map(() => '?').join(',');
    
    const availableUsersStmt = this.db.prepare(`
      SELECT id, username, avatar 
      FROM users 
      WHERE id NOT IN (${placeholders})
      ORDER BY username ASC
    `);
    const availableToAdd = availableUsersStmt.all(...currentMemberIds);
    
    return {
      group: group,
      currentMembers: group.members,
      availableToAdd: availableToAdd,
      memberCount: group.members.length,
      canAddMore: availableToAdd.length > 0
    };
  }

  // Message operations
  createMessage(message) {
    const stmt = this.db.prepare(`
      INSERT INTO messages (id, content, user_id, recipient_id, group_id, message_type, chat_type, timestamp)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?)
    `);
    return stmt.run(
      message.id,
      message.content || null,
      message.userId,
      message.recipientId || null,
      message.groupId || null,
      message.type || 'text',
      message.chatType,
      message.timestamp
    );
  }

  getGeneralMessages(limit = 50, offset = 0) {
    const stmt = this.db.prepare(`
      SELECT m.*, 
             COALESCE(u.username, 'Deleted User') as username, 
             COALESCE(u.avatar, 'https://ui-avatars.com/api/?name=Deleted+User&background=gray&color=white') as avatar
      FROM messages m
      LEFT JOIN users u ON m.user_id = u.id
      WHERE m.chat_type = 'general'
      ORDER BY m.timestamp DESC
      LIMIT ? OFFSET ?
    `);
    return stmt.all(limit, offset);
  }

  getPrivateMessages(userId1, userId2) {
    const stmt = this.db.prepare(`
      SELECT m.*, 
             COALESCE(u.username, 'Deleted User') as username, 
             COALESCE(u.avatar, 'https://ui-avatars.com/api/?name=Deleted+User&background=gray&color=white') as avatar
      FROM messages m
      LEFT JOIN users u ON m.user_id = u.id
      WHERE m.chat_type = 'private'
        AND ((m.user_id = ? AND m.recipient_id = ?) OR (m.user_id = ? AND m.recipient_id = ?))
      ORDER BY m.timestamp ASC
    `);
    return stmt.all(userId1, userId2, userId2, userId1);
  }

  getGroupMessages(groupId, limit = 50, offset = 0) {
    const stmt = this.db.prepare(`
      SELECT m.*, 
             COALESCE(u.username, 'Deleted User') as username, 
             COALESCE(u.avatar, 'https://ui-avatars.com/api/?name=Deleted+User&background=gray&color=white') as avatar
      FROM messages m
      LEFT JOIN users u ON m.user_id = u.id
      WHERE m.chat_type = 'group' AND m.group_id = ?
      ORDER BY m.timestamp DESC
      LIMIT ? OFFSET ?
    `);
    return stmt.all(groupId, limit, offset);
  }

  getUserConversations(userId) {
    // First get all unique conversation partners
    const partnersStmt = this.db.prepare(`
      SELECT DISTINCT
        CASE 
          WHEN m.user_id = ? THEN m.recipient_id
          ELSE m.user_id
        END as other_user_id
      FROM messages m
      WHERE m.chat_type = 'private' AND (m.user_id = ? OR m.recipient_id = ?)
    `);
    const partners = partnersStmt.all(userId, userId, userId);
    
    // For each partner, get the last message
    const conversations = [];
    const lastMessageStmt = this.db.prepare(`
      SELECT m.content, m.timestamp, u.username, u.avatar
      FROM messages m
      JOIN users u ON u.id = ?
      WHERE m.chat_type = 'private' 
      AND ((m.user_id = ? AND m.recipient_id = ?) OR (m.user_id = ? AND m.recipient_id = ?))
      ORDER BY m.timestamp DESC
      LIMIT 1
    `);
    
    partners.forEach(partner => {
      const lastMessage = lastMessageStmt.get(
        partner.other_user_id, 
        userId, partner.other_user_id, 
        partner.other_user_id, userId
      );
      
      if (lastMessage) {
        conversations.push({
          other_user_id: partner.other_user_id,
          username: lastMessage.username,
          avatar: lastMessage.avatar,
          last_message_content: lastMessage.content,
          last_message_time: lastMessage.timestamp
        });
      }
    });
    
    // Sort by timestamp
    return conversations.sort((a, b) => 
      new Date(b.last_message_time) - new Date(a.last_message_time)
    );
  }

  // File operations - back to filesystem storage
  createFile(file) {
    // Validate all required properties
    const requiredProps = ['id', 'messageId', 'originalName', 'filename', 'mimetype', 'size', 'url', 'uploadDate'];
    const missingProps = requiredProps.filter(prop => file[prop] === undefined || file[prop] === null);
    
    if (missingProps.length > 0) {
      throw new Error(`Missing required file properties: ${missingProps.join(', ')}`);
    }
    
    const stmt = this.db.prepare(`
      INSERT INTO files (id, message_id, original_name, filename, mimetype, size, url, upload_date)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?)
    `);
    
    try {
      return stmt.run(
        file.id,
        file.messageId,
        file.originalName,
        file.filename,
        file.mimetype,
        file.size,
        file.url,
        file.uploadDate
      );
    } catch (error) {
      console.error('Database error in createFile:', error);
      console.error('File object:', JSON.stringify(file, null, 2));
      throw error;
    }
  }

  getMessageFiles(messageId) {
    const stmt = this.db.prepare(`
      SELECT id, original_name, filename, mimetype, size, url, upload_date
      FROM files WHERE message_id = ?
    `);
    const files = stmt.all(messageId);
    // Return files with proper property mapping
    return files.map(file => ({
      ...file,
      originalName: file.original_name,
      uploadDate: file.upload_date
    }));
  }

  getFileById(fileId) {
    const stmt = this.db.prepare('SELECT * FROM files WHERE id = ?');
    return stmt.get(fileId);
  }

  // Notification operations
  createNotification(notification) {
    const stmt = this.db.prepare(`
      INSERT INTO notifications (id, user_id, type, title, message, sender_id, group_id, message_id, timestamp)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    `);
    return stmt.run(
      notification.id,
      notification.userId,
      notification.type,
      notification.title,
      notification.message,
      notification.senderId || null,
      notification.groupId || null,
      notification.messageId || null,
      notification.timestamp
    );
  }

  getUserNotifications(userId, limit = 100, offset = 0) {
    const stmt = this.db.prepare(`
      SELECT n.*, u.username as sender_username, u.avatar as sender_avatar, g.name as group_name
      FROM notifications n
      LEFT JOIN users u ON n.sender_id = u.id
      LEFT JOIN groups g ON n.group_id = g.id
      WHERE n.user_id = ?
      ORDER BY n.timestamp DESC
      LIMIT ? OFFSET ?
    `);
    return stmt.all(userId, limit, offset);
  }

  getUnreadNotificationCount(userId) {
    const stmt = this.db.prepare('SELECT COUNT(*) as count FROM notifications WHERE user_id = ? AND is_read = 0');
    return stmt.get(userId).count;
  }

  getNotificationById(notificationId) {
    const stmt = this.db.prepare('SELECT * FROM notifications WHERE id = ?');
    return stmt.get(notificationId);
  }

  markNotificationAsRead(notificationId, userId = null) {
    let stmt;
    if (userId) {
      // Verify notification belongs to user for security
      stmt = this.db.prepare('UPDATE notifications SET is_read = 1 WHERE id = ? AND user_id = ?');
      return stmt.run(notificationId, userId);
    } else {
      // Legacy support without user verification (less secure)
      stmt = this.db.prepare('UPDATE notifications SET is_read = 1 WHERE id = ?');
      return stmt.run(notificationId);
    }
  }

  markAllNotificationsAsRead(userId) {
    const stmt = this.db.prepare('UPDATE notifications SET is_read = 1 WHERE user_id = ? AND is_read = 0');
    return stmt.run(userId);
  }

  deleteNotification(notificationId, userId) {
    const stmt = this.db.prepare('DELETE FROM notifications WHERE id = ? AND user_id = ?');
    return stmt.run(notificationId, userId);
  }

  clearUserNotifications(userId) {
    const stmt = this.db.prepare('DELETE FROM notifications WHERE user_id = ?');
    return stmt.run(userId);
  }

  // Clean up old notifications (older than 30 days)
  cleanupOldNotifications() {
    const thirtyDaysAgo = new Date();
    thirtyDaysAgo.setDate(thirtyDaysAgo.getDate() - 30);
    const stmt = this.db.prepare('DELETE FROM notifications WHERE timestamp < ?');
    return stmt.run(thirtyDaysAgo.toISOString());
  }

  // Count operations
  getTotalMessages(chatType = null) {
    let stmt;
    if (chatType) {
      stmt = this.db.prepare('SELECT COUNT(*) as count FROM messages WHERE chat_type = ?');
      return stmt.get(chatType).count;
    } else {
      stmt = this.db.prepare('SELECT COUNT(*) as count FROM messages');
      return stmt.get().count;
    }
  }

  getTotalGroupMessages(groupId) {
    const stmt = this.db.prepare('SELECT COUNT(*) as count FROM messages WHERE chat_type = "group" AND group_id = ?');
    return stmt.get(groupId).count;
  }

  close() {
    this.db.close();
  }
}

module.exports = ChatDatabase;
