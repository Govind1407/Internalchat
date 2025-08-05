import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, filter, map } from 'rxjs';
import { 
  AppNotification, 
  NotificationSettings, 
  NotificationType, 
  NotificationPriority,
  BrowserNotificationOptions,
  InAppNotificationConfig,
  NotificationStats,
  NotificationAction
} from '../models/notification.model';

@Injectable({
  providedIn: 'root'
})
export class EnhancedNotificationService {
  private readonly STORAGE_KEY = 'chat_notification_settings';
  private readonly NOTIFICATIONS_KEY = 'chat_notifications';

  // Subjects for reactive data
  private notificationsSubject = new BehaviorSubject<AppNotification[]>([]);
  private settingsSubject = new BehaviorSubject<NotificationSettings>(this.getDefaultSettings());
  private permissionSubject = new BehaviorSubject<NotificationPermission>('default');

  // Public observables
  public notifications$ = this.notificationsSubject.asObservable();
  public settings$ = this.settingsSubject.asObservable();
  public permission$ = this.permissionSubject.asObservable();

  // Configuration
  private inAppConfig: InAppNotificationConfig = {
    duration: 5000,
    position: 'top-end',
    showClose: true,
    showActions: true,
    maxVisible: 5
  };

  constructor() {
    this.initializeService();
  }

  private initializeService(): void {
    // Load settings from storage
    this.loadSettingsFromStorage();
    
    // Load notifications from storage
    this.loadNotificationsFromStorage();
    
    // Check browser notification permission
    if ('Notification' in window) {
      this.permissionSubject.next(Notification.permission);
    }

    // Clean up old notifications on startup
    this.cleanupOldNotifications();
  }

  // Settings Management
  public updateSettings(settings: Partial<NotificationSettings>): void {
    const currentSettings = this.settingsSubject.value;
    const newSettings = { ...currentSettings, ...settings };
    this.settingsSubject.next(newSettings);
    this.saveSettingsToStorage(newSettings);
  }

  public getSettings(): NotificationSettings {
    return this.settingsSubject.value;
  }

  public resetSettings(): void {
    const defaultSettings = this.getDefaultSettings();
    this.settingsSubject.next(defaultSettings);
    this.saveSettingsToStorage(defaultSettings);
  }

  // Permission Management
  public async requestPermission(): Promise<boolean> {
    if (!('Notification' in window)) {
      console.warn('This browser does not support notifications');
      return false;
    }

    if (this.permissionSubject.value === 'granted') {
      return true;
    }

    try {
      const permission = await Notification.requestPermission();
      this.permissionSubject.next(permission);
      
      if (permission === 'granted') {
        this.updateSettings({ browserNotifications: true });
        return true;
      }
      
      return false;
    } catch (error) {
      console.error('Error requesting notification permission:', error);
      return false;
    }
  }

  public hasPermission(): boolean {
    return this.permissionSubject.value === 'granted';
  }

  // Notification Creation
  public createNotification(
    type: NotificationType,
    title: string,
    message: string,
    options: {
      priority?: NotificationPriority;
      actionable?: boolean;
      actions?: NotificationAction[];
      metadata?: any;
      showBrowser?: boolean;
      showInApp?: boolean;
      playSound?: boolean;
    } = {}
  ): string {
    const notification: AppNotification = {
      id: this.generateId(),
      type,
      title,
      message,
      timestamp: new Date(),
      read: false,
      priority: options.priority || NotificationPriority.NORMAL,
      actionable: options.actionable || false,
      actions: options.actions || [],
      metadata: options.metadata || {}
    };

    // Add to notifications list
    this.addNotification(notification);

    // Show notifications based on settings and options
    const settings = this.getSettings();
    
    if (this.shouldShowNotification(type)) {
      // Browser notification
      if ((options.showBrowser !== false) && settings.browserNotifications && this.hasPermission() && this.isDocumentHidden()) {
        this.showBrowserNotification(notification);
      }

      // In-app notification
      if ((options.showInApp !== false) && settings.inAppNotifications) {
        this.showInAppNotification(notification);
      }

      // Sound notification
      if ((options.playSound !== false) && settings.soundNotifications) {
        this.playNotificationSound(type);
      }
    }

    return notification.id;
  }

