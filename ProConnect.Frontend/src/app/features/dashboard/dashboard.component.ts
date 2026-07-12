import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatTabsModule } from '@angular/material/tabs';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { JobService, Job } from '../../core/services/job.service';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatTabsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatDividerModule
  ],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit {
  myJobs: Job[] = [];
  myBids: any[] = [];
  assignedJobs: Job[] = [];
  
  isLoadingJobs = false;
  isLoadingBids = false;
  isLoadingAssigned = false;
  
  errorMessage: string | null = null;
  
  isVendor = false;
  isCustomer = false;

  constructor(
    private jobService: JobService,
    public authService: AuthService
  ) {}

  ngOnInit(): void {
    const user = this.authService.getCurrentUser();
    this.isVendor = user?.role === 'Vendor';
    this.isCustomer = user?.role === 'Customer';
    
    this.loadDashboardData();
  }

  loadDashboardData(): void {
    if (this.isCustomer) {
      this.loadMyJobs();
    }
    if (this.isVendor) {
      this.loadMyBids();
      this.loadAssignedJobs();
    }
  }

  loadMyJobs(): void {
    this.isLoadingJobs = true;
    this.jobService.getMyJobs().subscribe({
      next: (data) => {
        this.myJobs = data;
        this.isLoadingJobs = false;
      },
      error: (err) => {
        console.error('Failed to load my jobs', err);
        this.isLoadingJobs = false;
        this.errorMessage = 'Failed to load your jobs.';
      }
    });
  }

  loadMyBids(): void {
    this.isLoadingBids = true;
    this.jobService.getMyBids().subscribe({
      next: (data) => {
        this.myBids = data;
        this.isLoadingBids = false;
      },
      error: (err) => {
        console.error('Failed to load my bids', err);
        this.isLoadingBids = false;
        this.errorMessage = 'Failed to load your bids.';
      }
    });
  }

  loadAssignedJobs(): void {
    this.isLoadingAssigned = true;
    this.jobService.getAssignedJobs().subscribe({
      next: (data) => {
        this.assignedJobs = data;
        this.isLoadingAssigned = false;
      },
      error: (err) => {
        console.error('Failed to load assigned jobs', err);
        this.isLoadingAssigned = false;
        this.errorMessage = 'Failed to load assigned jobs.';
      }
    });
  }

  getStatusColor(status: string): string {
    switch (status) {
      case 'Open': return 'primary';
      case 'Bidding': return 'accent';
      case 'Assigned': return 'warn';
      case 'Completed': return 'success';
      case 'Cancelled': return 'warn';
      default: return '';
    }
  }

  getBidStatusColor(status: string): string {
    switch (status) {
      case 'Pending': return 'accent';
      case 'Accepted': return 'primary';
      case 'Rejected': return 'warn';
      default: return '';
    }
  }
}