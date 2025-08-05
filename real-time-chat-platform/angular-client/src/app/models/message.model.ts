import { User } from './user.model';

export interface Message {
  id: string;
  content: string;
  userId: string;
  timestamp: string;
  type: 'text' | 'image' | 'file';
  user: User;
}

export interface SendMessageRequest {
  content: string;
  type?: 'text' | 'image' | 'file';
}

export interface MessagesResponse {
  messages: Message[];
  total: number;
  hasMore: boolean;
}

export interface TypingEvent {
  user: User;
  isTyping: boolean;
}

export interface UserStatusEvent {
  user: User;
  timestamp: string;
} 