  // Specific notification types
  public notifyNewMessage(username: string, message: string, avatar?: string, messageId?: string): string {
    return this.createNotification(
      NotificationType.MESSAGE,
      `New message from ${username}`,
      message,
      {
        priority: NotificationPriority.NORMAL,
        metadata: { username, avatar, messageId },
        actionable: true,
        actions: [
          { id: 'reply', label: 'Reply', action: 'reply', style: 'primary' },
          { id: 'mark-read', label: 'Mark as Read', action: 'mark-read', style: 'secondary' }
        ]
      }
    );
  }

  public notifyUserMention(username: string, message: string, avatar?: string): string {
    return this.createNotification(
      NotificationType.MENTION,
      `${username} mentioned you`,
      message,
      {
        priority: NotificationPriority.HIGH,
        metadata: { username, avatar },
        actionable: true,
        actions: [
          { id: 'view', label: 'View Message', action: 'view', style: 'primary' }
        ]
      }
    );
  }

  public notifyUserOnline(username: string, avatar?: string): string {
    return this.createNotification(
      NotificationType.USER_ONLINE,
      `${username} is now online`,
      `${username} joined the chat`,
      {
        priority: NotificationPriority.LOW,
        metadata: { username, avatar }
      }
    );
  }

  public notifyUserOffline(username: string, avatar?: string): string {
    return this.createNotification(
      NotificationType.USER_OFFLINE,
      `${username} went offline`,
      `${username} left the chat`,
      {
        priority: NotificationPriority.LOW,
        metadata: { username, avatar }
      }
    );
  }

  public notifyPrivateMessage(username: string, message: string, avatar?: string): string {
    return this.createNotification(
      NotificationType.PRIVATE_MESSAGE,
      `Private message from ${username}`,
      message,
      {
        priority: NotificationPriority.HIGH,
        metadata: { username, avatar },
        actionable: true,
        actions: [
          { id: 'reply', label: 'Reply', action: 'reply', style: 'primary' },
          { id: 'view-chat', label: 'View Chat', action: 'view-chat', style: 'secondary' }
        ]
      }
    );
  }

  public notifySystemMessage(title: string, message: string): string {
    return this.createNotification(
      NotificationType.SYSTEM,
      title,
      message,
      {
        priority: NotificationPriority.NORMAL,
        showBrowser: false
      }
    );
  }

  public notifyError(message: string): string {
    return this.createNotification(
      NotificationType.ERROR,
      'Error',
      message,
      {
        priority: NotificationPriority.HIGH,
        showBrowser: false
      }
    );
  }

  public notifySuccess(message: string): string {
    return this.createNotification(
      NotificationType.SUCCESS,
      'Success',
      message,
      {
        priority: NotificationPriority.LOW,
        showBrowser: false
      }
    );
  }

  // Notification Management
  public markAsRead(notificationId: string): void {
    const notifications = this.notificationsSubject.value;
    const updatedNotifications = notifications.map(n => 
      n.id === notificationId ? { ...n, read: true } : n
    );
    this.notificationsSubject.next(updatedNotifications);
    this.saveNotificationsToStorage(updatedNotifications);
  }

  public markAllAsRead(): void {
    const notifications = this.notificationsSubject.value;
    const updatedNotifications = notifications.map(n => ({ ...n, read: true }));
    this.notificationsSubject.next(updatedNotifications);
    this.saveNotificationsToStorage(updatedNotifications);
  }

  public deleteNotification(notificationId: string): void {
    const notifications = this.notificationsSubject.value;
    const updatedNotifications = notifications.filter(n => n.id !== notificationId);
    this.notificationsSubject.next(updatedNotifications);
    this.saveNotificationsToStorage(updatedNotifications);
  }

  public clearAllNotifications(): void {
    this.notificationsSubject.next([]);
    this.saveNotificationsToStorage([]);
  }

  public getUnreadNotifications(): Observable<AppNotification[]> {
    return this.notifications$.pipe(
      map(notifications => notifications.filter(n => !n.read))
    );
  }

  public getNotificationsByType(type: NotificationType): Observable<AppNotification[]> {
    return this.notifications$.pipe(
      map(notifications => notifications.filter(n => n.type === type))
    );
  }

