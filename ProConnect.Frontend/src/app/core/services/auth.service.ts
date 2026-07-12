import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResponse, LoginRequest, RegisterRequest, User } from '../models/user.model';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = environment.apiUrl;
  private tokenKey = 'proconnect_token';
  private userKey = 'proconnect_user';

  // Observable to track authentication state
  private currentUserSubject = new BehaviorSubject<User | null>(null);
  public currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient) {
    // Restore user from localStorage, but only if the stored token is still valid.
    // A stale token would otherwise let the app act as if it were logged in and
    // every API/SignalR call would fail with 401.
    if (this.isTokenExpired(this.getToken())) {
      this.clearSession();
      return;
    }

    const savedUser = localStorage.getItem(this.userKey);
    if (savedUser) {
      this.currentUserSubject.next(JSON.parse(savedUser));
    }
  }

  // Register
  register(data: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/Auth/register`, data)
      .pipe(tap(response => this.handleAuthResponse(response)));
  }

  // Login
  login(credentials: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/Auth/login`, credentials)
      .pipe(tap(response => this.handleAuthResponse(response)));
  }

  // Logout
  logout(): void {
    this.clearSession();
  }

  // Get stored token
  getToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  // Check if user is authenticated (token present AND not expired)
  isAuthenticated(): boolean {
    const token = this.getToken();
    if (this.isTokenExpired(token)) {
      if (token) {
        this.clearSession();
      }
      return false;
    }
    return true;
  }

  // Decode the JWT payload; returns null if the token is malformed
  private decodeToken(token: string): { exp?: number } | null {
    try {
      const payload = token.split('.')[1];
      const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
      return JSON.parse(json);
    } catch {
      return null;
    }
  }

  // A missing, malformed or past-'exp' token counts as expired
  private isTokenExpired(token: string | null): boolean {
    if (!token) {
      return true;
    }
    const payload = this.decodeToken(token);
    if (!payload?.exp) {
      return true;
    }
    return payload.exp * 1000 <= Date.now();
  }

  private clearSession(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.userKey);
    if (this.currentUserSubject.value !== null) {
      this.currentUserSubject.next(null);
    }
  }

  // Get current user (synchronous)
  getCurrentUser(): User | null {
    return this.currentUserSubject.value;
  }

  // Get user role
  getUserRole(): string | null {
    const user = this.getCurrentUser();
    return user?.role || null;
  }

  // Check if user has a specific role
  hasRole(role: string): boolean {
    return this.getUserRole() === role;
  }

  // Private: handle auth response (store token and user)
  private handleAuthResponse(response: AuthResponse): void {
    localStorage.setItem(this.tokenKey, response.token);
    localStorage.setItem(this.userKey, JSON.stringify(response.user));
    this.currentUserSubject.next(response.user);
  }

  // Update current user in localStorage and BehaviorSubject
  updateCurrentUser(updatedFields: Partial<User>): void {
    const current = this.getCurrentUser();
    if (current) {
      const newUser = { ...current, ...updatedFields };
      localStorage.setItem(this.userKey, JSON.stringify(newUser));
      this.currentUserSubject.next(newUser);
    }
  }

  // Refresh current user from localStorage (useful after updates)
  refreshCurrentUser(): void {
    const savedUser = localStorage.getItem(this.userKey);
    if (savedUser) {
        this.currentUserSubject.next(JSON.parse(savedUser));
    }
    }
}