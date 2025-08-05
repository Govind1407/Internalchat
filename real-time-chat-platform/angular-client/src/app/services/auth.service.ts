import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { User, AuthResponse, LoginRequest, RegisterRequest } from '../models/user.model';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly API_URL = '/api/auth';
  private userSubject = new BehaviorSubject<User | null>(null);
  private tokenSubject = new BehaviorSubject<string | null>(null);

  public user$ = this.userSubject.asObservable();
  public token$ = this.tokenSubject.asObservable();

  constructor(private http: HttpClient) {
    this.loadUserFromStorage();
  }

  private loadUserFromStorage(): void {
    const userStr = localStorage.getItem('chat_user');
    const token = localStorage.getItem('chat_token');
    
    if (userStr && token) {
      try {
        const user = JSON.parse(userStr) as User;
        this.userSubject.next(user);
        this.tokenSubject.next(token);
      } catch (error) {
        console.error('Error parsing stored user data:', error);
        this.clearStorage();
      }
    }
  }

  private saveUserToStorage(user: User, token: string): void {
    localStorage.setItem('chat_user', JSON.stringify(user));
    localStorage.setItem('chat_token', token);
  }

  private clearStorage(): void {
    localStorage.removeItem('chat_user');
    localStorage.removeItem('chat_token');
  }

  public login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.API_URL}/login`, request)
      .pipe(
        tap(response => {
          this.userSubject.next(response.user);
          this.tokenSubject.next(response.token);
          this.saveUserToStorage(response.user, response.token);
        })
      );
  }

  public register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.API_URL}/register`, request)
      .pipe(
        tap(response => {
          this.userSubject.next(response.user);
          this.tokenSubject.next(response.token);
          this.saveUserToStorage(response.user, response.token);
        })
      );
  }

  public logout(): void {
    this.userSubject.next(null);
    this.tokenSubject.next(null);
    this.clearStorage();
  }

  public getCurrentUser(): User | null {
    return this.userSubject.value;
  }

  public getCurrentToken(): string | null {
    return this.tokenSubject.value;
  }

  public isAuthenticated(): boolean {
    return !!this.userSubject.value && !!this.tokenSubject.value;
  }
} 