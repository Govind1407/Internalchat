import { Injectable } from '@angular/core';
import { Observable, BehaviorSubject } from 'rxjs';
import { io, Socket } from 'socket.io-client';
import { Message, SendMessageRequest, TypingEvent, UserStatusEvent } from '../models/message.model';
import { User } from '../models/user.model';

@Injectable({
  providedIn: 'root'
})
export class SocketService {
  private socket: Socket | null = null;
  private readonly serverUrl = window.location.origin;
  
  // Subjects for real-time data
  private messagesSubject = new BehaviorSubject<Message[]>([]);
  private onlineUsersSubject = new BehaviorSubject<User[]>([]);
  private typingUsersSubject = new BehaviorSubject<User[]>([]);
  private connectionStatusSubject = new BehaviorSubject<boolean>(false);

  // Public observables
  public messages$ = this.messagesSubject.asObservable();
  public onlineUsers$ = this.onlineUsersSubject.asObservable();
  public typingUsers$ = this.typingUsersSubject.asObservable();
  public connectionStatus$ = this.connectionStatusSubject.asObservable();

  constructor() {}

  public connect(user: User): void {
    if (this.socket?.connected) {
      return;
    }

    this.socket = io(this.serverUrl, {
      autoConnect: true,
      reconnection: true,
      reconnectionAttempts: 5,
      reconnectionDelay: 1000,
    });

    this.setupSocketListeners();
    this.joinChat(user);
  }

  public disconnect(): void {
    if (this.socket) {
      this.socket.disconnect();
      this.socket = null;
      this.connectionStatusSubject.next(false);
      this.resetState();
    }
  }

  private setupSocketListeners(): void {
    if (!this.socket) return;

    // Connection events
    this.socket.on('connect', () => {
      console.log('Connected to server');
      this.connectionStatusSubject.next(true);
    });

    this.socket.on('disconnect', () => {
      console.log('Disconnected from server');
      this.connectionStatusSubject.next(false);
    });

    this.socket.on('connect_error', (error) => {
      console.error('Connection error:', error);
      this.connectionStatusSubject.next(false);
    });

    // Message events
    this.socket.on('new_message', (message: Message) => {
      const currentMessages = this.messagesSubject.value;
      this.messagesSubject.next([...currentMessages, message]);
    });

    // User events
    this.socket.on('user_online', (data: UserStatusEvent) => {
      const currentUsers = this.onlineUsersSubject.value;
      const userExists = currentUsers.find(u => u.id === data.user.id);
      if (!userExists) {
        this.onlineUsersSubject.next([...currentUsers, data.user]);
      }
    });

    this.socket.on('user_offline', (data: UserStatusEvent) => {
      const currentUsers = this.onlineUsersSubject.value;
      const filteredUsers = currentUsers.filter(u => u.id !== data.user.id);
      this.onlineUsersSubject.next(filteredUsers);
    });

    this.socket.on('online_users', (users: User[]) => {
      this.onlineUsersSubject.next(users);
    });

    // Typing events
    this.socket.on('user_typing', (data: TypingEvent) => {
      const currentTypingUsers = this.typingUsersSubject.value;
      
      if (data.isTyping) {
        const userExists = currentTypingUsers.find(u => u.id === data.user.id);
        if (!userExists) {
          this.typingUsersSubject.next([...currentTypingUsers, data.user]);
        }
      } else {
        const filteredUsers = currentTypingUsers.filter(u => u.id !== data.user.id);
        this.typingUsersSubject.next(filteredUsers);
      }
    });

    // Error events
    this.socket.on('error', (error: { message: string }) => {
      console.error('Socket error:', error.message);
    });
  }

  private joinChat(user: User): void {
    if (this.socket) {
      this.socket.emit('join', user);
    }
  }

  public sendMessage(messageData: SendMessageRequest): void {
    if (this.socket && this.socket.connected) {
      this.socket.emit('send_message', messageData);
    } else {
      console.error('Socket not connected. Cannot send message.');
    }
  }

  public startTyping(): void {
    if (this.socket && this.socket.connected) {
      this.socket.emit('typing_start');
    }
  }

  public stopTyping(): void {
    if (this.socket && this.socket.connected) {
      this.socket.emit('typing_stop');
    }
  }

  public getMessages(): Message[] {
    return this.messagesSubject.value;
  }

  public getOnlineUsers(): User[] {
    return this.onlineUsersSubject.value;
  }

  public getTypingUsers(): User[] {
    return this.typingUsersSubject.value;
  }

  public isConnected(): boolean {
    return this.connectionStatusSubject.value;
  }

  public setMessages(messages: Message[]): void {
    this.messagesSubject.next(messages);
  }

  private resetState(): void {
    this.messagesSubject.next([]);
    this.onlineUsersSubject.next([]);
    this.typingUsersSubject.next([]);
  }
} 