import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatIconModule } from '@angular/material/icon';

import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-auth',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatInputModule,
    MatButtonModule,
    MatTabsModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatIconModule
  ],
  template: `
    <div class="auth-container">
      <mat-card class="auth-card">
        <mat-card-header>
          <mat-card-title class="auth-title">
            <mat-icon>chat</mat-icon>
            Real-time Chat Platform
          </mat-card-title>
        </mat-card-header>

        <mat-card-content>
          <mat-tab-group [(selectedIndex)]="selectedTabIndex" animationDuration="300ms">
            <mat-tab label="Sign In">
              <form [formGroup]="loginForm" (ngSubmit)="onLogin()" class="auth-form">
                <mat-form-field appearance="outline" class="full-width">
                  <mat-label>Username</mat-label>
                  <input matInput formControlName="username" placeholder="Enter your username" />
                  <mat-error *ngIf="loginForm.get('username')?.hasError('required')">
                    Username is required
                  </mat-error>
                </mat-form-field>

                <button 
                  mat-raised-button 
                  color="primary" 
                  type="submit" 
                  class="full-width auth-button"
                  [disabled]="loginForm.invalid || isLoading"
                >
                  <mat-spinner diameter="20" *ngIf="isLoading"></mat-spinner>
                  <span *ngIf="!isLoading">Sign In</span>
                </button>
              </form>
            </mat-tab>

            <mat-tab label="Sign Up">
              <form [formGroup]="registerForm" (ngSubmit)="onRegister()" class="auth-form">
                <mat-form-field appearance="outline" class="full-width">
                  <mat-label>Username</mat-label>
                  <input matInput formControlName="username" placeholder="Choose a username" />
                  <mat-error *ngIf="registerForm.get('username')?.hasError('required')">
                    Username is required
                  </mat-error>
                  <mat-error *ngIf="registerForm.get('username')?.hasError('minlength')">
                    Username must be at least 3 characters
                  </mat-error>
                </mat-form-field>

                <mat-form-field appearance="outline" class="full-width">
                  <mat-label>Email</mat-label>
                  <input matInput formControlName="email" type="email" placeholder="Enter your email" />
                  <mat-error *ngIf="registerForm.get('email')?.hasError('required')">
                    Email is required
                  </mat-error>
                  <mat-error *ngIf="registerForm.get('email')?.hasError('email')">
                    Please enter a valid email
                  </mat-error>
                </mat-form-field>

                <button 
                  mat-raised-button 
                  color="primary" 
                  type="submit" 
                  class="full-width auth-button"
                  [disabled]="registerForm.invalid || isLoading"
                >
                  <mat-spinner diameter="20" *ngIf="isLoading"></mat-spinner>
                  <span *ngIf="!isLoading">Create Account</span>
                </button>
              </form>
            </mat-tab>
          </mat-tab-group>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .auth-container {
      height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      padding: 20px;
    }

    .auth-card {
      width: 100%;
      max-width: 400px;
      border-radius: 16px;
      box-shadow: 0 20px 60px rgba(0, 0, 0, 0.1);
    }

    .auth-title {
      text-align: center;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      font-size: 24px;
      font-weight: 600;
      color: #333;
    }

    .auth-form {
      padding: 20px 0;
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .full-width {
      width: 100%;
    }

    .auth-button {
      height: 48px;
      font-size: 16px;
      font-weight: 600;
      margin-top: 8px;
    }

    ::ng-deep .mat-mdc-tab-group {
      --mdc-tab-indicator-active-indicator-color: #667eea;
    }

    ::ng-deep .mat-mdc-button.mat-primary {
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    }

    ::ng-deep .mat-mdc-form-field.mat-focused .mat-mdc-form-field-focus-overlay {
      background-color: transparent;
    }

    ::ng-deep .mat-mdc-form-field.mat-focused .mdc-notched-outline__leading,
    ::ng-deep .mat-mdc-form-field.mat-focused .mdc-notched-outline__notch,
    ::ng-deep .mat-mdc-form-field.mat-focused .mdc-notched-outline__trailing {
      border-color: #667eea !important;
    }
  `]
})
export class AuthComponent implements OnInit {
  loginForm: FormGroup;
  registerForm: FormGroup;
  isLoading = false;
  selectedTabIndex = 0;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {
    this.loginForm = this.fb.group({
      username: ['', [Validators.required]]
    });

    this.registerForm = this.fb.group({
      username: ['', [Validators.required, Validators.minLength(3)]],
      email: ['', [Validators.required, Validators.email]]
    });
  }

  ngOnInit(): void {
    // Check if user is already authenticated
    if (this.authService.isAuthenticated()) {
      this.router.navigate(['/chat']);
    }
  }

  onLogin(): void {
    if (this.loginForm.valid) {
      this.isLoading = true;
      const { username } = this.loginForm.value;

      this.authService.login({ username, password:"Password@123" }).subscribe({
        next: (response) => {
          this.snackBar.open(`Welcome back, ${response.user.username}!`, 'Close', {
            duration: 3000,
            panelClass: ['success-snackbar']
          });
          this.router.navigate(['/chat']);
        },
        error: (error) => {
          this.snackBar.open(
            error.error?.error || 'Login failed. Please try again.',
            'Close',
            {
              duration: 5000,
              panelClass: ['error-snackbar']
            }
          );
        },
        complete: () => {
          this.isLoading = false;
        }
      });
    }
  }

  onRegister(): void {
    if (this.registerForm.valid) {
      this.isLoading = true;
      const { username, email } = this.registerForm.value;

      this.authService.register({ username, email }).subscribe({
        next: (response) => {
          this.snackBar.open(`Welcome to the chat, ${response.user.username}!`, 'Close', {
            duration: 3000,
            panelClass: ['success-snackbar']
          });
          this.router.navigate(['/chat']);
        },
        error: (error) => {
          this.snackBar.open(
            error.error?.error || 'Registration failed. Please try again.',
            'Close',
            {
              duration: 5000,
              panelClass: ['error-snackbar']
            }
          );
        },
        complete: () => {
          this.isLoading = false;
        }
      });
    }
  }
} 