  public getNotificationStats(): Observable<NotificationStats> {
    return this.notifications$.pipe(
      map(notifications => {
        const stats: NotificationStats = {
          totalNotifications: notifications.length,
          unreadNotifications: notifications.filter(n => !n.read).length,
          notificationsByType: {} as { [key in NotificationType]: number },
          lastNotificationTime: notifications.length > 0 ? 
            new Date(Math.max(...notifications.map(n => n.timestamp.getTime()))) : null
        };

        // Count by type
        for (const type of Object.values(NotificationType)) {
          stats.notificationsByType[type] = notifications.filter(n => n.type === type).length;
        }

        return stats;
      })
    );
  }

  // Private methods
  private addNotification(notification: AppNotification): void {
    const notifications = this.notificationsSubject.value;
    const updatedNotifications = [notification, ...notifications];
    
    // Limit to prevent memory issues
    if (updatedNotifications.length > 1000) {
      updatedNotifications.splice(1000);
    }
    
    this.notificationsSubject.next(updatedNotifications);
    this.saveNotificationsToStorage(updatedNotifications);
  }

  private shouldShowNotification(type: NotificationType): boolean {
    const settings = this.getSettings();
    
    // Check do not disturb
    if (this.isInDoNotDisturbMode()) {
      return false;
    }

    // Check type-specific settings
    switch (type) {
      case NotificationType.MESSAGE:
        return settings.notificationTypes.newMessage;
      case NotificationType.MENTION:
        return settings.notificationTypes.userMention;
      case NotificationType.USER_ONLINE:
        return settings.notificationTypes.userOnline;
      case NotificationType.USER_OFFLINE:
        return settings.notificationTypes.userOffline;
      case NotificationType.PRIVATE_MESSAGE:
        return settings.notificationTypes.privateMessage;
      case NotificationType.SYSTEM:
        return settings.notificationTypes.systemMessage;
      default:
        return true;
    }
  }

  private isInDoNotDisturbMode(): boolean {
    const settings = this.getSettings();
    if (!settings.doNotDisturb.enabled) {
      return false;
    }

    const now = new Date();
    const currentTime = now.getHours() * 60 + now.getMinutes();
    
    const [startHour, startMin] = settings.doNotDisturb.startTime.split(':').map(Number);
    const [endHour, endMin] = settings.doNotDisturb.endTime.split(':').map(Number);
    
    const startTime = startHour * 60 + startMin;
    const endTime = endHour * 60 + endMin;

    if (startTime <= endTime) {
      return currentTime >= startTime && currentTime <= endTime;
    } else {
      // Crosses midnight
      return currentTime >= startTime || currentTime <= endTime;
    }
  }

  private showBrowserNotification(notification: AppNotification): void {
    if (!this.hasPermission()) return;

    const options: BrowserNotificationOptions = {
      body: notification.message,
      icon: notification.metadata?.userAvatar || '/assets/icons/chat-icon.png',
      badge: '/assets/icons/chat-badge.png',
      tag: `${notification.type}-${notification.id}`,
      requireInteraction: notification.priority === NotificationPriority.URGENT,
      silent: false,
      vibrate: this.getVibrationPattern(notification.priority)
    };

    try {
      const browserNotification = new Notification(notification.title, options);
      
      browserNotification.onclick = () => {
        window.focus();
        this.handleNotificationClick(notification);
        browserNotification.close();
      };

      // Auto close after duration based on priority
      const duration = this.getNotificationDuration(notification.priority);
      setTimeout(() => {
        browserNotification.close();
      }, duration);

    } catch (error) {
      console.error('Error showing browser notification:', error);
    }
  }

  private showInAppNotification(notification: AppNotification): void {
    // This will be handled by the notification component
    // We emit an event that the component can listen to
    window.dispatchEvent(new CustomEvent('in-app-notification', {
      detail: notification
    }));
  }

  private playNotificationSound(type: NotificationType): void {
    const settings = this.getSettings();
    let soundFile = '';

    switch (type) {
      case NotificationType.MESSAGE:
        soundFile = settings.soundSettings.messageSound;
        break;
      case NotificationType.MENTION:
        soundFile = settings.soundSettings.mentionSound;
        break;
      case NotificationType.USER_ONLINE:
      case NotificationType.USER_OFFLINE:
        soundFile = settings.soundSettings.userStatusSound;
        break;
      default:
        soundFile = settings.soundSettings.messageSound;
    }

    if (soundFile) {
      const audio = new Audio(`/assets/sounds/${soundFile}`);
      audio.volume = settings.soundSettings.volume / 100;
      audio.play().catch(error => {
        console.error('Error playing notification sound:', error);
      });
    }
  }

