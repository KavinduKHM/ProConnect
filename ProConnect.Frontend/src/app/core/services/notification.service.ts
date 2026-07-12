import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { tap } from 'rxjs/operators';
import { AuthService } from './auth.service';

export interface NotificationDto {
  id: number;
  title: string;
  message: string;
  actionUrl?: string;
  isRead: boolean;
  createdAt: string;
}

@Injectable({
  providedIn: 'root'
})
export class NotificationService {
  private apiUrl = `${environment.apiUrl}/notifications`;
  
  private unreadNotificationsSubject = new BehaviorSubject<NotificationDto[]>([]);
  public unreadNotifications$ = this.unreadNotificationsSubject.asObservable();

  constructor(private http: HttpClient, private authService: AuthService) {
    this.authService.currentUser$.subscribe(user => {
      if (user) {
        this.fetchUnreadNotifications().subscribe();
      } else {
        this.unreadNotificationsSubject.next([]);
      }
    });
  }

  fetchUnreadNotifications(): Observable<NotificationDto[]> {
    return this.http.get<NotificationDto[]>(this.apiUrl).pipe(
      tap(notifications => this.unreadNotificationsSubject.next(notifications))
    );
  }

  markAsRead(id: number): Observable<any> {
    return this.http.put(`${this.apiUrl}/${id}/read`, {}).pipe(
      tap(() => {
        const current = this.unreadNotificationsSubject.value;
        this.unreadNotificationsSubject.next(current.filter(n => n.id !== id));
      })
    );
  }
  
  addLiveNotification(notification: NotificationDto): void {
    const current = this.unreadNotificationsSubject.value;
    // Put at beginning
    this.unreadNotificationsSubject.next([notification, ...current]);
  }
}
