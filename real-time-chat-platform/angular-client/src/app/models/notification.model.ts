export interface NotificationSettings {
  browserNotifications: boolean;
  inAppNotifications: boolean;
  soundNotifications: boolean;
  pushNotifications: boolean;
  notificationTypes: {
    newMessage: boolean;
    userMention: boolean;
    userOnline: boolean;
    userOffline: boolean;
    privateMessage: boolean;
    systemMessage: boolean;
  };
  soundSettings: {
    messageSound: string;
    mentionSound: string;
    userStatusSound: string;
    volume: number;
  };
  doNotDisturb: {
    enabled: boolean;
    startTime: string;
    endTime: string;
  };
}

export interface AppNotification {
  id: string;
  type: NotificationType;
  title: string;
  message: string;
  timestamp: Date;
  read: boolean;
  priority: NotificationPriority;
  actionable: boolean;
  actions?: NotificationAction[];
  metadata?: {
    userId?: string;
    messageId?: string;
    roomId?: string;
    userAvatar?: string;
    [key: string]: any;
  };
}

export interface NotificationAction {
  id: string;
  label: string;
  action: string;
  style?: 'primary' | 'secondary' | 'warn';
}

export enum NotificationType {
  MESSAGE = 'message',
  MENTION = 'mention',
  USER_ONLINE = 'user_online',
  USER_OFFLINE = 'user_offline',
  PRIVATE_MESSAGE = 'private_message',
  SYSTEM = 'system',
  ERROR = 'error',
  SUCCESS = 'success',
  WARNING = 'warning',
  INFO = 'info'
}

export enum NotificationPriority {
  LOW = 'low',
  NORMAL = 'normal',
  HIGH = 'high',
  URGENT = 'urgent'
}

export interface BrowserNotificationOptions extends NotificationOptions {
  priority?: NotificationPriority;
  sound?: string;
  vibrate?: number[];
  actions?: NotificationAction[];
}

export interface InAppNotificationConfig {
  duration: number;
  position: 'top' | 'bottom' | 'top-start' | 'top-end' | 'bottom-start' | 'bottom-end';
  showClose: boolean;
  showActions: boolean;
  maxVisible: number;
}

export interface NotificationStats {
  totalNotifications: number;
  unreadNotifications: number;
  notificationsByType: { [key in NotificationType]: number };
  lastNotificationTime: Date | null;
} 