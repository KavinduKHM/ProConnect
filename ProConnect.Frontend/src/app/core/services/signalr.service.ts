import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Subject, Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { NotificationService, NotificationDto } from './notification.service';

@Injectable({
  providedIn: 'root'
})
export class SignalrService {
  private hubConnection?: HubConnection;
  private reconnectTimer?: ReturnType<typeof setTimeout>;
  private isStarting = false;
  private newBidSubject = new Subject<any>();
  private newJobSubject = new Subject<any>();
  public newJob$: Observable<any> = this.newJobSubject.asObservable();
  public newBid$: Observable<any> = this.newBidSubject.asObservable();

  constructor(
    private authService: AuthService,
    private notificationService: NotificationService
  ) {}

  startConnection(): void {
    // isAuthenticated() also rejects an expired token, so we never negotiate
    // with credentials the hub is guaranteed to refuse.
    if (!this.authService.isAuthenticated()) return;
    if (this.isStarting) return;
    if (this.hubConnection && this.hubConnection.state !== HubConnectionState.Disconnected) {
      return; // Already connected or connecting
    }

    if (!this.hubConnection) {
      this.hubConnection = new HubConnectionBuilder()
        .withUrl(`${environment.apiUrl.replace(/\/api\/?$/, '')}/notificationHub`, {
          // Read the token per request so a re-login uses the fresh one
          accessTokenFactory: () => this.authService.getToken() ?? ''
        })
        .withAutomaticReconnect()
        .build();

      this.registerListeners();
    }

    this.isStarting = true;
    this.hubConnection
      .start()
      .then(() => {
        this.isStarting = false;
        console.log('SignalR connection established.');
      })
      .catch((err) => {
        this.isStarting = false;
        console.error('SignalR connection failed: ', err);
        // Only retry while the session is still valid. On a rejected token the
        // auth interceptor logs the user out, so retrying would just spam 401s.
        if (this.authService.isAuthenticated()) {
          this.scheduleReconnect();
        }
      });
  }

  private scheduleReconnect(): void {
    if (this.reconnectTimer) return;
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = undefined;
      this.startConnection();
    }, 5000);
  }

  private registerListeners(): void {
    if (!this.hubConnection) return;

    // Listen for new bid notifications from the hub
    this.hubConnection.on('NewBid', (data: any) => {
      this.newBidSubject.next(data);
    });
    // Listen for new job notifications from the hub
    this.hubConnection.on('NewJob', (data: any) => {
      this.newJobSubject.next(data);
    });
  }

  stopConnection(): void {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = undefined;
    }
    if (this.hubConnection) {
      this.hubConnection.stop();
      this.hubConnection = undefined;
    }
  }
}
