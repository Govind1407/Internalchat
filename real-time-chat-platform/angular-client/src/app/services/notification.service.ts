import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class NotificationService {
  private permission: NotificationPermission = 'default';

  constructor() {
    if ('Notification' in window) {
      this.permission = Notification.permission;
    }
  }

  public async requestPermission(): Promise<boolean> {
    if (!('Notification' in window)) {
      console.warn('This browser does not support notifications');
      return false;
    }

    if (this.permission === 'granted') {
      return true;
    }

    const permission = await Notification.requestPermission();
    this.permission = permission;
    return permission === 'granted';
  }

  public showNotification(title: string, options: NotificationOptions = {}): void {
    if (!this.isEnabled()) {
      return;
    }

    const defaultOptions: NotificationOptions = {
      icon: '/assets/icons/chat-icon.png',
      badge: '/assets/icons/chat-badge.png',
      tag: 'chat-notification',
      requireInteraction: false,
      ...options
    };

    try {
      const notification = new Notification(title, defaultOptions);
      
      // Auto close after 5 seconds
      setTimeout(() => {
        notification.close();
      }, 5000);

      // Handle notification click
      notification.onclick = () => {
        window.focus();
        notification.close();
      };

    } catch (error) {
      console.error('Error showing notification:', error);
    }
  }

  public showMessageNotification(username: string, message: string, avatar?: string): void {
    if (!this.canShowNotification()) {
      return;
    }

    this.showNotification(`New message from ${username}`, {
      body: message,
      icon: avatar,
      tag: 'message-notification'
    });
  }

  public showUserStatusNotification(username: string, status: 'online' | 'offline', avatar?: string): void {
    if (!this.canShowNotification()) {
      return;
    }

    const message = status === 'online' ? 'joined the chat' : 'left the chat';
    this.showNotification(`${username} ${message}`, {
      icon: avatar,
      tag: 'status-notification'
    });
  }

  public isEnabled(): boolean {
    return 'Notification' in window && this.permission === 'granted';
  }

  public isSupported(): boolean {
    return 'Notification' in window;
  }

  public getPermission(): NotificationPermission {
    return this.permission;
  }

  public disable(): void {
    // Note: Cannot programmatically revoke permission
    // This method is for internal state management
    console.log('Notifications disabled by user preference');
  }

  private canShowNotification(): boolean {
    return this.isEnabled() && document.hidden;
  }
} 