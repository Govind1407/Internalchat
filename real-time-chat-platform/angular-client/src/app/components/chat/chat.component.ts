import { Component, OnInit, OnDestroy, ViewChild, ElementRef, AfterViewChecked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, takeUntil, debounceTime } from 'rxjs';

import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatBadgeModule } from '@angular/material/badge';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatMenuModule } from '@angular/material/menu';

import { AuthService } from '../../services/auth.service';
import { SocketService } from '../../services/socket.service';
import { User } from '../../models/user.model';
import { Message } from '../../models/message.model';
import { NotificationService } from '../../services/notification.service';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatToolbarModule,
    MatSidenavModule,
    MatListModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatBadgeModule,
    MatCardModule,
    MatChipsModule,
    MatSnackBarModule,
    MatTooltipModule,
    MatMenuModule
  ],
  template: `
    <div class="chat-layout">
      <!-- Header -->
      <mat-toolbar class="chat-header" color="primary">
        <button mat-icon-button (click)="toggleSidebar()">
          <mat-icon>menu</mat-icon>
        </button>
        <span class="chat-title">General Chat</span>
        <span class="spacer"></span>
        <button 
          mat-icon-button 
          [matBadge]="notificationCount" 
          matBadgeColor="warn"
          [matBadgeHidden]="notificationCount === 0"
          (click)="toggleNotifications()"
          [matTooltip]="notificationsEnabled ? 'Notifications enabled' : 'Enable notifications'"
        >
          <mat-icon>{{ notificationsEnabled ? 'notifications' : 'notifications_off' }}</mat-icon>
        </button>
        <button mat-icon-button [matMenuTriggerFor]="userMenu">
          <mat-icon>account_circle</mat-icon>
        </button>
        <mat-menu #userMenu="matMenu">
          <div mat-menu-item disabled class="user-info-menu">
            <img [src]="currentUser?.avatar" [alt]="currentUser?.username" class="user-avatar-small">
            <span>{{ currentUser?.username }}</span>
          </div>
          <mat-divider></mat-divider>
          <button mat-menu-item (click)="logout()">
            <mat-icon>logout</mat-icon>
            <span>Logout</span>
          </button>
        </mat-menu>
      </mat-toolbar>

      <div class="chat-container">
        <!-- Sidebar -->
        <mat-sidenav-container class="sidenav-container">
          <mat-sidenav 
            #sidenav 
            mode="side" 
            opened="true" 
            class="chat-sidenav"
            [style.width.px]="300"
          >
            <div class="sidebar-content">
              <div class="online-users-section">
                <h3 class="section-title">
                  <mat-icon>people</mat-icon>
                  Online Users ({{ onlineUsers.length }})
                </h3>
                <div class="connection-status" 
                     [class.connected]="isConnected" 
                     [class.disconnected]="!isConnected">
                  <mat-icon>{{ isConnected ? 'wifi' : 'wifi_off' }}</mat-icon>
                  {{ isConnected ? 'Connected' : 'Disconnected' }}
                </div>
                <mat-list class="users-list">
                  <mat-list-item *ngFor="let user of onlineUsers" class="user-item">
                    <img matListItemAvatar [src]="user.avatar" [alt]="user.username">
                    <div matListItemTitle>{{ user.username }}</div>
                    <div matListItemLine class="user-status">
                      <mat-icon class="online-indicator">fiber_manual_record</mat-icon>
                      Online
                    </div>
                  </mat-list-item>
                </mat-list>
              </div>
            </div>
          </mat-sidenav>

          <mat-sidenav-content class="chat-content">
            <!-- Messages Area -->
            <div class="messages-container" #messagesContainer>
              <div class="messages-list">
                <div *ngFor="let message of messages" 
                     class="message-wrapper"
                     [class.own-message]="message.userId === currentUser?.id">
                  <mat-card class="message-card" 
                           [class.own-card]="message.userId === currentUser?.id">
                    <div class="message-header">
                      <img [src]="message.user.avatar" 
                           [alt]="message.user.username" 
                           class="message-avatar">
                      <div class="message-info">
                        <span class="message-author">{{ message.user.username }}</span>
                        <span class="message-time">{{ formatTime(message.timestamp) }}</span>
                      </div>
                    </div>
                    <div class="message-content">{{ message.content }}</div>
                  </mat-card>
                </div>

                <!-- Typing indicators -->
                <div *ngIf="typingUsers.length > 0" class="typing-indicator">
                  <mat-chip-set>
                    <mat-chip *ngFor="let user of typingUsers">
                      <img [src]="user.avatar" [alt]="user.username" class="typing-avatar">
                      {{ user.username }} is typing...
                    </mat-chip>
                  </mat-chip-set>
                </div>
              </div>
            </div>

            <!-- Message Input -->
            <div class="message-input-container">
              <form [formGroup]="messageForm" (ngSubmit)="sendMessage()" class="message-form">
                <mat-form-field appearance="outline" class="message-input">
                  <textarea 
                    matInput 
                    formControlName="content"
                    placeholder="Type your message..."
                    rows="1"
                    (keydown)="onKeyDown($event)"
                    (input)="onMessageInput()"
                    #messageInput
                  ></textarea>
                </mat-form-field>
                <button 
                  mat-fab 
                  color="primary" 
                  type="submit" 
                  class="send-button"
                  [disabled]="messageForm.invalid || !isConnected"
                  matTooltip="Send message"
                >
                  <mat-icon>send</mat-icon>
                </button>
              </form>
            </div>
          </mat-sidenav-content>
        </mat-sidenav-container>
      </div>
    </div>
  `,
  styles: [`
    .chat-layout {
      height: 100vh;
      display: flex;
      flex-direction: column;
    }

    .chat-header {
      z-index: 1000;
    }

    .chat-title {
      font-size: 20px;
      font-weight: 600;
    }

    .spacer {
      flex: 1 1 auto;
    }

    .user-info-menu {
      display: flex;
      align-items: center;
      gap: 12px;
      pointer-events: none;
    }

    .user-avatar-small {
      width: 32px;
      height: 32px;
      border-radius: 50%;
    }

    .chat-container {
      flex: 1;
      overflow: hidden;
    }

    .sidenav-container {
      height: 100%;
    }

    .chat-sidenav {
      border-right: 1px solid #e0e0e0;
      background: #fafafa;
    }

    .sidebar-content {
      padding: 16px;
      height: 100%;
    }

    .section-title {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 0 0 16px 0;
      font-size: 16px;
      font-weight: 600;
      color: #333;
    }

    .connection-status {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 12px;
      border-radius: 20px;
      font-size: 12px;
      font-weight: 500;
      margin-bottom: 16px;
    }

    .connection-status.connected {
      background: #e8f5e8;
      color: #2e7d32;
    }

    .connection-status.disconnected {
      background: #ffebee;
      color: #c62828;
    }

    .users-list {
      max-height: calc(100vh - 250px);
      overflow-y: auto;
    }

    .user-item {
      border-radius: 8px;
      margin-bottom: 4px;
    }

    .user-item:hover {
      background: rgba(0, 0, 0, 0.04);
    }

    .user-status {
      display: flex;
      align-items: center;
      gap: 4px;
      font-size: 12px;
      color: #4caf50;
    }

    .online-indicator {
      font-size: 12px;
      width: 12px;
      height: 12px;
      color: #4caf50;
    }

    .chat-content {
      display: flex;
      flex-direction: column;
      height: 100%;
    }

    .messages-container {
      flex: 1;
      overflow-y: auto;
      padding: 16px;
      background: #f5f5f5;
    }

    .messages-list {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .message-wrapper {
      display: flex;
      max-width: 70%;
    }

    .message-wrapper.own-message {
      align-self: flex-end;
      justify-content: flex-end;
    }

    .message-card {
      border-radius: 18px;
      padding: 12px 16px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
      background: white;
    }

    .message-card.own-card {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: white;
    }

    .message-header {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 8px;
    }

    .message-avatar {
      width: 24px;
      height: 24px;
      border-radius: 50%;
    }

    .message-info {
      display: flex;
      flex-direction: column;
    }

    .message-author {
      font-weight: 600;
      font-size: 14px;
    }

    .message-time {
      font-size: 11px;
      opacity: 0.7;
    }

    .message-content {
      line-height: 1.4;
      word-wrap: break-word;
    }

    .typing-indicator {
      padding: 8px 0;
    }

    .typing-avatar {
      width: 20px;
      height: 20px;
      border-radius: 50%;
      margin-right: 4px;
    }

    .message-input-container {
      padding: 16px;
      background: white;
      border-top: 1px solid #e0e0e0;
    }

    .message-form {
      display: flex;
      gap: 12px;
      align-items: flex-end;
    }

    .message-input {
      flex: 1;
    }

    .send-button {
      width: 48px;
      height: 48px;
    }

    ::ng-deep .mat-mdc-form-field-subscript-wrapper {
      display: none;
    }

    ::ng-deep .mat-toolbar.mat-primary {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    }

    ::ng-deep .mat-mdc-fab.mat-primary {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    }

    @media (max-width: 768px) {
      .message-wrapper {
        max-width: 85%;
      }
      
      .chat-sidenav {
        width: 280px !important;
      }
    }
  `]
})
export class ChatComponent implements OnInit, OnDestroy, AfterViewChecked {
  @ViewChild('messagesContainer') messagesContainer!: ElementRef;
  @ViewChild('messageInput') messageInput!: ElementRef;