  private handleNotificationClick(notification: AppNotification): void {
    // Mark as read
    this.markAsRead(notification.id);

    // Handle navigation based on notification type
    switch (notification.type) {
      case NotificationType.MESSAGE:
      case NotificationType.MENTION:
      case NotificationType.PRIVATE_MESSAGE:
        // Navigate to chat or message
        window.dispatchEvent(new CustomEvent('notification-action', {
          detail: { action: 'navigate-to-chat', notification }
        }));
        break;
      default:
        // Default action
        break;
    }
  }

  private getVibrationPattern(priority: NotificationPriority): number[] {
    switch (priority) {
      case NotificationPriority.URGENT:
        return [200, 100, 200, 100, 200];
      case NotificationPriority.HIGH:
        return [200, 100, 200];
      case NotificationPriority.NORMAL:
        return [200];
      case NotificationPriority.LOW:
        return [100];
      default:
        return [200];
    }
  }

  private getNotificationDuration(priority: NotificationPriority): number {
    switch (priority) {
      case NotificationPriority.URGENT:
        return 10000; // 10 seconds
      case NotificationPriority.HIGH:
        return 7000;  // 7 seconds
      case NotificationPriority.NORMAL:
        return 5000;  // 5 seconds
      case NotificationPriority.LOW:
        return 3000;  // 3 seconds
      default:
        return 5000;
    }
  }

  private isDocumentHidden(): boolean {
    return document.hidden || document.visibilityState === 'hidden';
  }

  private generateId(): string {
    return `notif_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
  }

  private cleanupOldNotifications(): void {
    const notifications = this.notificationsSubject.value;
    const thirtyDaysAgo = new Date();
    thirtyDaysAgo.setDate(thirtyDaysAgo.getDate() - 30);

    const filteredNotifications = notifications.filter(n => 
      n.timestamp > thirtyDaysAgo
    );

    if (filteredNotifications.length !== notifications.length) {
      this.notificationsSubject.next(filteredNotifications);
      this.saveNotificationsToStorage(filteredNotifications);
    }
  }

  private getDefaultSettings(): NotificationSettings {
    return {
      browserNotifications: false,
      inAppNotifications: true,
      soundNotifications: true,
      pushNotifications: false,
      notificationTypes: {
        newMessage: true,
        userMention: true,
        userOnline: true,
        userOffline: false,
        privateMessage: true,
        systemMessage: true
      },
      soundSettings: {
        messageSound: 'message.mp3',
        mentionSound: 'mention.mp3',
        userStatusSound: 'status.mp3',
        volume: 50
      },
      doNotDisturb: {
        enabled: false,
        startTime: '22:00',
        endTime: '08:00'
      }
    };
  }

  private loadSettingsFromStorage(): void {
    try {
      const storedSettings = localStorage.getItem(this.STORAGE_KEY);
      if (storedSettings) {
        const settings = JSON.parse(storedSettings);
        this.settingsSubject.next({ ...this.getDefaultSettings(), ...settings });
      }
    } catch (error) {
      console.error('Error loading notification settings:', error);
    }
  }

  private saveSettingsToStorage(settings: NotificationSettings): void {
    try {
      localStorage.setItem(this.STORAGE_KEY, JSON.stringify(settings));
    } catch (error) {
      console.error('Error saving notification settings:', error);
    }
  }

  private loadNotificationsFromStorage(): void {
    try {
      const storedNotifications = localStorage.getItem(this.NOTIFICATIONS_KEY);
      if (storedNotifications) {
        const notifications = JSON.parse(storedNotifications).map((n: any) => ({
          ...n,
          timestamp: new Date(n.timestamp)
        }));
        this.notificationsSubject.next(notifications);
      }
    } catch (error) {
      console.error('Error loading notifications:', error);
    }
  }

  private saveNotificationsToStorage(notifications: AppNotification[]): void {
    try {
      // Only save last 100 notifications to storage
      const notificationsToSave = notifications.slice(0, 100);
      localStorage.setItem(this.NOTIFICATIONS_KEY, JSON.stringify(notificationsToSave));
    } catch (error) {
      console.error('Error saving notifications:', error);
    }
  }
} 