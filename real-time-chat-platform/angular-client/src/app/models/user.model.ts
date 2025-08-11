export interface User {
  id: string;
  username: string;
  email: string;
  avatar: string;
  createdAt: string;
  isOnline?: boolean;
  lastSeen?: string;
}

export interface AuthResponse {
  user: User;
  token: string;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface RegisterRequest {
  username: string;
  email: string;
  password: string;
}