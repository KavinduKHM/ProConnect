import { Component, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NavComponent } from './core/nav/nav.component';
import { SignalrService } from './core/services/signalr.service';
import { AuthService } from './core/services/auth.service';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router } from '@angular/router';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, NavComponent, MatSnackBarModule],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit {
  title = 'ProConnect.Frontend';

  constructor(
    private signalrService: SignalrService,
    private authService: AuthService,
    private snackBar: MatSnackBar,
    private router: Router
  ) {}

  ngOnInit(): void {
    if (this.authService.isAuthenticated()) {
      this.signalrService.startConnection();
    }

    // Listen for new job notifications (vendors)
    this.signalrService.newJob$.subscribe((notification) => {
      if (notification) {
        const message = `New job posted: ${notification.title} by ${notification.customerName}`;
        this.showNotification(message, 'View Job', () => {
          this.router.navigate(['/jobs', notification.jobId]);
        });
      }
    });

    // Listen for new bid notifications (customers)
    this.signalrService.newBid$.subscribe((notification) => {
      if (notification) {
        const message = `New bid on "${notification.jobTitle}": $${notification.bidAmount} by ${notification.vendorName}`;
        this.showNotification(message, 'View Bid', () => {
          this.router.navigate(['/jobs', notification.jobId]);
        });
      }
    });
    
    // Subscribe to auth state changes
    this.authService.currentUser$.subscribe(user => {
      if (user) {
        this.signalrService.startConnection();
      } else {
        this.signalrService.stopConnection();
      }
    });
  }

  private showNotification(message: string, action: string, onAction: () => void): void {
    const snackBarRef = this.snackBar.open(message, action, {
      duration: 10000, // 10 seconds
      horizontalPosition: 'end',
      verticalPosition: 'top'
    });

    snackBarRef.onAction().subscribe(() => {
      onAction();
    });
  }
}