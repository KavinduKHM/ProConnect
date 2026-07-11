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
    // Restore user from localStorage if token exists
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
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.userKey);
    this.currentUserSubject.next(null);
  }

  // Get stored token
  getToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  // Check if user is authenticated
  isAuthenticated(): boolean {
    return !!this.getToken();
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
}