  messageForm: FormGroup;
  currentUser: User | null = null;
  messages: Message[] = [];
  onlineUsers: User[] = [];
  typingUsers: User[] = [];
  isConnected = false;
  notificationsEnabled = false;
  notificationCount = 0;

  private destroy$ = new Subject<void>();
  private shouldScrollToBottom = true;
  private typingTimeout: any;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private socketService: SocketService,
    private notificationService: NotificationService,
    private snackBar: MatSnackBar,
    private router: Router
  ) {
    this.messageForm = this.fb.group({
      content: ['']
    });
  }

  ngOnInit(): void {
    this.currentUser = this.authService.getCurrentUser();
    
    if (!this.currentUser) {
      this.router.navigate(['/auth']);
      return;
    }

    this.initializeSocket();
    this.loadInitialData();
    this.checkNotificationPermission();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.socketService.disconnect();
  }

  ngAfterViewChecked(): void {
    if (this.shouldScrollToBottom) {
      this.scrollToBottom();
    }
  }

  private initializeSocket(): void {
    if (!this.currentUser) return;

    this.socketService.connect(this.currentUser);

    // Subscribe to socket events
    this.socketService.messages$
      .pipe(takeUntil(this.destroy$))
      .subscribe(messages => {
        this.messages = messages;
        this.shouldScrollToBottom = true;
      });

    this.socketService.onlineUsers$
      .pipe(takeUntil(this.destroy$))
      .subscribe(users => {
        this.onlineUsers = users;
      });

    this.socketService.typingUsers$
      .pipe(takeUntil(this.destroy$))
      .subscribe(users => {
        this.typingUsers = users.filter(user => user.id !== this.currentUser?.id);
      });

    this.socketService.connectionStatus$
      .pipe(takeUntil(this.destroy$))
      .subscribe(status => {
        this.isConnected = status;
        if (status) {
          this.snackBar.open('Connected to chat', 'Close', { duration: 2000 });
        } else {
          this.snackBar.open('Disconnected from chat', 'Close', { duration: 3000 });
        }
      });
  }

  private async loadInitialData(): Promise<void> {
    // Load messages from API
    // This would typically be handled by a chat service
  }

  private checkNotificationPermission(): void {
    this.notificationsEnabled = this.notificationService.isEnabled();
  }

  toggleSidebar(): void {
    // Implemented via template reference
  }

  toggleNotifications(): void {
    if (this.notificationsEnabled) {
      this.notificationService.disable();
      this.notificationsEnabled = false;
      this.snackBar.open('Notifications disabled', 'Close', { duration: 2000 });
    } else {
      this.notificationService.requestPermission().then(granted => {
        this.notificationsEnabled = granted;
        if (granted) {
          this.snackBar.open('Notifications enabled', 'Close', { duration: 2000 });
        } else {
          this.snackBar.open('Notification permission denied', 'Close', { duration: 3000 });
        }
      });
    }
  }

  sendMessage(): void {
    const content = this.messageForm.get('content')?.value?.trim();
    if (!content || !this.isConnected) return;

    this.socketService.sendMessage({ content });
    this.messageForm.reset();
    this.socketService.stopTyping();
  }

  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.sendMessage();
    }
  }

  onMessageInput(): void {
    // Handle typing indicators
    this.socketService.startTyping();
    
    clearTimeout(this.typingTimeout);
    this.typingTimeout = setTimeout(() => {
      this.socketService.stopTyping();
    }, 1000);
  }

  logout(): void {
    this.socketService.disconnect();
    this.authService.logout();
    this.router.navigate(['/auth']);
  }

  formatTime(timestamp: string): string {
    return new Date(timestamp).toLocaleTimeString([], { 
      hour: '2-digit', 
      minute: '2-digit' 
    });
  }

  private scrollToBottom(): void {
    try {
      if (this.messagesContainer) {
        const element = this.messagesContainer.nativeElement;
        element.scrollTop = element.scrollHeight;
      }
    } catch (err) {
      console.error('Error scrolling to bottom:', err);
    }
  }
} 